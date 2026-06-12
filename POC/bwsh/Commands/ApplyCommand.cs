using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class ApplyCommand
{
    public static Command Build()
    {
        var cmd = new Command("apply",
            "Reconcile a deployment to its manifest (idempotent; re-renders config, preserves secrets).");

        var manifest = new Option<string>("--manifest", "-m")
        { Description = "Path to the YAML manifest.", Required = true };
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var pull = new Option<bool>("--pull")
        { Description = "Always pull images, even when present locally (for moving tags like :dev)." };

        cmd.Options.Add(manifest);
        cmd.Options.Add(root);
        cmd.Options.Add(pull);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var manifestPath = parseResult.GetValue(manifest)!;
            if (!File.Exists(manifestPath))
            {
                Cli.Error($"Manifest not found: {manifestPath}");
                return 2;
            }

            var loaded = InstallManifest.Load(manifestPath);
            var kind = Cli.ResolveKind(null, loaded); // kind comes from the manifest
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;

            // Don't layer one deployment over another already installed at --root.
            if (Cli.DetectInstalledKind(rootDir) is { } existing && existing != kind)
            {
                Cli.Error($"{rootDir} already contains a {existing} deployment, but {manifestPath} declares {kind}.");
                return 4; // precondition-failed
            }

            var ctx = new InstallContext { Root = rootDir, Manifest = loaded };

            // Re-render from the manifest (secrets are reused, not regenerated), then recreate the
            // stack to pick up the changes. UpAsync recreates every container, so the stack restarts
            // briefly; data survives via the named volume.
            await dep.GenerateAssetsAsync(ctx, ct);

            using var engine = new DockerDotNetEngine();

            await dep.PreUpAsync(ctx, engine, ct);

            await dep.UpAsync(ctx, engine, $"Bitwarden {kind} — apply", parseResult.GetValue(pull), ct);

            AnsiConsole.MarkupLine($"\n[green]Apply complete.[/] Running at: [link]{Markup.Escape(dep.ResolveUrl(rootDir))}[/]");
            return 0;
        });

        return cmd;
    }
}

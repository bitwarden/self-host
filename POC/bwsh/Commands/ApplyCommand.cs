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
        { Description = "Path to the YAML manifest.", DefaultValueFactory = _ => "bitwarden.yaml" };
        var deployment = new Option<string?>("--deployment", "-d")
        { Description = "standard | lite (defaults to the manifest)." };
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };

        cmd.Options.Add(manifest);
        cmd.Options.Add(deployment);
        cmd.Options.Add(root);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var manifestPath = parseResult.GetValue(manifest)!;
            if (!File.Exists(manifestPath))
            {
                Console.Error.WriteLine($"Manifest not found: {manifestPath}");
                return 2;
            }

            var loaded = InstallManifest.Load(manifestPath);
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), loaded);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;
            var ctx = new InstallContext { Root = rootDir, Manifest = loaded };

            // Re-render config/env from the manifest WITHOUT regenerating secrets (generation reuses
            // on-disk secrets), then recreate the stack so the new env takes effect. UpAsync force-
            // recreates every container, so the whole stack restarts briefly; data survives via the
            // named volume. Selective (only-changed) recreate is a future refinement.
            await dep.GenerateAssetsAsync(ctx, ct);
            var topology = dep.BuildTopology(ctx);

            using var engine = new DockerDotNetEngine();
            var orch = new Orchestrator(engine, dep.Networks);
            await orch.UpAsync(topology, ct, $"Bitwarden {kind} — apply");

            AnsiConsole.MarkupLine($"\n[green]Apply complete.[/] Running at: [link]{Markup.Escape(dep.ResolveUrl(rootDir))}[/]");
            return 0;
        });

        return cmd;
    }
}

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
        var deployment = Cli.DeploymentOption("standard | lite (defaults to the manifest).");
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
                Cli.Error($"Manifest not found: {manifestPath}");
                return 2;
            }

            var loaded = InstallManifest.Load(manifestPath);
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), loaded);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;
            var ctx = new InstallContext { Root = rootDir, Manifest = loaded };

            // Re-render from the manifest (secrets are reused, not regenerated), then recreate the
            // stack to pick up the changes. UpAsync recreates every container, so the stack restarts
            // briefly; data survives via the named volume.
            await dep.GenerateAssetsAsync(ctx, ct);
            var topology = dep.BuildTopology(ctx);

            using var engine = new DockerDotNetEngine();
            var orch = new Orchestrator(engine, dep.Networks);

            if (kind == DeploymentKind.Standard)
                await Setup.LetsEncrypt.ProvisionIfNeeded(engine, rootDir, Setup.StandardConfig.Load(rootDir), loaded.Ssl.Email, ct);

            await orch.UpAsync(topology, ct, $"Bitwarden {kind} — apply");

            AnsiConsole.MarkupLine($"\n[green]Apply complete.[/] Running at: [link]{Markup.Escape(dep.ResolveUrl(rootDir))}[/]");
            return 0;
        });

        return cmd;
    }
}

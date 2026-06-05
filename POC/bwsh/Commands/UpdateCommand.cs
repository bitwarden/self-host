using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class UpdateCommand
{
    public static Command Build()
    {
        var cmd = new Command("update", "Pull target versions and recreate changed containers.");

        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var rebuild = new Option<bool>("--rebuild")
        { Description = "Regenerate config assets before updating (run.sh `rebuild`)." };
        var check = new Option<bool>("--check") { Description = "Only report which services need updating." };
        var pull = new Option<bool>("--pull")
        { Description = "Always pull and recreate, even when the image tag is unchanged (for moving tags like :dev)." };
        var coreVersion = new Option<string?>("--core-version")
        { Description = "Target core image version (api/identity/admin/etc). Defaults to the pinned version." };
        var webVersion = new Option<string?>("--web-version")
        { Description = "Target web vault image version. Defaults to the pinned version." };
        var keyConnectorVersion = new Option<string?>("--key-connector-version")
        { Description = "Target Key Connector image version (when enabled)." };

        cmd.Options.Add(deployment);
        cmd.Options.Add(root);
        cmd.Options.Add(rebuild);
        cmd.Options.Add(check);
        cmd.Options.Add(pull);
        cmd.Options.Add(coreVersion);
        cmd.Options.Add(webVersion);
        cmd.Options.Add(keyConnectorVersion);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var kind = Cli.ResolveInstalledKind(parseResult.GetValue(deployment), parseResult.GetValue(root)!);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;

            // `update` operates on an EXISTING install. If the marker is gone (e.g. after
            // `uninstall --purge`), don't silently re-deploy — point the user at `install`.
            if (!File.Exists(Path.Combine(rootDir, dep.InstalledMarker)))
            {
                Cli.Error($"No {kind} deployment found at {rootDir}. Run `install` first.");
                return 4; // precondition-failed
            }

            // Reconstruct the manifest from on-disk config so a rebuild preserves the deployment's
            // actual URL/SSL/services rather than resetting to defaults. Version flags override the
            // pinned defaults; omitted ones keep the baked version.
            var manifest = dep.ReadManifest(rootDir);
            if (parseResult.GetValue(coreVersion) is { } cv) manifest.CoreVersion = cv;
            if (parseResult.GetValue(webVersion) is { } wv) manifest.WebVersion = wv;
            if (parseResult.GetValue(keyConnectorVersion) is { } kv) manifest.KeyConnectorVersion = kv;
            var ctx = new InstallContext { Root = rootDir, Manifest = manifest };

            if (parseResult.GetValue(rebuild))
                await dep.GenerateAssetsAsync(ctx, ct);

            var topology = dep.BuildTopology(ctx);
            using var engine = new DockerDotNetEngine();
            var orch = new Orchestrator(engine, dep.Networks);

            // --pull forces a pull + recreate regardless of tag: the staleness check compares image
            // reference strings, so a moving tag (:dev) always looks up to date.
            var forcePull = parseResult.GetValue(pull);

            var stale = new List<ServiceSpec>();
            if (!forcePull)
                await AnsiConsole.Status().StartAsync("Checking for updates…", async _ =>
                {
                    foreach (var s in topology)
                        if (await orch.NeedsUpdateAsync(s, ct)) stale.Add(s);
                });

            if (!forcePull && stale.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]All services up to date.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine(forcePull
                ? "[yellow]Forcing pull and recreate of all services…[/]"
                : $"Services needing update: [yellow]{string.Join(", ", stale.Select(s => s.Name))}[/]");
            if (parseResult.GetValue(check)) return 0;

            // DB migrations run themselves: the admin service migrates on startup (self-hosted).
            await orch.UpAsync(topology, ct, $"Bitwarden {kind} — update", forcePull); // idempotent recreate
            AnsiConsole.MarkupLine("\n[green]Update complete.[/]");
            return 0;
        });

        return cmd;
    }
}

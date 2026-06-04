using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class MigrateCommand
{
    public static Command Build()
    {
        var cmd = new Command("migrate", "Adopt an existing bash/compose install under CLI management (non-destructive).");

        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var yes = new Option<bool>("--yes", "-y") { Description = "Skip confirmation." };
        var noBackup = new Option<bool>("--no-backup") { Description = "Skip the automatic pre-migration backup." };
        var pruneNetworks = new Option<bool>("--prune-networks") { Description = "Remove the orphaned compose networks afterward." };

        cmd.Options.Add(deployment);
        cmd.Options.Add(root);
        cmd.Options.Add(yes);
        cmd.Options.Add(noBackup);
        cmd.Options.Add(pruneNetworks);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), null);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;

            if (!File.Exists(Path.Combine(rootDir, dep.InstalledMarker)))
            {
                Console.Error.WriteLine($"No {kind} deployment found at {rootDir}. Nothing to migrate.");
                return 4;
            }
            if (kind != DeploymentKind.Standard)
            {
                Console.Error.WriteLine("migrate supports standard deployments only in v1.");
                return 4;
            }

            // Built-in MSSQL must be a host bind we can re-mount. A bash-Linux install keeps it at
            // {root}/mssql/data; a Docker-named-volume DB (common on macOS) can't be safely adopted yet.
            var dbDataDir = Path.Combine(rootDir, "mssql", "data");
            if (!Directory.Exists(dbDataDir))
            {
                Console.Error.WriteLine(
                    $"Could not locate an on-disk mssql/data bind under {rootDir} (looks like a named-volume " +
                    "database, common on macOS). Automatic DB adoption isn't supported in v1 — aborting to avoid data loss.");
                return 4;
            }

            using var engine = new DockerDotNetEngine();

            // Pin to the versions currently running (migrate ≠ upgrade); missing → pinned defaults.
            var manifest = new InstallManifest();
            if (await Cli.ImageTagAsync(engine, "bitwarden-api", ct) is { } core) manifest.CoreVersion = core;
            if (await Cli.ImageTagAsync(engine, "bitwarden-web", ct) is { } web) manifest.WebVersion = web;
            if (await Cli.ImageTagAsync(engine, "bitwarden-key-connector", ct) is { } kc) manifest.KeyConnectorVersion = kc;

            var ctx = new InstallContext { Root = rootDir, Manifest = manifest };
            var topology = AdoptStandard(dep.BuildTopology(ctx), rootDir);

            // Preview + confirm.
            AnsiConsole.MarkupLine($"[bold]Migrate[/] the install at [grey]{Markup.Escape(rootDir)}[/] to CLI management:");
            AnsiConsole.MarkupLine($"  • recreate {topology.Count} containers (brief downtime), moving them onto the [green]bitwarden-*[/] networks");
            AnsiConsole.MarkupLine($"  • preserve the database at [green]{Markup.Escape(dbDataDir)}[/] and reuse existing config/secrets");
            AnsiConsole.MarkupLine($"  • pin to: core [green]{manifest.CoreVersion}[/], web [green]{manifest.WebVersion}[/]");
            AnsiConsole.MarkupLine(parseResult.GetValue(noBackup)
                ? "  • [yellow]--no-backup: skipping the pre-migration backup[/]"
                : "  • take a full backup first");

            if (!parseResult.GetValue(yes))
            {
                if (!AnsiConsole.Confirm("Continue?", defaultValue: false))
                {
                    Console.WriteLine("Migration canceled.");
                    return 3;
                }
            }

            // Auto-backup before touching anything.
            if (!parseResult.GetValue(noBackup))
            {
                var archive = await BackupCommand.RunAsync(engine, kind, rootDir, null, ct);
                AnsiConsole.MarkupLine($"Backed up to [green]{Markup.Escape(archive)}[/] before migrating.\n");
            }

            // Takeover: force-remove the compose containers and recreate them under CLI management,
            // reusing the existing config/env/data (no asset generation, no secret regeneration).
            var orch = new Orchestrator(engine, dep.Networks);
            await orch.UpAsync(topology, ct, "Bitwarden — migrate");

            if (parseResult.GetValue(pruneNetworks))
            {
                foreach (var net in new[] { "bwdata_default", "bwdata_public" })
                {
                    await engine.RemoveNetworkAsync(net, ct);
                    AnsiConsole.MarkupLine($"[grey]pruned orphaned network {net} (if present)[/]");
                }
            }

            AnsiConsole.MarkupLine("\n[green]Migration complete.[/]");
            Cli.WriteCommandHelp();
            return 0;
        });

        return cmd;
    }

    /// <summary>
    /// Repoints the mssql data mount (named volume → the existing host bind) for an adopted install.
    /// Nginx host ports come from config.yml via BuildTopology, so nothing else needs rewriting.
    /// </summary>
    internal static IReadOnlyList<ServiceSpec> AdoptStandard(IReadOnlyList<ServiceSpec> topology, string root)
    {
        return topology.Select(spec => spec.Name == "mssql"
            ? spec with
            {
                Binds = spec.Binds
                    .Select(b => b.Container == "/var/opt/mssql/data" ? ($"{root}/mssql/data", b.Container) : b)
                    .ToArray(),
            }
            : spec).ToList();
    }
}

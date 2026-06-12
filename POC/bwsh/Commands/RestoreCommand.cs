using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Bit.SelfHost.Setup;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class RestoreCommand
{
    public static Command Build()
    {
        var cmd = new Command("restore", "Restore a deployment from a backup .tar.gz.");

        var archive = new Argument<string>("archive") { Description = "Path to a backup .tar.gz." };
        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Target data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var yes = new Option<bool>("--yes", "-y") { Description = "Skip confirmation." };
        var force = new Option<bool>("--force") { Description = "Allow restoring into a non-empty data directory." };

        cmd.Arguments.Add(archive);
        cmd.Options.Add(deployment);
        cmd.Options.Add(root);
        cmd.Options.Add(yes);
        cmd.Options.Add(force);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var archivePath = parseResult.GetValue(archive)!;
            var kind = Cli.ResolveInstalledKind(parseResult.GetValue(deployment), parseResult.GetValue(root)!);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;

            if (!File.Exists(archivePath))
            {
                Cli.Error($"Archive not found: {archivePath}");
                return 2;
            }
            if (File.Exists(Path.Combine(rootDir, dep.InstalledMarker)) && !parseResult.GetValue(force))
            {
                Cli.Error($"{rootDir} already holds a {kind} deployment. Use --force to overwrite it.");
                return 4;
            }

            if (!parseResult.GetValue(yes))
            {
                AnsiConsole.MarkupLine($"Restore [green]{Markup.Escape(archivePath)}[/] into [grey]{Markup.Escape(rootDir)}[/] and bring the stack up.");
                if (!AnsiConsole.Confirm("Continue?", defaultValue: false))
                {
                    AnsiConsole.MarkupLine("[yellow]Restore canceled.[/]");
                    return 3;
                }
            }

            // 1. Unpack config, secrets, certs, attachments, and the .BAK into the target.
            Archive.Unpack(archivePath, rootDir);

            using var engine = new DockerDotNetEngine();
            var orch = new Orchestrator(engine, dep.Networks);
            var ctx = new InstallContext { Root = rootDir, Manifest = new InstallManifest() };
            var topology = dep.BuildTopology(ctx);

            // 2. Deployment-specific restore step before the app services connect. Standard brings up
            //    mssql alone and restores the .BAK.
            await dep.PostUnpackAsync(rootDir, orch, topology, engine, ct);

            // 3. Bring up the full stack. App services connect to the restored DB; admin migrates forward if needed.
            await dep.UpAsync(ctx, engine, $"Bitwarden {kind} — restore", forcePull: false, ct);

            AnsiConsole.MarkupLine($"\n[green]Restore complete.[/] Running at: [link]{Markup.Escape(dep.ResolveUrl(rootDir))}[/]");
            return 0;
        });

        return cmd;
    }
}

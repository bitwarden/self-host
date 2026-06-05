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

            // 1. Unpack config/secrets/certs/attachments (+ the .BAK for standard) into the target.
            Archive.Unpack(archivePath, rootDir);

            using var engine = new DockerDotNetEngine();
            var orch = new Orchestrator(engine, dep.Networks);
            var ctx = new InstallContext { Root = rootDir, Manifest = new InstallManifest() };
            var topology = dep.BuildTopology(ctx);

            if (kind == DeploymentKind.Standard)
            {
                // 2. Bring up mssql alone (fresh data volume), then RESTORE the .BAK into it before the
                //    app services connect (RESTORE needs no active connections).
                var mssql = topology.First(s => s.Name == "mssql");
                await orch.UpAsync([mssql], ct, "Bitwarden — restore (database)");
                await RestoreDatabaseAsync(engine, rootDir, ct);
            }

            // 3. Bring up the full stack (app services connect to the restored DB; admin migrates forward if needed).
            await orch.UpAsync(topology, ct, $"Bitwarden {kind} — restore");

            AnsiConsole.MarkupLine($"\n[green]Restore complete.[/] Running at: [link]{Markup.Escape(dep.ResolveUrl(rootDir))}[/]");
            return 0;
        });

        return cmd;
    }

    private static async Task RestoreDatabaseAsync(IContainerEngine engine, string root, CancellationToken ct)
    {
        var backupsDir = Path.Combine(root, "mssql", "backups");
        var bak = Directory.Exists(backupsDir)
            ? Directory.EnumerateFiles(backupsDir, "*.BAK").OrderBy(File.GetLastWriteTimeUtc).LastOrDefault()
            : null;
        if (bak is null)
        {
            AnsiConsole.MarkupLine("[yellow]No .BAK in the archive — skipping database restore (files restored only).[/]");
            return;
        }

        var (db, saPw) = ReadDbCreds(root);
        var diskPath = $"/etc/bitwarden/mssql/backups/{Path.GetFileName(bak)}";
        // Both the database name and the .BAK filename originate from the (untrusted) archive, so escape
        // them for their T-SQL contexts: bracket identifier (] -> ]]) and string literal (' -> '').
        var dbEscaped = db.Replace("]", "]]");
        var diskEscaped = diskPath.Replace("'", "''");
        string[] cmd =
        [
            "/opt/mssql-tools18/bin/sqlcmd", "-S", "localhost", "-U", "sa", "-P", saPw, "-C",
            "-Q", $"RESTORE DATABASE [{dbEscaped}] FROM DISK = N'{diskEscaped}' WITH REPLACE",
        ];

        // mssql may report healthy a moment before it accepts the restore; retry briefly.
        await AnsiConsole.Status().StartAsync("Restoring database…", async _ =>
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var output = await engine.ExecAsync("bitwarden-mssql", cmd, ct);
                    if (output.Contains("RESTORE DATABASE successfully", StringComparison.OrdinalIgnoreCase)) return;
                    if (attempt >= 5) throw new InvalidOperationException(output.Trim());
                }
                catch when (attempt < 5) { /* retry */ }
                await Task.Delay(3000, ct);
            }
        });
    }

    private static (string Database, string SaPassword) ReadDbCreds(string root)
    {
        var path = Path.Combine(root, "env", "mssql.override.env");
        string db = "vault", pw = "";
        if (File.Exists(path))
        {
            foreach (var line in File.ReadLines(path))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var (k, v) = (line[..eq], line[(eq + 1)..]);
                if (k == "DATABASE") db = v;
                else if (k == "SA_PASSWORD") pw = v;
            }
        }
        return (db, pw);
    }
}

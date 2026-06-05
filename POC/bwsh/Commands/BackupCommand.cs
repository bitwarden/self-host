using System.CommandLine;
using System.Globalization;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Bit.SelfHost.Setup;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class BackupCommand
{
    // Subtrees never archived: logs (noise) and the raw mssql data dir (the .bak is the DB snapshot).
    private static readonly string[] Exclude = ["logs", "mssql/data"];

    public static Command Build()
    {
        var cmd = new Command("backup", "Back up a deployment (config, secrets, certs, attachments + a DB dump) to a .tar.gz.");

        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var output = new Option<string?>("--out", "-o")
        { Description = "Archive path or directory (default: ./bitwarden-backup-<timestamp>.tar.gz)." };

        cmd.Options.Add(deployment);
        cmd.Options.Add(root);
        cmd.Options.Add(output);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var kind = Cli.ResolveInstalledKind(parseResult.GetValue(deployment), parseResult.GetValue(root)!);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;

            if (!File.Exists(Path.Combine(rootDir, dep.InstalledMarker)))
            {
                Cli.Error($"No {kind} deployment found at {rootDir}. Nothing to back up.");
                return 4;
            }

            var path = await RunAsync(new DockerDotNetEngine(), dep, rootDir, parseResult.GetValue(output), ct);
            AnsiConsole.MarkupLine($"\nBacked up to [green]{Markup.Escape(path)}[/]");
            AnsiConsole.MarkupLine("[yellow]This archive contains secrets and your full database — store it securely.[/]");
            return 0;
        });

        return cmd;
    }

    /// <summary>
    /// Runs the deployment's pre-backup hook and packs the data dir into a .tar.gz. Reusable so
    /// `migrate` can snapshot before it recreates. Returns the archive path. Orchestration-agnostic:
    /// it operates by container name + filesystem, so it works on a bash/compose or CLI-managed install.
    /// </summary>
    public static async Task<string> RunAsync(IContainerEngine engine, IDeployment dep, string root, string? outPath, CancellationToken ct)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var archive = ResolveOut(outPath, stamp);

        await dep.PreBackupAsync(root, engine, ct);

        await AnsiConsole.Status().StartAsync("Packaging archive…", async _ =>
        {
            var versions = string.Join(", ",
                (await dep.GatherVersionsAsync(engine, ct)).Select(v => $"{v.Label} {v.Version}"));
            var manifest =
                $"deployment: {dep.Kind}\n" +
                $"created:    {DateTime.Now:O}\n" +
                $"versions:   {versions}\n" +
                $"excludes:   {string.Join(", ", Exclude)}\n";

            Archive.Pack(root, archive, manifest, Exclude);
        });

        return archive;
    }

    private static string ResolveOut(string? outPath, string stamp)
    {
        var name = $"bitwarden-backup-{stamp}.tar.gz";
        if (string.IsNullOrEmpty(outPath)) return Path.Combine(Directory.GetCurrentDirectory(), name);
        if (outPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return outPath;
        if (outPath.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) return outPath;
        return Path.Combine(outPath, name); // treat as a directory
    }
}

using System.CommandLine;
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
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), null);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;

            if (!File.Exists(Path.Combine(rootDir, dep.InstalledMarker)))
            {
                Console.Error.WriteLine($"No {kind} deployment found at {rootDir}. Nothing to back up.");
                return 4;
            }

            var path = await RunAsync(new DockerDotNetEngine(), kind, rootDir, parseResult.GetValue(output), ct);
            AnsiConsole.MarkupLine($"\nBacked up to [green]{Markup.Escape(path)}[/]");
            AnsiConsole.MarkupLine("[yellow]This archive contains secrets and your full database — store it securely.[/]");
            return 0;
        });

        return cmd;
    }

    /// <summary>
    /// Dumps the DB (standard) and packs the data dir into a .tar.gz. Reusable so `migrate` can
    /// snapshot before it recreates. Returns the archive path. Orchestration-agnostic: it operates
    /// by container name + filesystem, so it works on a bash/compose or CLI-managed install.
    /// </summary>
    public static async Task<string> RunAsync(IContainerEngine engine, DeploymentKind kind, string root, string? outPath, CancellationToken ct)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var archive = ResolveOut(outPath, stamp);

        await AnsiConsole.Status().StartAsync("Backing up…", async statusCtx =>
        {
            // Standard: trigger the mssql image's online backup (writes a .BAK to mssql/backups,
            // which is bind-mounted to {root}/mssql/backups). Lite stores sqlite in {root} directly.
            if (kind == DeploymentKind.Standard && (await engine.InspectAsync("bitwarden-mssql", ct)).Running)
            {
                statusCtx.Status("Dumping database…");
                try
                {
                    await engine.ExecAsync("bitwarden-mssql", ["/backup-db.sh"], ct);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Database dump skipped: {Markup.Escape(ex.Message)}[/]");
                }

                if (!Directory.EnumerateFiles(Path.Combine(root, "mssql", "backups"), "*.BAK").Any())
                    AnsiConsole.MarkupLine("[yellow]Warning: no .BAK was produced — archive will not contain a database backup.[/]");
            }
            else if (kind == DeploymentKind.Standard)
            {
                AnsiConsole.MarkupLine("[yellow]bitwarden-mssql not running — backing up files only (no fresh DB dump).[/]");
            }

            statusCtx.Status("Packaging archive…");
            var versions = string.Join(", ",
                await CollectAsync(engine, kind, ct));
            var manifest =
                $"deployment: {kind}\n" +
                $"created:    {DateTime.Now:O}\n" +
                $"versions:   {versions}\n" +
                $"excludes:   {string.Join(", ", Exclude)}\n";

            Archive.Pack(root, archive, manifest, Exclude);
        });

        return archive;
    }

    private static async Task<IEnumerable<string>> CollectAsync(IContainerEngine engine, DeploymentKind kind, CancellationToken ct)
    {
        if (kind == DeploymentKind.Lite)
            return [$"lite {await Cli.ImageTagAsync(engine, LiteDeployment.ContainerName, ct) ?? "?"}"];

        var parts = new List<string>();
        if (await Cli.ImageTagAsync(engine, "bitwarden-api", ct) is { } core) parts.Add($"core {core}");
        if (await Cli.ImageTagAsync(engine, "bitwarden-web", ct) is { } web) parts.Add($"web {web}");
        if (await Cli.ImageTagAsync(engine, "bitwarden-key-connector", ct) is { } kc) parts.Add($"key-connector {kc}");
        return parts;
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

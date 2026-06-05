using System.CommandLine;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Docker.DotNet;

namespace Bit.SelfHost.Commands;

public static class LogsCommand
{
    public static Command Build()
    {
        var cmd = new Command("logs", "Show or export logs for a deployment.");

        var service = new Argument<string?>("service")
        {
            Description = "Service name (e.g. identity). Omit for the whole container.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        service.CompletionSources.Add(Cli.ServiceNames);
        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var tail = new Option<int>("--tail")
        { Description = "Number of lines to show.", DefaultValueFactory = _ => 200 };
        var all = new Option<bool>("--all") { Description = "Show the full log, not just the tail." };
        var export = new Option<string?>("--export")
        {
            Description = "Bundle the FULL logs of every service into a .zip (give a .zip path, or a " +
                          "directory to drop a timestamped zip in). For attaching to a bug report.",
        };

        cmd.Arguments.Add(service);
        cmd.Options.Add(deployment);
        cmd.Options.Add(root);
        cmd.Options.Add(tail);
        cmd.Options.Add(all);
        cmd.Options.Add(export);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), null);
            var dep = DeploymentFactory.Create(kind);
            var svc = parseResult.GetValue(service);
            var lines = parseResult.GetValue(all) ? 0 : parseResult.GetValue(tail); // 0 => full log
            var exportPath = parseResult.GetValue(export);
            var rootDir = parseResult.GetValue(root)!;

            using var engine = new DockerDotNetEngine();
            try
            {
                if (exportPath is not null)
                    return await ExportZipAsync(engine, kind, dep, rootDir, exportPath, ct);

                // Bare `logs` on Lite = whole-container (supervisord) output.
                if (kind == DeploymentKind.Lite && string.IsNullOrEmpty(svc))
                {
                    Console.WriteLine(await engine.ContainerLogsAsync(LiteDeployment.ContainerName, lines, ct));
                    return 0;
                }

                if (string.IsNullOrEmpty(svc))
                {
                    var names = await ListServicesAsync(engine, kind, dep, rootDir, ct);
                    Console.Error.WriteLine($"Specify a service: {string.Join(", ", names)}");
                    return 2;
                }

                Console.WriteLine(await FetchAsync(engine, kind, svc, lines, ct));
                return 0;
            }
            catch (DockerContainerNotFoundException)
            {
                Console.Error.WriteLine($"Container not found — is the {kind} deployment running? Try `status`.");
                return 2;
            }
        });

        return cmd;
    }

    private static async Task<int> ExportZipAsync(
        IContainerEngine engine, DeploymentKind kind, IDeployment dep, string rootDir, string exportPath, CancellationToken ct)
    {
        var zipPath = ResolveZipPath(exportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);

        var services = await ListServicesAsync(engine, kind, dep, rootDir, ct);

        await using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var manifest = new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"deployment: {kind}")
                .AppendLine(CultureInfo.InvariantCulture, $"generated:  {DateTime.Now:O}")
                .AppendLine(CultureInfo.InvariantCulture, $"services:   {string.Join(", ", services)}")
                .ToString();
            await WriteEntryAsync(zip, "manifest.txt", manifest, ct);

            foreach (var s in services)
            {
                var content = await FetchAsync(engine, kind, s, tail: 0, ct);
                await WriteEntryAsync(zip, $"{s}.log", content, ct);
                Console.WriteLine($"  + {s}.log  ({content.Length:N0} bytes)");
            }
        }

        Console.WriteLine($"\nBundled {services.Count} log(s) into {Path.GetFullPath(zipPath)}");
        return 0;
    }

    /// <summary>A .zip path is used as-is; a directory gets a timestamped zip dropped inside.</summary>
    private static string ResolveZipPath(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.Combine(path, $"bitwarden-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

    private static async Task WriteEntryAsync(ZipArchive zip, string name, string content, CancellationToken ct)
    {
        await using var writer = new StreamWriter(zip.CreateEntry(name).Open());
        await writer.WriteAsync(content.AsMemory(), ct);
    }

    /// <summary>Fetch one service's log. tail &lt;= 0 means the full log.</summary>
    private static Task<string> FetchAsync(IContainerEngine engine, DeploymentKind kind, string svc, int tail, CancellationToken ct)
    {
        if (kind == DeploymentKind.Lite)
        {
            var path = $"/var/log/bitwarden/{svc}.log";
            string[] cmd = tail <= 0 ? ["cat", path] : ["tail", "-n", tail.ToString(CultureInfo.InvariantCulture), path];
            return engine.ExecAsync(LiteDeployment.ContainerName, cmd, ct);
        }
        return engine.ContainerLogsAsync($"bitwarden-{svc}", tail, ct); // standard: one container per service
    }

    /// <summary>The services whose logs exist for this deployment.</summary>
    private static async Task<IReadOnlyList<string>> ListServicesAsync(
        IContainerEngine engine, DeploymentKind kind, IDeployment dep, string root, CancellationToken ct)
    {
        if (kind == DeploymentKind.Lite)
        {
            var ls = await engine.ExecAsync(LiteDeployment.ContainerName,
                ["sh", "-c", "ls /var/log/bitwarden/*.log 2>/dev/null || true"], ct);
            return ls.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .Distinct()
                .ToList();
        }
        var ctx = new InstallContext { Root = root, Manifest = new InstallManifest() };
        return dep.BuildTopology(ctx).Select(s => s.Name).ToList();
    }
}

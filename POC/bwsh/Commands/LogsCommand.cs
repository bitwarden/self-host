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
            var kind = Cli.ResolveInstalledKind(parseResult.GetValue(deployment), parseResult.GetValue(root)!);
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

                if (string.IsNullOrEmpty(svc))
                {
                    // Some deployments expose a single aggregate stream; others need a service name.
                    if (dep.SupportsAggregateLogs)
                    {
                        Console.WriteLine(await dep.FetchLogAsync(null, lines, engine, ct));
                        return 0;
                    }
                    var names = await dep.ListLogServicesAsync(rootDir, engine, ct);
                    Cli.Error($"Specify a service: {string.Join(", ", names)}");
                    return 2;
                }

                Console.WriteLine(await dep.FetchLogAsync(svc, lines, engine, ct));
                return 0;
            }
            catch (DockerContainerNotFoundException)
            {
                Cli.Error($"Container not found — is the {kind} deployment running? Try `status`.");
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

        var services = await dep.ListLogServicesAsync(rootDir, engine, ct);

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
                var content = await dep.FetchLogAsync(s, tail: 0, engine, ct);
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
}

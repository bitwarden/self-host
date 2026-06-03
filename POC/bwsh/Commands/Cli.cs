using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

/// <summary>Shared helpers for the command layer.</summary>
public static class Cli
{
    /// <summary>The image tag of a running container (e.g. "2026.4.1"), or null if it isn't present.</summary>
    public static async Task<string?> ImageTagAsync(IContainerEngine engine, string container, CancellationToken ct)
    {
        try
        {
            var image = await engine.ImageOfAsync(container, ct);
            var slash = image.LastIndexOf('/');
            var colon = image.LastIndexOf(':');
            return colon > slash ? image[(colon + 1)..] : null;
        }
        catch
        {
            return null; // container not present
        }
    }

    /// <summary>
    /// Prints the management commands after an install, kubectl-style: verb + one-line description,
    /// with flag details deferred to per-command --help rather than inline parentheticals.
    /// </summary>
    public static void WriteCommandHelp()
    {
        var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadLeft(2).PadRight(4)).AddColumn();
        grid.AddRow("[green]status[/]", "Show service health");
        grid.AddRow("[green]logs[/]", "Show or export service logs");
        grid.AddRow("[green]config[/]", "Get or set configuration");
        grid.AddRow("[green]update[/]", "Pull the latest images and recreate");
        grid.AddRow("[green]uninstall[/]", "Stop and remove the deployment");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Available commands:[/]");
        AnsiConsole.Write(grid);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("""Run [green]bwsh <command> --help[/] [grey]for details.[/]""");
    }

    /// <summary>Resolve which deployment to act on: explicit --deployment wins, else the manifest, else standard.</summary>
    public static DeploymentKind ResolveKind(string? deploymentFlag, InstallManifest? manifest) =>
        deploymentFlag is not null ? DeploymentFactory.Parse(deploymentFlag)
        : manifest is not null ? DeploymentFactory.Parse(manifest.Deployment)
        : DeploymentKind.Standard;

    public static void ApplyManifestValue(InstallManifest m, string key, string value)
    {
        switch (key)
        {
            case "domain": m.Domain = value; break;
            case "region": m.Region = value; break;
            case "installation-id": m.InstallationId = value; break;
            case "installation-key": m.InstallationKey = value; break;
            case "database": m.Database = value; break;
            case "db-provider": m.DbProvider = value; break;
            default: m.Config[key] = value; break;
        }
    }

    /// <summary>Upsert a KEY=VALUE line into an env-style file (creating it if needed).</summary>
    public static void UpsertEnv(string path, string key, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var idx = lines.FindIndex(l => l.TrimStart().StartsWith(key + "="));
        if (idx >= 0) lines[idx] = $"{key}={value}";
        else lines.Add($"{key}={value}");
        File.WriteAllLines(path, lines);
    }
}

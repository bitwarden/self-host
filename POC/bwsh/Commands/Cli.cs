using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

/// <summary>Shared helpers for the command layer.</summary>
public static class Cli
{
    private static readonly IAnsiConsole ErrorConsole =
        AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(Console.Error) });

    /// <summary>Write a red error line to stderr (degrades to plain text when redirected).</summary>
    public static void Error(string message) => ErrorConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");

    /// <summary>Service names across both deployments, for `logs &lt;service&gt;` tab completion.</summary>
    public static readonly string[] ServiceNames =
    [
        "mssql", "web", "attachments", "api", "identity", "sso", "admin", "icons",
        "notifications", "events", "nginx", "key-connector", "scim",
    ];

    /// <summary>The shared `--deployment` option, with standard|lite validation + tab completion.</summary>
    public static Option<string?> DeploymentOption(string description = "standard | lite.")
    {
        var option = new Option<string?>("--deployment", "-d") { Description = description };
        option.AcceptOnlyFromAmong("standard", "lite");
        return option;
    }

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

    /// <summary>
    /// Detect which deployment is installed under <paramref name="root"/> by probing each
    /// deployment's marker file (standard: config.yml, lite: settings.env). Returns null when
    /// neither is present (nothing installed yet).
    /// </summary>
    public static DeploymentKind? DetectInstalledKind(string root)
    {
        foreach (var kind in Enum.GetValues<DeploymentKind>())
            if (File.Exists(Path.Combine(root, DeploymentFactory.Create(kind).InstalledMarker)))
                return kind;
        return null;
    }

    /// <summary>
    /// Resolve the deployment to act on for an EXISTING install (status/logs/update/etc.):
    /// explicit --deployment wins, else auto-detect from on-disk markers, else fall back to
    /// standard so the usual "not found" message still surfaces for an empty directory.
    /// </summary>
    public static DeploymentKind ResolveInstalledKind(string? deploymentFlag, string root) =>
        deploymentFlag is not null ? DeploymentFactory.Parse(deploymentFlag)
        : DetectInstalledKind(root) ?? DeploymentKind.Standard;

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

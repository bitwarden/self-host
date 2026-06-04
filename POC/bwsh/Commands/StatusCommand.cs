using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class StatusCommand
{
    public static Command Build()
    {
        var cmd = new Command("status", "Show the running state of a deployment's services.");

        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };

        cmd.Options.Add(deployment);
        cmd.Options.Add(root);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), null);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;

            if (!File.Exists(Path.Combine(rootDir, dep.InstalledMarker)))
            {
                AnsiConsole.MarkupLine($"No [yellow]{kind}[/] deployment found at [grey]{Markup.Escape(rootDir)}[/]. Run [green]install[/] first.");
                return 4;
            }

            var ctx = new InstallContext { Root = rootDir, Manifest = new InstallManifest() };
            var topology = dep.BuildTopology(ctx);

            using var engine = new DockerDotNetEngine();

            var table = new Table()
                .Title($"[bold]{kind}[/] deployment")
                .Border(TableBorder.Rounded)
                .AddColumns("Service", "Container", "State", "Health");

            foreach (var s in topology)
            {
                var st = await engine.InspectAsync(s.ContainerName, ct);
                table.AddRow(
                    Markup.Escape(s.Name),
                    Markup.Escape(s.ContainerName),
                    StateMarkup(st),
                    st.Health is null ? "[grey]-[/]" : HealthMarkup(st.Health));
            }
            AnsiConsole.Write(table);

            // Lite runs every service under supervisord inside one container, so the real per-service
            // state (e.g. identity FATAL — the #373 signal) lives there, not in the container state.
            if (kind == DeploymentKind.Lite)
            {
                var container = topology[0].ContainerName;
                if ((await engine.InspectAsync(container, ct)).Running)
                {
                    try
                    {
                        var output = await engine.ExecAsync(container, ["supervisorctl", "status"], ct);
                        AnsiConsole.Write(SupervisordTable(container, output));
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[grey](could not query supervisord: {Markup.Escape(ex.Message)})[/]");
                    }
                }
            }

            var url = dep.ResolveUrl(rootDir);
            AnsiConsole.MarkupLine($"\n[bold]Bitwarden[/] running at: [link]{Markup.Escape(url)}[/]");

            var versions = await GatherVersionsAsync(engine, kind, ct);
            if (versions.Count > 0)
            {
                AnsiConsole.MarkupLine("[bold]Versions:[/]");
                var grid = new Grid().AddColumn(new GridColumn().PadLeft(2).PadRight(3)).AddColumn();
                foreach (var (label, version) in versions)
                    grid.AddRow($"[grey]{label}:[/]", $"[green]{Markup.Escape(version)}[/]");
                AnsiConsole.Write(grid);
            }
            return 0;
        });

        return cmd;
    }

    private static string StateMarkup(ContainerState st)
    {
        if (!st.Exists) return "[grey]not created[/]";
        return st.Running ? $"[green]{st.Status}[/]" : $"[red]{st.Status}[/]";
    }

    private static string HealthMarkup(string health) => health switch
    {
        "healthy" => "[green]healthy[/]",
        "starting" => "[yellow]starting[/]",
        _ => $"[red]{Markup.Escape(health)}[/]",
    };

    /// <summary>
    /// Deployed versions, read from running containers' image tags: Web + Core for Standard
    /// (and Key Connector when present, i.e. enabled), or the single image tag for Lite.
    /// </summary>
    private static async Task<IReadOnlyList<(string Label, string Version)>> GatherVersionsAsync(
        IContainerEngine engine, DeploymentKind kind, CancellationToken ct)
    {
        if (kind == DeploymentKind.Lite)
        {
            var v = await Cli.ImageTagAsync(engine, LiteDeployment.ContainerName, ct);
            return v is null ? [] : [("Version", v)];
        }

        var versions = new List<(string, string)>();
        if (await Cli.ImageTagAsync(engine, "bitwarden-web", ct) is { } web) versions.Add(("Web", web));
        if (await Cli.ImageTagAsync(engine, "bitwarden-api", ct) is { } core) versions.Add(("Core", core));
        if (await Cli.ImageTagAsync(engine, "bitwarden-key-connector", ct) is { } kc) versions.Add(("Key Connector", kc));
        return versions;
    }

    private static readonly HashSet<string> KnownStates =
        ["RUNNING", "STARTING", "STOPPED", "STOPPING", "BACKOFF", "EXITED", "FATAL", "UNKNOWN"];

    private static Table SupervisordTable(string container, string supervisorctlOutput)
    {
        var table = new Table()
            .Title($"[bold]{Markup.Escape(container)}[/] — supervisord")
            .Border(TableBorder.Rounded)
            .AddColumns("Service", "State");

        foreach (var line in supervisorctlOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var (name, state) = (parts[0], parts[1]);
            if (!KnownStates.Contains(state)) continue; // skip supervisorctl error/connection lines
            var stateMarkup = state switch
            {
                "RUNNING" => "[green]RUNNING[/]",
                "STARTING" => "[yellow]STARTING[/]",
                "STOPPED" => "[grey]STOPPED[/]",
                _ => $"[red]{state}[/]", // FATAL / EXITED / BACKOFF
            };
            table.AddRow(Markup.Escape(name), stateMarkup);
        }
        return table;
    }
}

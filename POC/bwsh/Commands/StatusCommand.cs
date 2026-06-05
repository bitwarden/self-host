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
            var kind = Cli.ResolveInstalledKind(parseResult.GetValue(deployment), parseResult.GetValue(root)!);
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

            // Some deployments run every service inside one container, where the real per-service
            // state lives in an in-container process manager rather than the container state.
            var procs = await dep.InspectProcessesAsync(engine, ct);
            if (procs.Count > 0)
                AnsiConsole.Write(ProcessTable(procs));

            var url = dep.ResolveUrl(rootDir);
            AnsiConsole.MarkupLine($"\n[bold]Bitwarden[/] running at: [link]{Markup.Escape(url)}[/]");

            var versions = await dep.GatherVersionsAsync(engine, ct);
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

    private static Table ProcessTable(IReadOnlyList<ProcessStatus> procs)
    {
        var table = new Table()
            .Title("[bold]processes[/]")
            .Border(TableBorder.Rounded)
            .AddColumns("Service", "State");

        foreach (var p in procs)
        {
            var stateMarkup = p.State switch
            {
                "RUNNING" => "[green]RUNNING[/]",
                "STARTING" => "[yellow]STARTING[/]",
                "STOPPED" => "[grey]STOPPED[/]",
                _ => $"[red]{p.State}[/]", // FATAL / EXITED / BACKOFF
            };
            table.AddRow(Markup.Escape(p.Name), stateMarkup);
        }
        return table;
    }
}

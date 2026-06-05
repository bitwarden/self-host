using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class UninstallCommand
{
    public static Command Build()
    {
        var cmd = new Command("uninstall", "Stop and remove the deployment's containers.");

        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var purge = new Option<bool>("--purge")
        { Description = "Also delete the data directory and volumes (DESTROYS DATA)." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Skip confirmation." };

        cmd.Options.Add(deployment);
        cmd.Options.Add(root);
        cmd.Options.Add(purge);
        cmd.Options.Add(yes);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var kind = Cli.ResolveInstalledKind(parseResult.GetValue(deployment), parseResult.GetValue(root)!);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;
            var doPurge = parseResult.GetValue(purge);

            if (!parseResult.GetValue(yes))
            {
                var prompt = doPurge
                    ? $"This will REMOVE containers AND DELETE all data under {Markup.Escape(rootDir)}. Continue?"
                    : "This will stop and remove all containers (data preserved). Continue?";
                if (!AnsiConsole.Confirm(prompt, defaultValue: false))
                {
                    AnsiConsole.MarkupLine("[yellow]Uninstall canceled.[/]");
                    return 3; // user-cancel
                }
            }

            var ctx = new InstallContext { Root = rootDir, Manifest = new InstallManifest() };
            var topology = dep.BuildTopology(ctx);
            using var engine = new DockerDotNetEngine();
            var orch = new Orchestrator(engine, dep.Networks);

            await AnsiConsole.Status().StartAsync($"Uninstalling {kind}…", async statusCtx =>
            {
                await orch.DownAsync(topology, doPurge, msg => statusCtx.Status(Markup.Escape(msg)), ct);
                if (doPurge && Directory.Exists(rootDir))
                {
                    statusCtx.Status($"deleting {Markup.Escape(rootDir)}");
                    Directory.Delete(rootDir, recursive: true);
                }
            });

            AnsiConsole.MarkupLine(doPurge
                ? $"\n[green]Uninstall complete.[/] Deleted [grey]{Markup.Escape(rootDir)}[/]."
                : "\n[green]Uninstall complete.[/]");
            return 0;
        });

        return cmd;
    }
}

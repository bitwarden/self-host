using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class InstallCommand
{
    public static Command Build()
    {
        var cmd = new Command("install", "Install a Bitwarden self-host deployment.");

        var deployment = new Option<string?>("--deployment", "-d")
        { Description = "Deployment type: standard | lite. Defaults to the answer file, else standard." };
        var config = new Option<string?>("--config", "-c")
        { Description = "Path to a YAML answer file for an unattended install." };
        var root = new Option<string>("--root")
        { Description = "Target data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var plan = new Option<bool>("--plan")
        { Description = "Dry run: generate assets and print the topology without pulling or starting." };

        cmd.Options.Add(deployment);
        cmd.Options.Add(config);
        cmd.Options.Add(root);
        cmd.Options.Add(plan);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var configPath = parseResult.GetValue(config);
            var deploymentFlag = parseResult.GetValue(deployment);
            var rootDir = parseResult.GetValue(root)!;
            var isPlan = parseResult.GetValue(plan);

            AnswerFile? loaded = configPath is not null ? AnswerFile.Load(configPath) : null;

            // Interactive install needs a real terminal for the Spectre prompts.
            if (loaded is null && !AnsiConsole.Profile.Capabilities.Interactive)
            {
                Console.Error.WriteLine("Interactive install needs a terminal. Use --config <answers.yaml> for an unattended install.");
                return 4;
            }

            // Deployment: explicit flag wins, then the answer file, else prompt for it interactively.
            var kind = deploymentFlag is not null ? DeploymentFactory.Parse(deploymentFlag)
                : loaded is not null ? DeploymentFactory.Parse(loaded.Deployment)
                : Prompts.SelectDeployment();
            var dep = DeploymentFactory.Create(kind);

            var answers = loaded ?? Prompts.Collect(dep);
            var ctx = new InstallContext { Root = rootDir, Answers = answers };

            // TODO(cloud): validate installation id/key against api.bitwarden.com/.eu before proceeding.
            await dep.GenerateAssetsAsync(ctx, ct);

            var topology = dep.BuildTopology(ctx);

            if (isPlan)
            {
                Console.WriteLine($"\nWould pull + start {topology.Count} container(s) in parallel:");
                foreach (var s in topology)
                    Console.WriteLine($"  {s.Name,-14} {s.Image}");
                Console.WriteLine("\n(--plan: nothing pulled or started)");
                return 0;
            }

            using var engine = new DockerDotNetEngine();
            var orch = new Orchestrator(engine, dep.Networks);
            await orch.UpAsync(topology, ct, $"Bitwarden {kind}");
            AnsiConsole.MarkupLine("\n[green]Bitwarden install complete.[/]");
            Cli.WriteCommandHelp();
            return 0;
        });

        return cmd;
    }
}

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

        var deployment = Cli.DeploymentOption("Deployment type: standard | lite. Defaults to the manifest, else standard.");
        var manifest = new Option<string?>("--manifest", "-m")
        { Description = "Path to a YAML install manifest for an unattended install." };
        var root = new Option<string>("--root")
        { Description = "Target data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var plan = new Option<bool>("--plan")
        { Description = "Dry run: generate assets and print the topology without pulling or starting." };

        cmd.Options.Add(deployment);
        cmd.Options.Add(manifest);
        cmd.Options.Add(root);
        cmd.Options.Add(plan);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var manifestPath = parseResult.GetValue(manifest);
            var deploymentFlag = parseResult.GetValue(deployment);
            var rootDir = parseResult.GetValue(root)!;
            var isPlan = parseResult.GetValue(plan);

            InstallManifest? loaded = manifestPath is not null ? InstallManifest.Load(manifestPath) : null;

            // Interactive install needs a real terminal for the Spectre prompts.
            if (loaded is null && !AnsiConsole.Profile.Capabilities.Interactive)
            {
                Cli.Error("Interactive install needs a terminal. Use --manifest <bitwarden.yaml> for an unattended install.");
                return 4;
            }

            // Deployment: explicit flag wins, then the manifest, else prompt for it interactively.
            var kind = deploymentFlag is not null ? DeploymentFactory.Parse(deploymentFlag)
                : loaded is not null ? DeploymentFactory.Parse(loaded.Deployment)
                : Prompts.SelectDeployment();
            var dep = DeploymentFactory.Create(kind);

            var manifestData = loaded ?? Prompts.Collect(dep);
            var ctx = new InstallContext { Root = rootDir, Manifest = manifestData };

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

            await dep.PreUpAsync(ctx, engine, ct);

            await dep.UpAsync(ctx, engine, $"Bitwarden {kind}", forcePull: false, ct);
            AnsiConsole.MarkupLine("\n[green]Bitwarden install complete.[/]");
            AnsiConsole.MarkupLine($"Bitwarden running at: [cyan]{dep.ResolveUrl(rootDir)}[/]");
            Cli.WriteCommandHelp();
            return 0;
        });

        return cmd;
    }
}

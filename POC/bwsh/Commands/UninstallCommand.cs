using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;

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
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), null);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;
            var doPurge = parseResult.GetValue(purge);

            if (!parseResult.GetValue(yes))
            {
                Console.Write(doPurge
                    ? $"This will REMOVE containers AND DELETE all data under {rootDir}. Continue? (y/n): "
                    : "This will stop and remove all containers (data preserved). Continue? (y/n): ");
                if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("y" or "yes"))
                {
                    Console.WriteLine("Uninstall canceled.");
                    return 3; // user-cancel
                }
            }

            var ctx = new InstallContext { Root = rootDir, Manifest = new InstallManifest() };
            var topology = dep.BuildTopology(ctx);
            using var engine = new DockerDotNetEngine();
            var orch = new Orchestrator(engine, dep.Networks);
            await orch.DownAsync(topology, purge: doPurge, ct);

            if (doPurge && Directory.Exists(rootDir))
            {
                Directory.Delete(rootDir, recursive: true);
                Console.WriteLine($"Deleted {rootDir}.");
            }

            Console.WriteLine("Uninstall complete.");
            return 0;
        });

        return cmd;
    }
}

using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Bit.SelfHost.Setup;
using Spectre.Console;

namespace Bit.SelfHost.Commands;

public static class RenewCertCommand
{
    public static Command Build()
    {
        var cmd = new Command("renewcert", "Renew the deployment's Let's Encrypt certificate.");

        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var force = new Option<bool>("--force") { Description = "Force renewal even if not near expiry." };

        cmd.Options.Add(deployment);
        cmd.Options.Add(root);
        cmd.Options.Add(force);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), null);
            if (kind != DeploymentKind.Standard)
            {
                Console.Error.WriteLine("renewcert is for standard deployments; lite manages TLS in-container.");
                return 4;
            }

            var rootDir = parseResult.GetValue(root)!;
            var config = StandardConfig.Load(rootDir);
            if (!config.SslManagedLetsEncrypt)
            {
                Console.Error.WriteLine("This deployment isn't using Let's Encrypt (nothing to renew).");
                return 4;
            }

            using var engine = new DockerDotNetEngine();
            await LetsEncrypt.RenewAsync(engine, rootDir, parseResult.GetValue(force), ct);

            // certbot dropped nginx to free the ports; bring it back on the renewed cert.
            var dep = DeploymentFactory.Create(kind);
            var ctx = new InstallContext { Root = rootDir, Manifest = dep.ReadManifest(rootDir) };
            var nginx = dep.BuildTopology(ctx).Single(s => s.Name == "nginx");
            var orch = new Orchestrator(engine, dep.Networks);
            await orch.UpAsync([nginx], ct, "Bitwarden — renewcert");

            AnsiConsole.MarkupLine("\n[green]Certificate renewed.[/]");
            return 0;
        });

        return cmd;
    }
}

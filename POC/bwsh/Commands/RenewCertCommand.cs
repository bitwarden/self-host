using System.CommandLine;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
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
            var rootDir = parseResult.GetValue(root)!;
            var kind = Cli.ResolveInstalledKind(parseResult.GetValue(deployment), rootDir);
            var dep = DeploymentFactory.Create(kind);
            if (!dep.SupportsCertRenewal)
            {
                Cli.Error($"renewcert isn't supported for the {kind} deployment.");
                return 4;
            }

            using var engine = new DockerDotNetEngine();
            try
            {
                await dep.RenewCertAsync(rootDir, parseResult.GetValue(force), engine, ct);
            }
            catch (InvalidOperationException ex)
            {
                Cli.Error(ex.Message);
                return 4;
            }

            AnsiConsole.MarkupLine("\n[green]Certificate renewed.[/]");
            return 0;
        });

        return cmd;
    }
}

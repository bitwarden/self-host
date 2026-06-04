using System.CommandLine;
using Bit.SelfHost.Deployments;

namespace Bit.SelfHost.Commands;

public static class ConfigCommand
{
    public static Command Build()
    {
        var cmd = new Command("config", "Get or set deployment configuration (e.g. config key=value).");

        var assignment = new Argument<string?>("assignment")
        { Description = "key=value to set, or key to get. Omit with --show to list.", Arity = ArgumentArity.ZeroOrOne };
        var deployment = new Option<string?>("--deployment", "-d") { Description = "standard | lite." };
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };
        var show = new Option<bool>("--show") { Description = "Show resolved config files." };

        cmd.Arguments.Add(assignment);
        cmd.Options.Add(deployment);
        cmd.Options.Add(root);
        cmd.Options.Add(show);

        cmd.SetAction(parseResult =>
        {
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), null);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;
            var arg = parseResult.GetValue(assignment);

            if (parseResult.GetValue(show) || arg is null)
            {
                Console.WriteLine($"{kind} deployment config files (under {rootDir}):");
                Console.WriteLine(kind == DeploymentKind.Standard
                    ? "  config.yml                (structural — `rebuild` to apply)\n" +
                      "  env/global.override.env   (SMTP/admin/globalSettings — `restart` to apply)"
                    : "  settings.env              (all BW_*/globalSettings__* — `restart` to apply)");
                return 0;
            }

            var eq = arg.IndexOf('=');
            var key = eq >= 0 ? arg[..eq] : arg;

            if (!dep.TryResolveConfigKey(key, out var binding))
            {
                Console.Error.WriteLine($"Unknown config key '{key}' for {kind} deployment.");
                return 1;
            }

            if (eq < 0) // get
            {
                Console.WriteLine($"{key} lives in {binding.File} (apply changes with `bwsh apply`).");
                return 0;
            }

            var value = arg[(eq + 1)..];
            var target = Path.Combine(rootDir, binding.File);

            if (binding.File.EndsWith(".env"))
            {
                Cli.UpsertEnv(target, key, value);
                Console.WriteLine($"Set {key} in {binding.File}. Run `bwsh apply` to apply.");
                // The manifest is the source of truth: `apply` re-renders from it, so add durable
                // keys to the manifest's `config:` block (a bare `config set` is a one-off tweak).
                Console.WriteLine("Add it to your manifest's `config:` block to persist across applies.");
            }
            else
            {
                // TODO(setup-replacement): structured YAML edit of config.yml + apply.
                Console.WriteLine($"Would set {key}={value} in {binding.File} (config.yml YAML edit: TODO). Apply with `bwsh apply`.");
            }
            return 0;
        });

        return cmd;
    }
}

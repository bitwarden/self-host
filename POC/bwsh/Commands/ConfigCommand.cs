using System.CommandLine;
using Bit.SelfHost.Deployments;

namespace Bit.SelfHost.Commands;

public static class ConfigCommand
{
    public static Command Build()
    {
        var cmd = new Command("config", "Show the current config, or get/set a key (config key=value).");

        var assignment = new Argument<string?>("assignment")
        { Description = "key=value to set, or key to get. Omit to print the current config.", Arity = ArgumentArity.ZeroOrOne };
        var deployment = Cli.DeploymentOption();
        var root = new Option<string>("--root")
        { Description = "Data directory (bwdata).", DefaultValueFactory = _ => "./bwdata" };

        cmd.Arguments.Add(assignment);
        cmd.Options.Add(deployment);
        cmd.Options.Add(root);

        cmd.SetAction(parseResult =>
        {
            var kind = Cli.ResolveKind(parseResult.GetValue(deployment), null);
            var dep = DeploymentFactory.Create(kind);
            var rootDir = parseResult.GetValue(root)!;
            var arg = parseResult.GetValue(assignment);

            if (arg is null)
                return PrintConfig(dep, kind, rootDir);

            var eq = arg.IndexOf('=');
            var key = eq >= 0 ? arg[..eq] : arg;

            if (!dep.TryResolveConfigKey(key, out var binding))
            {
                Cli.Error($"Unknown config key '{key}' for {kind} deployment.");
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
                // apply re-renders from the manifest, so add durable keys to its `config:` block;
                // a bare `bwsh config key=value` is a one-off tweak.
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

    /// <summary>Prints the deployment's on-disk config files, redacting secret values.</summary>
    private static int PrintConfig(IDeployment dep, DeploymentKind kind, string rootDir)
    {
        var printed = false;
        foreach (var rel in dep.ConfigFiles)
        {
            var path = Path.Combine(rootDir, rel);
            if (!File.Exists(path)) continue;
            printed = true;

            Console.WriteLine($"# {rel}");
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.TrimEnd();
                var eq = line.IndexOf('=');
                if (eq > 0 && !line.TrimStart().StartsWith('#'))
                {
                    var key = line[..eq];
                    Console.WriteLine(IsSecret(key.Trim()) ? $"{key}=********" : line);
                }
                else
                {
                    Console.WriteLine(line); // YAML (config.yml), comments, blanks
                }
            }
            Console.WriteLine();
        }

        if (!printed)
        {
            Cli.Error($"No {kind} deployment config found under {rootDir}. Run `install` first.");
            return 4;
        }
        return 0;
    }

    /// <summary>Values worth hiding: anything that ends in a key, or names a password/connection string.</summary>
    internal static bool IsSecret(string key)
    {
        var k = key.ToLowerInvariant();
        return k.Contains("password") || k.Contains("connectionstring") || k.EndsWith("key");
    }
}

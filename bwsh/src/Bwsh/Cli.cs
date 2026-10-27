using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;

namespace Bit.SelfHost;

internal static class Cli
{
    // bwsh's own version, read from this assembly rather than whichever program started the process.
    internal static string Version { get; } = typeof(Cli).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    // Cuts the commit hash the SDK appends (0.1.0+50bbd83b8dd2...) to its first seven characters.
    internal static string ShortVersion(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 || version.Length - plus - 1 <= 7 ? version : version[..(plus + 8)];
    }

    internal static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("bwsh: install and manage a Bitwarden self-hosted server.");

        var versionOption = root.Options.OfType<VersionOption>().Single();
        versionOption.Action = new PrintVersionAction();

        return root;
    }

    private sealed class PrintVersionAction : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            var output = parseResult.InvocationConfiguration.Output;
            output.WriteLine($"bwsh {ShortVersion(Version)}");
            output.WriteLine(StackVersions.Embedded);
            return 0;
        }
    }
}

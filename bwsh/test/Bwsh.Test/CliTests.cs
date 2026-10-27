using System.CommandLine;
using System.Reflection;

namespace Bit.SelfHost.Test;

public class CliTests
{
    [Fact]
    public void Version_PrintsBwshAndStackVersions()
    {
        var output = new StringWriter();
        var configuration = new InvocationConfiguration { Output = output };

        var exitCode = Cli.BuildRootCommand().Parse(["--version"]).Invoke(configuration);

        var informationalVersion = typeof(Cli).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        var lines = output.ToString().Trim().Split(Environment.NewLine);
        Assert.Equal(0, exitCode);
        Assert.Equal($"bwsh {Cli.ShortVersion(informationalVersion)}", lines[0]);
        Assert.Equal(StackVersions.Embedded.ToString(), lines[1]);
    }

    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("0.1.0-alpha.42", "0.1.0-alpha.42")]
    [InlineData("0.1.0-alpha.42+50bbd83b8dd20396e7e0f811370044687d24ab8b", "0.1.0-alpha.42+50bbd83")]
    [InlineData("0.1.0+abc", "0.1.0+abc")]
    public void ShortVersion_TrimsCommitHash(string version, string expected)
    {
        Assert.Equal(expected, Cli.ShortVersion(version));
    }
}

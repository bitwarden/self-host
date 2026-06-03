using Bit.SelfHost.Commands;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Bit.SelfHost.Setup;
using Xunit;

namespace Bit.SelfHost.Test;

public class TopoSortTests
{
    [Fact]
    public void Order_places_dependencies_before_dependents()
    {
        ServiceSpec[] specs =
        [
            new() { Name = "nginx", ContainerName = "n", Image = "i", DependsOn = ["api", "admin"] },
            new() { Name = "admin", ContainerName = "a", Image = "i", DependsOn = ["mssql"] },
            new() { Name = "api", ContainerName = "p", Image = "i" },
            new() { Name = "mssql", ContainerName = "m", Image = "i" },
        ];

        var order = TopoSort.Order(specs).Select(s => s.Name).ToList();

        Assert.True(order.IndexOf("mssql") < order.IndexOf("admin"));
        Assert.True(order.IndexOf("admin") < order.IndexOf("nginx"));
        Assert.True(order.IndexOf("api") < order.IndexOf("nginx"));
    }

    [Fact]
    public void Order_throws_on_dependency_cycle()
    {
        ServiceSpec[] specs =
        [
            new() { Name = "a", ContainerName = "a", Image = "i", DependsOn = ["b"] },
            new() { Name = "b", ContainerName = "b", Image = "i", DependsOn = ["a"] },
        ];

        Assert.Throws<InvalidOperationException>(() => TopoSort.Order(specs));
    }
}

public class EnvFileParserTests
{
    [Fact]
    public void Merge_later_file_wins_skips_comments_tolerates_missing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var a = Path.Combine(dir, "a.env");
        var b = Path.Combine(dir, "b.env");
        File.WriteAllText(a, "# comment\nKEY=one\nB=base\n");
        File.WriteAllText(b, "B=override\nC=three\n");

        var merged = EnvFileParser.Merge([a, b, Path.Combine(dir, "missing.env")]);

        Assert.Contains("KEY=one", merged);
        Assert.Contains("B=override", merged);   // later file wins
        Assert.Contains("C=three", merged);
        Assert.DoesNotContain(merged, l => l.StartsWith("#"));
    }
}

public class DeploymentFactoryTests
{
    [Theory]
    [InlineData("standard", DeploymentKind.Standard)]
    [InlineData("traditional", DeploymentKind.Standard)]
    [InlineData("lite", DeploymentKind.Lite)]
    [InlineData("unified", DeploymentKind.Lite)]
    public void Parse_maps_known_names(string input, DeploymentKind expected)
        => Assert.Equal(expected, DeploymentFactory.Parse(input));

    [Fact]
    public void Parse_throws_on_unknown()
        => Assert.Throws<ArgumentException>(() => DeploymentFactory.Parse("nope"));
}

public class VersionsTests
{
    [Fact]
    public void Embedded_version_json_resolves_real_versions_not_latest()
    {
        Assert.NotEqual("latest", Versions.Core);
        Assert.Matches(@"^\d+\.\d+", Versions.Core);
        Assert.Matches(@"^\d+\.\d+", Versions.Web);
        Assert.Matches(@"^\d+\.\d+", Versions.KeyConnector);
    }
}

public class CliTests
{
    [Fact]
    public void UpsertEnv_adds_then_replaces_in_place()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "x.env");

        Cli.UpsertEnv(path, "A", "1");
        Cli.UpsertEnv(path, "B", "2");
        Cli.UpsertEnv(path, "A", "9"); // replace existing, not append

        var lines = File.ReadAllLines(path);
        Assert.Contains("A=9", lines);
        Assert.Contains("B=2", lines);
        Assert.Single(lines, l => l.StartsWith("A="));
    }
}

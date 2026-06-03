using Bit.SelfHost.Deployments;
using Bit.SelfHost.Setup;
using Xunit;

namespace Bit.SelfHost.Test;

public class StandardConfigTests
{
    [Fact]
    public void Save_then_Load_round_trips_flags()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        new StandardConfig { Url = "https://vault.example.com", EnableKeyConnector = true, EnableScim = true }.Save(root);

        var loaded = StandardConfig.Load(root);

        Assert.Equal("https://vault.example.com", loaded.Url);
        Assert.True(loaded.EnableKeyConnector);
        Assert.True(loaded.EnableScim);
    }
}

public class AnswerFileTests
{
    [Fact]
    public void Load_maps_hyphenated_yaml_keys()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "answers.yaml");
        File.WriteAllText(path,
            "deployment: standard\ncore-version: 2026.3.1\nenable-key-connector: true\nenable-scim: true\n");

        var a = AnswerFile.Load(path);

        Assert.Equal("standard", a.Deployment);
        Assert.Equal("2026.3.1", a.CoreVersion);
        Assert.True(a.EnableKeyConnector);
        Assert.True(a.EnableScim);
    }
}

public class StandardTopologyTests
{
    private static InstallContext CtxWith(bool keyConnector, bool scim)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        new StandardConfig { EnableKeyConnector = keyConnector, EnableScim = scim }.Save(root);
        return new InstallContext { Root = root, Answers = new AnswerFile() };
    }

    [Fact]
    public void Base_topology_is_eleven_services()
        => Assert.Equal(11, new StandardDeployment().BuildTopology(CtxWith(false, false)).Count);

    [Fact]
    public void Key_connector_added_only_when_enabled()
    {
        Assert.DoesNotContain("key-connector",
            new StandardDeployment().BuildTopology(CtxWith(false, false)).Select(s => s.Name));
        Assert.Contains("key-connector",
            new StandardDeployment().BuildTopology(CtxWith(true, false)).Select(s => s.Name));
    }

    [Fact]
    public void Scim_added_only_when_enabled()
        => Assert.Contains("scim", new StandardDeployment().BuildTopology(CtxWith(false, true)).Select(s => s.Name));

    [Fact]
    public void Both_enabled_is_thirteen_services()
        => Assert.Equal(13, new StandardDeployment().BuildTopology(CtxWith(true, true)).Count);
}

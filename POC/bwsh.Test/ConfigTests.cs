using Bit.SelfHost.Commands;
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

public class InstallManifestTests
{
    [Fact]
    public void Load_maps_hyphenated_yaml_keys()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "bitwarden.yaml");
        File.WriteAllText(path,
            "deployment: standard\ncore-version: 2026.3.1\nenable-key-connector: true\nenable-scim: true\n");

        var m = InstallManifest.Load(path);

        Assert.Equal("standard", m.Deployment);
        Assert.Equal("2026.3.1", m.CoreVersion);
        Assert.True(m.EnableKeyConnector);
        Assert.True(m.EnableScim);
    }
}

public class StandardTopologyTests
{
    private static InstallContext CtxWith(bool keyConnector, bool scim)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        new StandardConfig { EnableKeyConnector = keyConnector, EnableScim = scim }.Save(root);
        return new InstallContext { Root = root, Manifest = new InstallManifest() };
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

public class ConfigRedactionTests
{
    [Theory]
    [InlineData("SA_PASSWORD")]
    [InlineData("globalSettings__sqlServer__connectionString")]
    [InlineData("globalSettings__identityServer__certificatePassword")]
    [InlineData("globalSettings__internalIdentityKey")]
    [InlineData("globalSettings__mail__smtp__password")]
    [InlineData("BW_INSTALLATION_KEY")]
    public void Secrets_are_redacted(string key) => Assert.True(ConfigCommand.IsSecret(key));

    [Theory]
    [InlineData("globalSettings__mail__smtp__host")]
    [InlineData("globalSettings__mail__smtp__username")]
    [InlineData("globalSettings__installation__id")]
    [InlineData("BW_DOMAIN")]
    [InlineData("DATABASE")]
    public void Non_secrets_are_shown(string key) => Assert.False(ConfigCommand.IsSecret(key));
}

public class StandardAssetBuilderTests
{
    private static StandardConfig Config() => new() { Url = "http://localhost", GenerateNginxConfig = false };

    private static Dictionary<string, string> ReadEnv(string path)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq > 0) d[line[..eq]] = line[(eq + 1)..];
        }
        return d;
    }

    [Fact]
    public void Generate_twice_preserves_secrets()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var p = new StandardAssetBuilder.InstallParams("id-1", "key-1", "US", "vault");

        StandardAssetBuilder.BuildForInstaller(root, Config(), p);
        var mssql1 = ReadEnv(Path.Combine(root, "env/mssql.override.env"));
        var global1 = ReadEnv(Path.Combine(root, "env/global.override.env"));

        StandardAssetBuilder.BuildForInstaller(root, Config(), p);
        var mssql2 = ReadEnv(Path.Combine(root, "env/mssql.override.env"));
        var global2 = ReadEnv(Path.Combine(root, "env/global.override.env"));

        Assert.Equal(mssql1["SA_PASSWORD"], mssql2["SA_PASSWORD"]);
        Assert.Equal(global1["globalSettings__identityServer__certificatePassword"],
                     global2["globalSettings__identityServer__certificatePassword"]);
        Assert.Equal(global1["globalSettings__internalIdentityKey"], global2["globalSettings__internalIdentityKey"]);
        Assert.Equal(global1["globalSettings__duo__aKey"], global2["globalSettings__duo__aKey"]);
    }

    [Fact]
    public void Config_passthrough_overrides_defaults()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var passthrough = new Dictionary<string, string> { ["globalSettings__mail__smtp__host"] = "smtp.example.com" };
        StandardAssetBuilder.BuildForInstaller(root, Config(),
            new StandardAssetBuilder.InstallParams("id", "key", "US", "vault", passthrough));

        var global = ReadEnv(Path.Combine(root, "env/global.override.env"));
        Assert.Equal("smtp.example.com", global["globalSettings__mail__smtp__host"]);
    }

    [Fact]
    public void Omitted_installation_id_is_preserved_on_reapply()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        StandardAssetBuilder.BuildForInstaller(root, Config(),
            new StandardAssetBuilder.InstallParams("real-id", "real-key", "US", "vault"));
        StandardAssetBuilder.BuildForInstaller(root, Config(),
            new StandardAssetBuilder.InstallParams("", "", "US", "vault")); // manifest omitted id/key

        var global = ReadEnv(Path.Combine(root, "env/global.override.env"));
        Assert.Equal("real-id", global["globalSettings__installation__id"]);
        Assert.Equal("real-key", global["globalSettings__installation__key"]);
    }

    [Fact]
    public async Task ReadManifest_round_trips_standard_config()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var dep = new StandardDeployment();
        var original = new InstallManifest
        {
            Deployment = "standard",
            Domain = "vault.example.com",
            Region = "EU",
            Database = "customdb",
            InstallationId = "11111111-1111-1111-1111-111111111111",
            InstallationKey = "the-key",
            EnableKeyConnector = true,
            EnableScim = true,
            Ssl = new InstallManifest.SslOptions { Enable = true },
            Config = { ["globalSettings__mail__smtp__host"] = "smtp.example.com" },
        };
        await dep.GenerateAssetsAsync(new InstallContext { Root = root, Manifest = original }, default);

        // What `update --rebuild` would feed back into generation: it must reflect the install, not defaults.
        var read = dep.ReadManifest(root);

        Assert.Equal("vault.example.com", read.Domain);
        Assert.Equal("EU", read.Region);
        Assert.Equal("customdb", read.Database);
        Assert.True(read.EnableKeyConnector);
        Assert.True(read.EnableScim);
        Assert.True(read.Ssl.Enable);
        Assert.Equal("11111111-1111-1111-1111-111111111111", read.InstallationId);
        // Passthrough (SMTP etc.) is preserved so a rebuild doesn't reset it.
        Assert.Equal("smtp.example.com", read.Config["globalSettings__mail__smtp__host"]);
    }
}

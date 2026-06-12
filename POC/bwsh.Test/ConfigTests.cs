using Bit.SelfHost.Commands;
using Bit.SelfHost.Deployments;
using Bit.SelfHost.Engine;
using Bit.SelfHost.Setup;
using NSubstitute;
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

public class StandardTlsTests
{
    private static async Task<string> Generate(InstallManifest manifest)
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        await new StandardDeployment().GenerateAssetsAsync(new InstallContext { Root = root, Manifest = manifest }, default);
        return root;
    }

    private static ServiceSpec Nginx(string root) =>
        new StandardDeployment().BuildTopology(new InstallContext { Root = root, Manifest = new InstallManifest() })
            .Single(s => s.Name == "nginx");

    [Fact]
    public async Task Default_manifest_serves_https_with_a_self_signed_cert()
    {
        var root = await Generate(new InstallManifest { Domain = "vault.example.com" });

        var cert = Path.Combine(root, "ssl/self-signed/certificate.crt");
        var key = Path.Combine(root, "ssl/self-signed/private.key");
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", File.ReadAllText(cert));
        Assert.Contains("-----BEGIN PRIVATE KEY-----", File.ReadAllText(key));
        Assert.Contains("/etc/ssl/self-signed/certificate.crt", File.ReadAllText(Path.Combine(root, "nginx/default.conf")));
        Assert.True(StandardConfig.Load(root).Ssl);
    }

    [Fact]
    public async Task Self_signed_cert_is_preserved_on_regenerate()
    {
        var root = await Generate(new InstallManifest { Domain = "vault.example.com" });
        var first = File.ReadAllText(Path.Combine(root, "ssl/self-signed/certificate.crt"));

        await new StandardDeployment().GenerateAssetsAsync(
            new InstallContext { Root = root, Manifest = new InstallManifest { Domain = "vault.example.com" } }, default);

        Assert.Equal(first, File.ReadAllText(Path.Combine(root, "ssl/self-signed/certificate.crt")));
    }

    [Fact]
    public async Task Custom_cert_is_used_when_present_and_no_self_signed_is_generated()
    {
        var root = Directory.CreateTempSubdirectory().FullName;
        var dir = Directory.CreateDirectory(Path.Combine(root, "ssl", "vault.example.com")).FullName;
        File.WriteAllText(Path.Combine(dir, "certificate.crt"), "x");
        File.WriteAllText(Path.Combine(dir, "private.key"), "x");
        File.WriteAllText(Path.Combine(dir, "ca.crt"), "x");

        await new StandardDeployment().GenerateAssetsAsync(
            new InstallContext { Root = root, Manifest = new InstallManifest { Domain = "vault.example.com" } }, default);

        var nginx = File.ReadAllText(Path.Combine(root, "nginx/default.conf"));
        Assert.Contains("/etc/ssl/vault.example.com/certificate.crt", nginx);
        Assert.Contains("/etc/ssl/vault.example.com/ca.crt", nginx);
        Assert.False(File.Exists(Path.Combine(root, "ssl/self-signed/certificate.crt")));
    }

    [Fact]
    public async Task Ssl_disabled_renders_http_only()
    {
        var root = await Generate(new InstallManifest
        {
            Domain = "vault.example.com",
            Ssl = new InstallManifest.SslOptions { Enable = false },
        });

        Assert.DoesNotContain("ssl_certificate ", File.ReadAllText(Path.Combine(root, "nginx/default.conf")));
        Assert.False(StandardConfig.Load(root).Ssl);
        Assert.False(File.Exists(Path.Combine(root, "ssl/self-signed/certificate.crt")));
    }

    [Fact]
    public async Task Nginx_ports_default_to_80_443_and_drop_https_when_off()
    {
        var https = Nginx(await Generate(new InstallManifest { Domain = "v" }));
        Assert.Equal((80, 8080), https.Ports[0]);
        Assert.Equal((443, 8443), https.Ports[1]);

        var http = Nginx(await Generate(new InstallManifest
        { Domain = "v", Ssl = new InstallManifest.SslOptions { Enable = false } }));
        Assert.Single(http.Ports);
        Assert.Equal((80, 8080), http.Ports[0]);
    }

    [Fact]
    public async Task Nginx_ports_honor_custom_config()
    {
        var nginx = Nginx(await Generate(new InstallManifest { Domain = "v", HttpPort = 8080, HttpsPort = 8443 }));
        Assert.Equal((8080, 8080), nginx.Ports[0]);
        Assert.Equal((8443, 8443), nginx.Ports[1]);
    }
}

public class LiteTlsTests
{
    private static async Task<string> Generate(InstallManifest manifest)
        => await LiteTestHelper.GenerateAsync(manifest);

    private static Dictionary<string, string> Settings(string root)
        => StandardAssetBuilder.ReadEnv(Path.Combine(root, "settings.env"));

    [Fact]
    public async Task Lite_defaults_to_https()
    {
        var root = await Generate(new InstallManifest { Deployment = "lite", Domain = "v" });
        Assert.Equal("true", Settings(root)["BW_ENABLE_SSL"]);
    }

    [Fact]
    public async Task Lite_ssl_can_be_disabled()
    {
        var root = await Generate(new InstallManifest
        { Deployment = "lite", Domain = "v", Ssl = new InstallManifest.SslOptions { Enable = false } });
        Assert.Equal("false", Settings(root)["BW_ENABLE_SSL"]);
    }
}

/// <summary>
/// Covers the compose-driven lite path: GenerateAssets must write the downloaded compose + a .env
/// that parameterizes it, the image tag must follow CoreVersion (so `update --core-version` works),
/// and config must round-trip through settings.env.
/// </summary>
public class LiteComposeTests
{
    [Fact]
    public async Task GenerateAssets_writes_only_settings_and_compose_no_scaffolding()
    {
        var root = await LiteTestHelper.GenerateAsync(new InstallManifest { Deployment = "lite", Domain = "v" });

        Assert.True(File.Exists(Path.Combine(root, "settings.env")));      // the config (env_file)
        Assert.True(File.Exists(Path.Combine(root, "docker-compose.yml"))); // downloaded upstream file
        // No generated override or .env — the adaptation is passed to compose as env vars instead.
        Assert.False(File.Exists(Path.Combine(root, "docker-compose.override.yml")));
        Assert.False(File.Exists(Path.Combine(root, ".env")));
    }

    [Fact]
    public async Task Image_tag_follows_core_version_override()
    {
        // `update --core-version dev` sets CoreVersion; the topology image (used by the staleness
        // check) must reflect it, or NeedsUpdate compares the wrong tag. The TAG env var bwsh passes
        // to compose is derived from the same value.
        var image = new LiteDeployment()
            .BuildTopology(new InstallContext { Root = "/r", Manifest = new InstallManifest { CoreVersion = "dev" } })
            .Single().Image;
        Assert.Equal("ghcr.io/bitwarden/lite:dev", image);
    }

    [Fact]
    public async Task ReadManifest_round_trips_lite_config()
    {
        var root = await LiteTestHelper.GenerateAsync(new InstallManifest
        {
            Deployment = "lite",
            Domain = "vault.example.com",
            DbProvider = "postgresql",
            Database = "bw",
            InstallationId = "id-1",
            InstallationKey = "key-1",
            Config = { ["BW_DB_SERVER"] = "pg.example.com" }, // external-db passthrough
        });

        var read = new LiteDeployment().ReadManifest(root);

        Assert.Equal("vault.example.com", read.Domain);
        Assert.Equal("postgresql", read.DbProvider);
        Assert.Equal("bw", read.Database);
        Assert.Equal("id-1", read.InstallationId);
        Assert.Equal("key-1", read.InstallationKey);
        Assert.Equal("pg.example.com", read.Config["BW_DB_SERVER"]);
    }

    [Fact]
    public async Task ResolveUrl_uses_upstream_443_no_suffix()
    {
        var root = await LiteTestHelper.GenerateAsync(new InstallManifest { Deployment = "lite", Domain = "v" });
        Assert.Equal("https://v", new LiteDeployment().ResolveUrl(root));
    }

    [Fact]
    public async Task DownAsync_noops_when_never_installed()
    {
        // No docker-compose.yml in the data dir => nothing was ever brought up; uninstall must not
        // shell out to compose (which would fail/error), it should just return.
        var root = Directory.CreateTempSubdirectory().FullName;
        var engine = Substitute.For<IContainerEngine>();
        var reported = new List<string>();

        await new LiteDeployment().DownAsync(
            new InstallContext { Root = root, Manifest = new InstallManifest() },
            engine, purge: false, reported.Add, default);

        Assert.Empty(reported);
    }
}

/// <summary>Shared setup for lite tests: a local stub compose so GenerateAssets never hits the network.</summary>
internal static class LiteTestHelper
{
    public static async Task<string> GenerateAsync(InstallManifest manifest)
    {
        var stub = Path.Combine(Directory.CreateTempSubdirectory().FullName, "compose.yml");
        File.WriteAllText(stub, "services:\n  bitwarden:\n    image: ${REGISTRY}/lite:${TAG}\n");
        Environment.SetEnvironmentVariable("BWSH_LITE_COMPOSE_URL", stub);

        var root = Directory.CreateTempSubdirectory().FullName;
        await new LiteDeployment().GenerateAssetsAsync(new InstallContext { Root = root, Manifest = manifest }, default);
        return root;
    }
}

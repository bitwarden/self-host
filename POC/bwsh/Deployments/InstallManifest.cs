using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Bit.SelfHost.Deployments;

/// <summary>
/// The unattended-install manifest (`install --manifest bitwarden.yaml`). A small declarative
/// schema covering both deployments; the active deployment reads the fields it cares about.
/// </summary>
public sealed class InstallManifest
{
    public string Deployment { get; set; } = "standard";
    public string Domain { get; set; } = "localhost";
    public string Region { get; set; } = "US";

    public string? InstallationId { get; set; }
    public string? InstallationKey { get; set; }

    public string Database { get; set; } = "vault";       // standard: db name; lite: BW_DB_DATABASE
    public string DbProvider { get; set; } = "sqlserver"; // lite only: sqlite|mysql|postgresql|sqlserver
    public string? DbFile { get; set; }                   // lite sqlite path (default /etc/bitwarden/vault.db)

    public SslOptions Ssl { get; set; } = new();

    public bool EnableKeyConnector { get; set; } // standard: deploy the Key Connector service
    public bool EnableScim { get; set; }         // standard: deploy the SCIM service

    public string CoreVersion { get; set; } = Setup.Versions.Core;
    public string WebVersion { get; set; } = Setup.Versions.Web;
    public string KeyConnectorVersion { get; set; } = Setup.Versions.KeyConnector;

    // Lite-specific:
    public string? Image { get; set; }                    // override full image ref, e.g. to pin a beta tag for a repro

    // 0 = use the deployment default (standard 80/443, lite 8080/8443) — the shared manifest must
    // not force lite's ports onto standard.
    public int HttpPort { get; set; }
    public int HttpsPort { get; set; }

    /// <summary>Extra raw config key/values applied like `config set` after generation.</summary>
    public Dictionary<string, string> Config { get; set; } = new();

    public sealed class SslOptions
    {
        // null = deployment default (standard => HTTPS on, lite => off until lite TLS lands).
        public bool? Enable { get; set; }
        public bool LetsEncrypt { get; set; }
        public string? Email { get; set; }
    }

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static InstallManifest Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Manifest not found: {path}");
        return Yaml.Deserialize<InstallManifest>(File.ReadAllText(path)) ?? new InstallManifest();
    }

    public static string SampleYaml() =>
        """
        # bwsh unattended install manifest
        deployment: standard          # standard | lite
        domain: bitwarden.example.com
        region: US                    # US | EU
        installation-id: 00000000-0000-0000-0000-000000000000
        installation-key: your-key
        database: vault
        ssl:
          enable: true
          lets-encrypt: true
          email: admin@example.com
        config:
          globalSettings__mail__smtp__host: smtp.example.com
          globalSettings__mail__smtp__port: "587"
        """;
}

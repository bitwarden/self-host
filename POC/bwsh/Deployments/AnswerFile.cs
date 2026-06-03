using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Bit.SelfHost.Deployments;

/// <summary>
/// The unattended-install answer file (`install --config answers.yaml`). A small declarative
/// schema covering both deployments; the active deployment reads the fields it cares about.
/// </summary>
public sealed class AnswerFile
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
    public int HttpPort { get; set; } = 8080;
    public int HttpsPort { get; set; } = 8443;

    /// <summary>Extra raw config key/values applied like `config set` after generation.</summary>
    public Dictionary<string, string> Config { get; set; } = new();

    public sealed class SslOptions
    {
        public bool Enable { get; set; }
        public bool LetsEncrypt { get; set; }
        public string? Email { get; set; }
    }

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(HyphenatedNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static AnswerFile Load(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Answer file not found: {path}");
        return Yaml.Deserialize<AnswerFile>(File.ReadAllText(path)) ?? new AnswerFile();
    }

    public static string SampleYaml() =>
        """
        # bwsh unattended install answer file
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

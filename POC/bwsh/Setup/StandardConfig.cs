using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Bit.SelfHost.Setup;

/// <summary>
/// The config.yml model — port of Setup's Configuration (the fields that matter for generation).
/// Serialized with the underscored naming convention to match the on-disk format the existing
/// installer and `rebuild` expect.
/// </summary>
public sealed class StandardConfig
{
    public string Url { get; set; } = "https://localhost";
    public bool GenerateNginxConfig { get; set; } = true;
    public string? HttpPort { get; set; } = "80";
    public string? HttpsPort { get; set; } = "443";
    public bool Ssl { get; set; } = true;
    public string? SslVersions { get; set; }
    public string? SslCiphersuites { get; set; }
    public string? SslCurves { get; set; }
    public bool SslManagedLetsEncrypt { get; set; }
    public string? SslCertificatePath { get; set; }
    public string? SslKeyPath { get; set; }
    public string? SslCaPath { get; set; }
    public string? SslDiffieHellmanPath { get; set; }
    public string? NginxHeaderContentSecurityPolicy { get; set; }
    public bool PushNotifications { get; set; } = true;
    public bool DatabaseDockerVolume { get; set; }
    public List<string>? RealIps { get; set; }
    public bool EnableKeyConnector { get; set; }
    public bool EnableScim { get; set; }
    public bool EnableBuiltInMsSql { get; set; } = true;

    [YamlIgnore]
    public string? Domain =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) ? uri.Host : null;

    private static ISerializer Serializer => new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static IDeserializer Deserializer => new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public void Save(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "config.yml"), Serializer.Serialize(this));
    }

    public static StandardConfig Load(string root)
    {
        var path = Path.Combine(root, "config.yml");
        return File.Exists(path)
            ? Deserializer.Deserialize<StandardConfig>(File.ReadAllText(path)) ?? new StandardConfig()
            : new StandardConfig();
    }
}

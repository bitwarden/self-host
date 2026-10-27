using System.Text.Json;

namespace Bit.SelfHost;

/// <summary>
/// The Bitwarden image versions an install deploys, baked in from the repository's version.json at build time.
/// </summary>
internal sealed record StackVersions(string Core, string Web, string KeyConnector)
{
    internal static StackVersions Embedded { get; } = LoadEmbedded();

    internal static StackVersions Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var versions = document.RootElement.GetProperty("versions");

        return new StackVersions(
            Read(versions, "coreVersion"),
            Read(versions, "webVersion"),
            Read(versions, "keyConnectorVersion"));
    }

    public override string ToString() => $"core {Core} · web {Web} · key-connector {KeyConnector}";

    private static StackVersions LoadEmbedded()
    {
        using var stream = typeof(StackVersions).Assembly.GetManifestResourceStream("version.json")
            ?? throw new InvalidOperationException("The embedded version.json resource is missing.");
        using var reader = new StreamReader(stream);

        return Parse(reader.ReadToEnd());
    }

    private static string Read(JsonElement versions, string name)
    {
        if (!versions.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"version.json has no value for {name}.");
        }

        return value.GetString()!;
    }
}

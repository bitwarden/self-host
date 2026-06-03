using System.Reflection;
using System.Text.Json;

namespace Bit.SelfHost.Setup;

/// <summary>
/// Upstream image versions, baked from the repo's version.json at build time — the single source
/// of truth (the release pipeline keeps version.json current, the same way it updates bitwarden.sh).
/// Installs pin these by default so they're reproducible and `status` shows a real version, not "latest".
/// </summary>
public static class Versions
{
    public static string Core { get; }
    public static string Web { get; }
    public static string KeyConnector { get; }

    static Versions()
    {
        Core = Web = KeyConnector = "latest";
        try
        {
            using var stream = typeof(Versions).GetTypeInfo().Assembly.GetManifestResourceStream("version.json");
            if (stream is null) return;
            using var doc = JsonDocument.Parse(stream);
            var v = doc.RootElement.GetProperty("versions");
            Core = v.GetProperty("coreVersion").GetString() ?? "latest";
            Web = v.GetProperty("webVersion").GetString() ?? "latest";
            KeyConnector = v.GetProperty("keyConnectorVersion").GetString() ?? "latest";
        }
        catch
        {
            // Fall back to "latest" if the manifest is missing or malformed.
        }
    }
}

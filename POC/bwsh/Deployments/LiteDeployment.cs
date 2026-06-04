using System.Runtime.InteropServices;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Deployments;

/// <summary>
/// Bitwarden Lite — single-container deployment (docs: "Bitwarden Lite", formerly Unified).
/// One container runs all .NET services + nginx via supervisord, configured by a flat
/// settings.env of BW_* / globalSettings__* (no Handlebars templating, no Setup builders).
/// DB is either in-container sqlite or an external server the operator manages — so the
/// topology is always exactly ONE container, brought up by the SAME Orchestrator.
/// </summary>
public sealed class LiteDeployment : IDeployment
{
    public const string ContainerName = "bitwarden-lite";
    private const string SettingsFile = "settings.env";

    public DeploymentKind Kind => DeploymentKind.Lite;

    public string InstalledMarker => SettingsFile;

    public IReadOnlyList<string> ConfigFiles { get; } = [SettingsFile];

    public string ResolveUrl(string root)
    {
        var env = new Dictionary<string, string>();
        var path = Path.Combine(root, SettingsFile);
        if (File.Exists(path))
            foreach (var line in File.ReadLines(path))
            {
                var i = line.IndexOf('=');
                if (i > 0) env[line[..i]] = line[(i + 1)..];
            }

        var ssl = env.GetValueOrDefault("BW_ENABLE_SSL", "false") == "true";
        var domain = env.GetValueOrDefault("BW_DOMAIN", "localhost");
        var port = ssl ? env.GetValueOrDefault("BW_PORT_HTTPS", "8443") : env.GetValueOrDefault("BW_PORT_HTTP", "8080");
        var suffix = (ssl && port == "443") || (!ssl && port == "80") ? "" : $":{port}";
        return $"{(ssl ? "https" : "http")}://{domain}{suffix}";
    }

    // Single container on the default bridge — no internal/public split.
    public IReadOnlyList<NetworkSpec> Networks { get; } = [];

    public IReadOnlyList<PromptSpec> InstallPrompts { get; } =
    [
        new("domain", "Enter the domain name (ex. bitwarden.example.com)", "localhost"),
        new("db-provider", "Database provider (sqlite/mysql/postgresql/sqlserver)", "sqlite"),
        new("installation-id", "Enter your installation id — get one at https://bitwarden.com/host",
            Validate: v => Guid.TryParse(v, out var g) && g != Guid.Empty
                ? null : "must be a valid installation id (GUID) from https://bitwarden.com/host"),
        new("installation-key", "Enter your installation key", Secret: true,
            Validate: v => string.IsNullOrWhiteSpace(v)
                ? "installation key is required (from https://bitwarden.com/host)" : null),
    ];

    public Task GenerateAssetsAsync(InstallContext ctx, CancellationToken ct)
    {
        var a = ctx.Manifest;
        Directory.CreateDirectory(ctx.Root);

        var lines = new List<string>();
        void Set(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) lines.Add($"{key}={value}");
        }

        // Domain is intentionally omitted when blank — this is what lets us reproduce the
        // "missing BW_DOMAIN" class of issues (e.g. github #373) rather than always defaulting.
        Set("BW_DOMAIN", a.Domain);

        Set("BW_DB_PROVIDER", a.DbProvider);
        if (a.DbProvider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
            Set("BW_DB_FILE", a.DbFile ?? "/etc/bitwarden/vault.db");
        else
            Set("BW_DB_DATABASE", a.Database); // server/credentials expected via `config:` passthrough

        Set("BW_INSTALLATION_ID", a.InstallationId);
        Set("BW_INSTALLATION_KEY", a.InstallationKey);

        // Service toggles (defaults mirror bitwarden-lite/settings.env).
        Set("BW_ENABLE_ADMIN", "true");
        Set("BW_ENABLE_API", "true");
        Set("BW_ENABLE_IDENTITY", "true");
        Set("BW_ENABLE_ICONS", "true");
        Set("BW_ENABLE_NOTIFICATIONS", "true");
        Set("BW_ENABLE_EVENTS", "false");
        Set("BW_ENABLE_SCIM", "false");
        Set("BW_ENABLE_SSO", "false");

        Set("BW_ENABLE_SSL", a.Ssl.Enable ? "true" : "false");
        Set("BW_PORT_HTTP", a.HttpPort.ToString());
        Set("BW_PORT_HTTPS", a.HttpsPort.ToString());

        // Raw passthrough LAST so it overrides anything above — the escape hatch for crafting
        // an exact environment from a bug report (e.g. a malformed globalSettings__baseServiceUri__vault).
        foreach (var kv in a.Config) lines.Add($"{kv.Key}={kv.Value}");

        var path = Path.Combine(ctx.Root, SettingsFile);
        File.WriteAllLines(path, lines);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600 (holds the installation key)

        if (string.IsNullOrEmpty(a.Domain))
            AnsiConsole.MarkupLine("[grey]note: BW_DOMAIN unset (intentional for repro)[/]");
        return Task.CompletedTask;
    }

    public IReadOnlyList<ServiceSpec> BuildTopology(InstallContext ctx)
    {
        var a = ctx.Manifest;
        var image = string.IsNullOrEmpty(a.Image) ? $"ghcr.io/bitwarden/lite:{Setup.Versions.Core}" : a.Image!;

        (int, int)[] ports = a.Ssl.Enable
            ? [(a.HttpPort, 8080), (a.HttpsPort, 8443)]
            : [(a.HttpPort, 8080)];

        return
        [
            new ServiceSpec
            {
                Name = "bitwarden",
                ContainerName = ContainerName,
                Image = image,
                EnvFiles = [Path.Combine(ctx.Root, SettingsFile)], // merged into the container Env
                Binds = [(ctx.Root, "/etc/bitwarden")],
                Ports = ports,
                Networks = [],
            }
        ];
    }

    // Lite: every key is a settings.env var; applying requires a container restart.
    public bool TryResolveConfigKey(string key, out ConfigBinding binding)
    {
        binding = new ConfigBinding(SettingsFile, ConfigApplyAction.Restart);
        return true;
    }
}

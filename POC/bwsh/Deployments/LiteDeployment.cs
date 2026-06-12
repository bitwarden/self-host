using System.Globalization;
using System.Runtime.InteropServices;
using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Deployments;

/// <summary>
/// Bitwarden Lite — single-container deployment (docs: "Bitwarden Lite", formerly Unified).
/// One container runs all .NET services + nginx via supervisord, configured by a flat
/// settings.env of BW_* / globalSettings__* (no Handlebars templating, no Setup builders).
/// DB is either in-container sqlite or an external server the operator manages.
///
/// Unlike standard, lite is driven by `docker compose` rather than the Engine API: bwsh
/// downloads the repo's maintained bitwarden-lite/docker-compose.yml and runs it directly.
/// The only file bwsh generates is settings.env (the compose's env_file) — image tag and the
/// data-dir bind are passed to compose as environment variables (REGISTRY/TAG/BW_VOLUME), so
/// there's no override or .env to maintain. Read-only operations (status/logs/versions/backup)
/// still use the Engine API by container name, which the compose pins to "bitwarden-lite".
/// </summary>
public sealed class LiteDeployment : IDeployment
{
    public const string ContainerName = "bitwarden-lite";
    private const string SettingsFile = "settings.env";
    private const string ComposeFile = "docker-compose.yml"; // downloaded upstream file, run as-is

    // The maintained lite compose bwsh fetches. Override with BWSH_LITE_COMPOSE_URL (a URL or a
    // local file path) to test a branch/PR build — e.g. the rootless work in self-host#358.
    private const string DefaultComposeUrl =
        "https://raw.githubusercontent.com/bitwarden/self-host/bwsh-poc/bitwarden-lite/docker-compose.yml";

    public DeploymentKind Kind => DeploymentKind.Lite;

    public string InstalledMarker => SettingsFile;

    public IReadOnlyList<string> ConfigFiles { get; } = [SettingsFile];

    public string ResolveUrl(string root)
    {
        var env = Setup.StandardAssetBuilder.ReadEnv(Path.Combine(root, SettingsFile));
        var ssl = env.GetValueOrDefault("BW_ENABLE_SSL", "false") == "true";
        var domain = env.GetValueOrDefault("BW_DOMAIN", "localhost");
        // The upstream lite compose publishes on host 80/443, so there's no port suffix.
        return $"{(ssl ? "https" : "http")}://{domain}";
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

    public InstallManifest ReadManifest(string root)
    {
        var env = Setup.StandardAssetBuilder.ReadEnv(Path.Combine(root, SettingsFile));
        var manifest = new InstallManifest
        {
            Deployment = "lite",
            Domain = env.GetValueOrDefault("BW_DOMAIN", ""),
            DbProvider = env.GetValueOrDefault("BW_DB_PROVIDER", "sqlite"),
            DbFile = env.GetValueOrDefault("BW_DB_FILE"),
            Database = env.GetValueOrDefault("BW_DB_DATABASE", "vault"),
            InstallationId = env.GetValueOrDefault("BW_INSTALLATION_ID"),
            InstallationKey = env.GetValueOrDefault("BW_INSTALLATION_KEY"),
            // Host ports are fixed by the upstream compose (80/443), not configurable here.
            Ssl = new InstallManifest.SslOptions { Enable = env.GetValueOrDefault("BW_ENABLE_SSL") == "true" },
        };

        // Preserve everything GenerateAssetsAsync doesn't map to a field (BW_ENABLE_* toggles,
        // globalSettings__* passthrough) so a rebuild re-emits them.
        string[] managed =
        [
            "BW_DOMAIN", "BW_DB_PROVIDER", "BW_DB_FILE", "BW_DB_DATABASE", "BW_INSTALLATION_ID",
            "BW_INSTALLATION_KEY", "BW_PORT_HTTP", "BW_PORT_HTTPS", "BW_ENABLE_SSL",
        ];
        foreach (var (key, value) in env)
            if (!managed.Contains(key))
                manifest.Config[key] = value;

        return manifest;
    }

    public async Task GenerateAssetsAsync(InstallContext ctx, CancellationToken ct)
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

        // HTTPS by default; the lite container self-signs a cert when none is provided.
        Set("BW_ENABLE_SSL", (a.Ssl.Enable ?? true) ? "true" : "false");
        Set("BW_PORT_HTTP", (a.HttpPort != 0 ? a.HttpPort : 8080).ToString(CultureInfo.InvariantCulture));
        Set("BW_PORT_HTTPS", (a.HttpsPort != 0 ? a.HttpsPort : 8443).ToString(CultureInfo.InvariantCulture));

        // Raw passthrough LAST so it overrides anything above — the escape hatch for crafting
        // an exact environment from a bug report (e.g. a malformed globalSettings__baseServiceUri__vault).
        foreach (var kv in a.Config) lines.Add($"{kv.Key}={kv.Value}");

        var path = Path.Combine(ctx.Root, SettingsFile);
        File.WriteAllLines(path, lines);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600 (holds the installation key)

        if (string.IsNullOrEmpty(a.Domain))
            AnsiConsole.MarkupLine("[grey]note: BW_DOMAIN unset (intentional for repro)[/]");

        await EnsureComposeFileAsync(ctx, ct);
    }

    // Lite runs through compose, so the "topology" is a single descriptor used only for status/inspect
    // (container name) and update's image-tag staleness check — not for Engine-API creation. The
    // runtime config (volumes, ports, hardening) lives in the downloaded compose + generated .env.
    public IReadOnlyList<ServiceSpec> BuildTopology(InstallContext ctx) =>
    [
        new ServiceSpec
        {
            Name = "bitwarden",
            ContainerName = ContainerName,
            // Must match the image compose actually runs (REGISTRY/TAG below), so NeedsUpdateAsync
            // compares like-for-like. Tag follows the manifest's CoreVersion (`update --core-version`).
            Image = $"{Registry(ctx.Manifest)}/lite:{Tag(ctx.Manifest)}",
            Networks = [],
        }
    ];

    // ---- compose assets -------------------------------------------------------------------------

    private static string Registry(InstallManifest a) => "ghcr.io/bitwarden";

    // CoreVersion drives the tag (pinned default, or `dev`/a beta via `update --core-version`).
    private static string Tag(InstallManifest a) => a.CoreVersion;

    // Passed to the compose process so it interpolates ${...} without a generated .env file:
    //   REGISTRY/TAG  -> the image (TAG follows CoreVersion, set by `update --core-version`)
    //   BW_VOLUME     -> a host path => compose bind-mounts the data dir at /etc/bitwarden, so the
    //                    sqlite db + certs live where bwsh backs up (vs the upstream named volume)
    private static Dictionary<string, string> ComposeEnv(InstallContext ctx) => new()
    {
        ["REGISTRY"] = Registry(ctx.Manifest),
        ["TAG"] = Tag(ctx.Manifest),
        ["BW_VOLUME"] = Path.GetFullPath(ctx.Root),
    };

    /// <summary>Ensure the upstream compose file is present in the data dir (downloaded once).</summary>
    private static async Task EnsureComposeFileAsync(InstallContext ctx, CancellationToken ct)
    {
        Directory.CreateDirectory(ctx.Root);
        var composePath = Path.Combine(ctx.Root, ComposeFile);
        if (!File.Exists(composePath))
        {
            var url = Environment.GetEnvironmentVariable("BWSH_LITE_COMPOSE_URL") ?? DefaultComposeUrl;
            await File.WriteAllTextAsync(composePath, await FetchAsync(url, ct), ct);
        }
    }

    // A URL is fetched over HTTP; an existing local path is read directly (BWSH_LITE_COMPOSE_URL can
    // point at a working copy for local testing before the file is published).
    private static async Task<string> FetchAsync(string urlOrPath, CancellationToken ct)
    {
        if (File.Exists(urlOrPath)) return await File.ReadAllTextAsync(urlOrPath, ct);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            return await http.GetStringAsync(urlOrPath, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"Could not download the lite compose file from {urlOrPath}: {ex.Message}", ex);
        }
    }

    public async Task UpAsync(InstallContext ctx, IContainerEngine engine, string title, bool forcePull, CancellationToken ct)
    {
        await PreflightAsync(ctx, ct);
        await EnsureComposeFileAsync(ctx, ct);
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(title)} — docker compose up[/]");
        // --no-deps + an explicit service: the upstream file ships an example `db` service that
        // `bitwarden` depends_on. bwsh's DB is sqlite or external (via settings.env), so bring up
        // only the app and skip the bundled SQL Server.
        string[] args = forcePull
            ? ["up", "-d", "--no-deps", "--pull", "always", "bitwarden"]
            : ["up", "-d", "--no-deps", "bitwarden"];
        await ComposeCli.RunAsync(ctx.Root, Path.Combine(ctx.Root, ComposeFile), ComposeEnv(ctx), args, ct);
    }

    /// <summary>Fail fast before composing: the compose CLI must be present.</summary>
    private static async Task PreflightAsync(InstallContext ctx, CancellationToken ct)
    {
        if (!await ComposeCli.IsAvailableAsync(ct))
            throw new InvalidOperationException(
                "Docker Compose is required for a lite deployment but was not found. " +
                "Install the Docker Compose plugin (`docker compose`) and retry.");
    }

    public async Task DownAsync(InstallContext ctx, IContainerEngine engine, bool purge, Action<string> report, CancellationToken ct)
    {
        var composePath = Path.Combine(ctx.Root, ComposeFile);
        if (!File.Exists(composePath)) return; // never brought up, or already removed

        report("docker compose down");
        // BW_VOLUME bind-mounts the data dir, so -v only clears the upstream named volumes (logs, the
        // example db's); the uninstall command deletes the data dir itself on purge.
        string[] args = purge ? ["down", "-v"] : ["down"];
        await ComposeCli.RunAsync(ctx.Root, composePath, ComposeEnv(ctx), args, ct);
    }

    // Lite: every key is a settings.env var; applying requires a container restart.
    public bool TryResolveConfigKey(string key, out ConfigBinding binding)
    {
        binding = new ConfigBinding(SettingsFile, ConfigApplyAction.Restart);
        return true;
    }

    public bool SupportsCertRenewal => false; // lite manages TLS in-container

    public bool SupportsAggregateLogs => true; // bare `logs` returns the whole supervisord stream

    public Task PreUpAsync(InstallContext ctx, IContainerEngine engine, CancellationToken ct) => Task.CompletedTask;

    private static readonly HashSet<string> KnownStates =
        ["RUNNING", "STARTING", "STOPPED", "STOPPING", "BACKOFF", "EXITED", "FATAL", "UNKNOWN"];

    public async Task<IReadOnlyList<ProcessStatus>> InspectProcessesAsync(IContainerEngine engine, CancellationToken ct)
    {
        // The real per-service state lives under supervisord, not in the single container's state.
        if (!(await engine.InspectAsync(ContainerName, ct)).Running) return [];

        string output;
        try
        {
            output = await engine.ExecAsync(ContainerName, ["supervisorctl", "status"], ct);
        }
        catch
        {
            return [];
        }

        var rows = new List<ProcessStatus>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !KnownStates.Contains(parts[1])) continue; // skip connection/error lines
            rows.Add(new ProcessStatus(parts[0], parts[1]));
        }
        return rows;
    }

    public async Task<IReadOnlyList<VersionInfo>> GatherVersionsAsync(IContainerEngine engine, CancellationToken ct) =>
        await engine.ImageTagAsync(ContainerName, ct) is { } v ? [new("Version", v)] : [];

    public async Task<IReadOnlyList<string>> ListLogServicesAsync(string root, IContainerEngine engine, CancellationToken ct)
    {
        var ls = await engine.ExecAsync(ContainerName,
            ["sh", "-c", "ls /var/log/bitwarden/*.log 2>/dev/null || true"], ct);
        return ls.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFileNameWithoutExtension)
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .Distinct()
            .ToList();
    }

    public Task<string> FetchLogAsync(string? service, int tail, IContainerEngine engine, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(service))
            return engine.ContainerLogsAsync(ContainerName, tail, ct); // aggregate supervisord output

        var path = $"/var/log/bitwarden/{service}.log";
        string[] cmd = tail <= 0 ? ["cat", path] : ["tail", "-n", tail.ToString(CultureInfo.InvariantCulture), path];
        return engine.ExecAsync(ContainerName, cmd, ct);
    }

    // sqlite lives in {root} and travels in the archive directly, so there's nothing to dump or restore.
    public Task PreBackupAsync(string root, IContainerEngine engine, CancellationToken ct) => Task.CompletedTask;

    public Task PostUnpackAsync(
        string root, Orchestrator orch, IReadOnlyList<ServiceSpec> topology, IContainerEngine engine, CancellationToken ct) =>
        Task.CompletedTask;

    public Task RenewCertAsync(string root, bool force, IContainerEngine engine, CancellationToken ct) =>
        throw new NotSupportedException("lite manages TLS in-container");
}

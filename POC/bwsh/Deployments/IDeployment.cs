using Bit.SelfHost.Engine;

namespace Bit.SelfHost.Deployments;

/// <summary>
/// Docs terminology: a "deployment". Standard = multi-container; Lite = single container.
/// https://bitwarden.com/help/self-host-bitwarden/
/// </summary>
public enum DeploymentKind { Standard, Lite }

/// <summary>
/// The modularity seam. Every command (install/config/update/uninstall) is written ONCE
/// against this interface + the shared Orchestrator. "Dropping in Lite" = implementing this
/// interface and registering it in DeploymentFactory — no new orchestration code, because
/// BuildTopology() feeds the same engine whether it returns 11 specs or 1.
/// </summary>
public interface IDeployment
{
    DeploymentKind Kind { get; }

    IReadOnlyList<NetworkSpec> Networks { get; }

    /// <summary>Relative path (under the data dir) whose presence means the deployment is installed.</summary>
    string InstalledMarker { get; }

    /// <summary>The URL the vault is served at, read from the generated config under <paramref name="root"/>.</summary>
    string ResolveUrl(string root);

    /// <summary>Interactive prompts when `install` is run without --config.</summary>
    IReadOnlyList<PromptSpec> InstallPrompts { get; }

    /// <summary>
    /// Generate on-disk config assets. THIS is what replaces the Setup container — Standard
    /// renders config.yml + env files + certs + nginx; Lite writes settings.env.
    /// </summary>
    Task GenerateAssetsAsync(InstallContext ctx, CancellationToken ct);

    /// <summary>The container graph, for status/inspect and (standard) orchestration.</summary>
    IReadOnlyList<ServiceSpec> BuildTopology(InstallContext ctx);

    /// <summary>
    /// Bring the deployment up. The execution model is the deployment's own: standard drives the
    /// Engine API via the Orchestrator; lite drives `docker compose`. <paramref name="engine"/> is
    /// provided for the Engine-API path and ignored by compose-driven deployments.
    /// </summary>
    Task UpAsync(InstallContext ctx, IContainerEngine engine, string title, bool forcePull, CancellationToken ct);

    /// <summary>Stop and remove the deployment's containers (and, on purge, its volumes).</summary>
    Task DownAsync(InstallContext ctx, IContainerEngine engine, bool purge, Action<string> report, CancellationToken ct);

    /// <summary>
    /// Reconstruct the manifest from on-disk config so re-rendering (`update --rebuild`) preserves
    /// the deployment's actual config and topology instead of resetting to defaults.
    /// </summary>
    InstallManifest ReadManifest(string root);

    /// <summary>Relative paths of the on-disk config files, for `config` to print (secrets redacted).</summary>
    IReadOnlyList<string> ConfigFiles { get; }

    /// <summary>Resolve a `config key=value` key to the file it lives in + the action to apply it.</summary>
    bool TryResolveConfigKey(string key, out ConfigBinding binding);

    /// <summary>True when renewcert applies. Standard with Let's Encrypt supports it; lite does not.</summary>
    bool SupportsCertRenewal { get; }

    /// <summary>True when bare logs returns aggregate output. False means a service name is required.</summary>
    bool SupportsAggregateLogs { get; }

    /// <summary>Runs after assets are generated and before the stack comes up. Standard provisions Let's Encrypt.</summary>
    Task PreUpAsync(InstallContext ctx, IContainerEngine engine, CancellationToken ct);

    /// <summary>In-container process states for lite supervisord. Empty when not applicable.</summary>
    Task<IReadOnlyList<ProcessStatus>> InspectProcessesAsync(IContainerEngine engine, CancellationToken ct);

    /// <summary>Deployed image versions to display and record.</summary>
    Task<IReadOnlyList<VersionInfo>> GatherVersionsAsync(IContainerEngine engine, CancellationToken ct);

    /// <summary>Service names whose logs are available for this deployment.</summary>
    Task<IReadOnlyList<string>> ListLogServicesAsync(string root, IContainerEngine engine, CancellationToken ct);

    /// <summary>One service's log. A null service means aggregate output. tail of 0 or less means the full log.</summary>
    Task<string> FetchLogAsync(string? service, int tail, IContainerEngine engine, CancellationToken ct);

    /// <summary>Runs before the data dir is archived. Standard dumps the database to a .BAK.</summary>
    Task PreBackupAsync(string root, IContainerEngine engine, CancellationToken ct);

    /// <summary>Runs after unpack and before the full stack comes up. Standard brings up mssql and restores the .BAK.</summary>
    Task PostUnpackAsync(string root, Orchestrator orch, IReadOnlyList<ServiceSpec> topology, IContainerEngine engine, CancellationToken ct);

    /// <summary>Renew the TLS certificate. Only called when SupportsCertRenewal is true.</summary>
    Task RenewCertAsync(string root, bool force, IContainerEngine engine, CancellationToken ct);
}

/// <summary>A deployed component's version for status and backup display.</summary>
public sealed record VersionInfo(string Label, string Version);

/// <summary>An in-container process state row from lite supervisord.</summary>
public sealed record ProcessStatus(string Name, string State);

public sealed record InstallContext
{
    public required string Root { get; init; }      // the bwdata directory
    public required InstallManifest Manifest { get; init; }
}

/// <summary>
/// One interactive install prompt. <paramref name="Validate"/> returns an error message for an
/// invalid value, or null when valid (kept Spectre-free so deployments don't depend on the UI).
/// </summary>
public sealed record PromptSpec(
    string Key,
    string Question,
    string? Default = null,
    bool Secret = false,
    Func<string, string?>? Validate = null);

public sealed record ConfigBinding(string File, ConfigApplyAction Action);

/// <summary>
/// Docs: config.yml changes need `rebuild`; env changes need `restart`; some settings
/// (e.g. SMTP in the running app) can hot-reload.
/// </summary>
public enum ConfigApplyAction { Restart, Rebuild, HotReload }

/// <summary>The "drop in" registry. Add a kind + a class; nothing else changes.</summary>
public static class DeploymentFactory
{
    public static IDeployment Create(DeploymentKind kind) => kind switch
    {
        DeploymentKind.Standard => new StandardDeployment(),
        DeploymentKind.Lite => new LiteDeployment(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static DeploymentKind Parse(string value) => value.ToLowerInvariant() switch
    {
        "standard" or "traditional" => DeploymentKind.Standard,
        "lite" or "unified" => DeploymentKind.Lite,
        _ => throw new ArgumentException($"Unknown deployment '{value}'. Use 'standard' or 'lite'."),
    };
}

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

    /// <summary>The container graph the Orchestrator brings up.</summary>
    IReadOnlyList<ServiceSpec> BuildTopology(InstallContext ctx);

    /// <summary>Resolve a `config set key=value` key to the file it lives in + the action to apply it.</summary>
    bool TryResolveConfigKey(string key, out ConfigBinding binding);
}

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

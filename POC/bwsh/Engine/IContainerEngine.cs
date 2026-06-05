namespace Bit.SelfHost.Engine;

/// <summary>
/// The test seam. Orchestration depends on this, not on Docker.DotNet directly, so unit
/// tests substitute a fake and assert sequencing with no Docker daemon — the property the
/// bash installer cannot give us.
/// </summary>
public interface IContainerEngine
{
    Task<bool> ImageExistsAsync(string image, CancellationToken ct);
    Task PullAsync(string image, IProgress<string> log, CancellationToken ct);
    Task EnsureNetworkAsync(NetworkSpec net, CancellationToken ct);
    Task<string> CreateAsync(ServiceSpec spec, IList<string> env, CancellationToken ct);
    Task ConnectNetworkAsync(string containerId, string network, string alias, CancellationToken ct);
    Task StartAsync(string containerId, CancellationToken ct);

    /// <summary>Block until the container exits and return its exit code (one-off runs like certbot).</summary>
    Task<long> WaitAsync(string containerName, CancellationToken ct);

    Task<ContainerState> InspectAsync(string containerName, CancellationToken ct);
    Task RemoveAsync(string containerName, bool force, CancellationToken ct);
    Task RemoveNetworkAsync(string name, CancellationToken ct);
    Task RemoveVolumeAsync(string name, CancellationToken ct);
    Task<string> ImageOfAsync(string containerName, CancellationToken ct);
    Task<string> ExecAsync(string containerName, string[] cmd, CancellationToken ct);
    Task<string> ContainerLogsAsync(string containerName, int tail, CancellationToken ct);
}

public sealed record ContainerState(bool Exists, bool Running, string? Health, string Status);

public static class ContainerEngineExtensions
{
    /// <summary>The tag of a running container's image, or null when the container is absent.</summary>
    public static async Task<string?> ImageTagAsync(this IContainerEngine engine, string container, CancellationToken ct)
    {
        try
        {
            var image = await engine.ImageOfAsync(container, ct);
            var slash = image.LastIndexOf('/');
            var colon = image.LastIndexOf(':');
            return colon > slash ? image[(colon + 1)..] : null;
        }
        catch
        {
            return null; // container not present
        }
    }
}

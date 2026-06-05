using System.Diagnostics;
using System.Globalization;
using System.Text;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace Bit.SelfHost.Engine;

/// <summary>Docker.DotNet implementation talking to the local Engine API over the default socket/pipe.</summary>
public sealed class DockerDotNetEngine : IContainerEngine, IDisposable
{
    private readonly DockerClient _client;
    private const string ProductLabel = "com.bitwarden.product";

    public DockerDotNetEngine()
    {
        // Auto-detects unix:///var/run/docker.sock (Linux/macOS) or the Windows named pipe.
        _client = new DockerClientConfiguration().CreateClient();
    }

    public async Task<bool> ImageExistsAsync(string image, CancellationToken ct)
    {
        try
        {
            await _client.Images.InspectImageAsync(image, ct);
            return true;
        }
        catch (DockerImageNotFoundException)
        {
            return false;
        }
    }

    public async Task PullAsync(string image, IProgress<string> log, CancellationToken ct)
    {
        // Shell out to `docker pull` rather than Docker.DotNet's CreateImageAsync — the latter stalls
        // on genuinely-missing images. Docker is already a hard dependency, and the CLI is the reliable
        // path for the pull itself (the rest of orchestration stays on the Engine API).
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("pull");
        psi.ArgumentList.Add(image);

        using var process = new Process { StartInfo = psi };
        var errors = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is { } line) log.Report(line); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) errors.AppendLine(line); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw;
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"`docker pull {image}` failed: {errors.ToString().Trim()}");
    }

    public async Task EnsureNetworkAsync(NetworkSpec net, CancellationToken ct)
    {
        var existing = await _client.Networks.ListNetworksAsync(
            new NetworksListParameters
            {
                Filters = new Dictionary<string, IDictionary<string, bool>>
                { ["name"] = new Dictionary<string, bool> { [net.Name] = true } }
            }, ct);
        if (existing.Any(n => n.Name == net.Name)) return;

        await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
        {
            Name = net.Name,
            Internal = net.Internal,
            Driver = "bridge",
            Labels = new Dictionary<string, string> { [ProductLabel] = "bitwarden" },
        }, ct);
    }

    public async Task<string> CreateAsync(ServiceSpec spec, IList<string> env, CancellationToken ct)
    {
        // A bind host with no path separator is a named (managed) volume, not a host path —
        // pass it through; otherwise resolve to an absolute host path.
        var binds = spec.Binds.Select(b =>
        {
            var isNamedVolume = !b.Host.Contains('/') && !b.Host.Contains('\\');
            var host = isNamedVolume ? b.Host : Path.GetFullPath(b.Host);
            return $"{host}:{b.Container}";
        }).ToList();

        var portBindings = new Dictionary<string, IList<PortBinding>>();
        var exposed = new Dictionary<string, EmptyStruct>();
        foreach (var (host, container) in spec.Ports)
        {
            portBindings[$"{container}/tcp"] = [new PortBinding { HostPort = host.ToString(CultureInfo.InvariantCulture) }];
            exposed[$"{container}/tcp"] = default;
        }

        var primaryNet = spec.Networks.FirstOrDefault();

        var resp = await _client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Name = spec.ContainerName,
            Image = spec.Image,
            Cmd = spec.Command.Length > 0 ? spec.Command : null,
            Env = env,
            ExposedPorts = exposed.Count > 0 ? exposed : null,
            Labels = new Dictionary<string, string>
            {
                [ProductLabel] = "bitwarden",
                // Keep `docker compose ls/ps` and `docker ps` filters recognizing the stack
                // even though we orchestrate via the raw API.
                ["com.docker.compose.project"] = "bitwarden",
                ["com.docker.compose.service"] = spec.Name,
            },
            HostConfig = new HostConfig
            {
                Binds = binds.Count > 0 ? binds : null,
                PortBindings = portBindings.Count > 0 ? portBindings : null,
                RestartPolicy = new RestartPolicy
                {
                    Name = spec.RestartAlways ? RestartPolicyKind.Always : RestartPolicyKind.No
                },
                NetworkMode = primaryNet,
            },
            NetworkingConfig = primaryNet is null ? null : new NetworkingConfig
            {
                // Alias = the logical service name (e.g. "mssql", "api") so the generated config's
                // inter-service hostnames resolve — compose gives these for free; the API doesn't.
                EndpointsConfig = new Dictionary<string, EndpointSettings>
                {
                    [primaryNet] = new() { Aliases = [spec.Name] }
                }
            },
            StopTimeout = spec.StopTimeoutSeconds is { } secs ? TimeSpan.FromSeconds(secs) : null,
        }, ct);

        return resp.ID;
    }

    public Task ConnectNetworkAsync(string containerId, string network, string alias, CancellationToken ct) =>
        _client.Networks.ConnectNetworkAsync(network,
            new NetworkConnectParameters
            {
                Container = containerId,
                EndpointConfig = new EndpointSettings { Aliases = [alias] }
            }, ct);

    public Task StartAsync(string containerId, CancellationToken ct) =>
        _client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), ct);

    public async Task<long> WaitAsync(string containerName, CancellationToken ct) =>
        (await _client.Containers.WaitContainerAsync(containerName, ct)).StatusCode;

    public async Task<ContainerState> InspectAsync(string containerName, CancellationToken ct)
    {
        try
        {
            var r = await _client.Containers.InspectContainerAsync(containerName, ct);
            return new ContainerState(true, r.State.Running, r.State.Health?.Status, r.State.Status);
        }
        catch (DockerContainerNotFoundException)
        {
            return new ContainerState(false, false, null, "not found");
        }
    }

    public async Task RemoveAsync(string containerName, bool force, CancellationToken ct)
    {
        try
        {
            await _client.Containers.RemoveContainerAsync(containerName,
                new ContainerRemoveParameters { Force = force }, ct);
        }
        catch (DockerContainerNotFoundException) { /* idempotent down */ }
    }

    public async Task RemoveNetworkAsync(string name, CancellationToken ct)
    {
        try { await _client.Networks.DeleteNetworkAsync(name, ct); }
        catch (DockerApiException) { /* not found / not created — idempotent */ }
    }

    public async Task RemoveVolumeAsync(string name, CancellationToken ct)
    {
        try { await _client.Volumes.RemoveAsync(name, force: true, ct); }
        catch (DockerApiException) { /* not found — idempotent */ }
    }

    public async Task<string> ImageOfAsync(string containerName, CancellationToken ct)
    {
        var r = await _client.Containers.InspectContainerAsync(containerName, ct);
        return r.Config.Image;
    }

    public async Task<string> ExecAsync(string containerName, string[] cmd, CancellationToken ct)
    {
        var exec = await _client.Exec.ExecCreateContainerAsync(containerName,
            new ContainerExecCreateParameters { Cmd = cmd, AttachStdout = true, AttachStderr = true }, ct);
        using var stream = await _client.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, ct);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(ct);
        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    public async Task<string> ContainerLogsAsync(string containerName, int tail, CancellationToken ct)
    {
        using var stream = await _client.Containers.GetContainerLogsAsync(containerName, tty: false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Tail = tail <= 0 ? "all" : tail.ToString(CultureInfo.InvariantCulture) }, ct);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(ct);
        return string.Concat(stdout, stderr);
    }

    public void Dispose() => _client.Dispose();
}

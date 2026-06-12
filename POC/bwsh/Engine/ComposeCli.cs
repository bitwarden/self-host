using System.Diagnostics;

namespace Bit.SelfHost.Engine;

/// <summary>
/// Thin wrapper over the host's Docker Compose CLI. Lite drives `docker compose` rather than the
/// Engine API directly so it can ride the maintained, hardened compose file (user/read_only/tmpfs
/// and service wiring) instead of re-deriving it through Docker.DotNet. Standard still uses the
/// Engine API via <see cref="Orchestrator"/>; this is only on the lite path.
/// </summary>
public static class ComposeCli
{
    // `docker compose` (v2 plugin) is preferred; fall back to the standalone `docker-compose` (v1),
    // mirroring run.sh's detection so existing hosts keep working.
    private static (string File, string[] Lead)? _resolved;

    public static async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        try { return (await ResolveAsync(ct)) is not null; }
        catch { return false; }
    }

    private static async Task<(string File, string[] Lead)?> ResolveAsync(CancellationToken ct)
    {
        if (_resolved is not null) return _resolved;
        if (await ProbeAsync("docker", ["compose", "version"], ct))
            return _resolved = ("docker", ["compose"]);
        if (await ProbeAsync("docker-compose", ["version"], ct))
            return _resolved = ("docker-compose", []);
        return null;
    }

    private static async Task<bool> ProbeAsync(string file, string[] args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p is null) return false;
            await p.WaitForExitAsync(ct);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Run a compose subcommand for the project rooted at <paramref name="projectDir"/>, streaming
    /// output to the console. <paramref name="env"/> values are set on the child process so compose
    /// interpolates them (REGISTRY/TAG/BW_VOLUME) without a generated .env file. Throws when compose
    /// is missing or the command exits non-zero.
    /// </summary>
    public static async Task RunAsync(
        string projectDir, string composeFile, IReadOnlyDictionary<string, string> env, string[] args, CancellationToken ct)
    {
        var resolved = await ResolveAsync(ct)
            ?? throw new InvalidOperationException(
                "Docker Compose not found. Install the Docker Compose plugin (`docker compose`) to manage a lite deployment.");

        var psi = new ProcessStartInfo(resolved.File) { UseShellExecute = false };
        foreach (var lead in resolved.Lead) psi.ArgumentList.Add(lead);
        psi.ArgumentList.Add("--project-name");
        psi.ArgumentList.Add("bitwarden");
        psi.ArgumentList.Add("--project-directory");
        psi.ArgumentList.Add(projectDir);
        psi.ArgumentList.Add("--file");
        psi.ArgumentList.Add(composeFile);
        foreach (var a in args) psi.ArgumentList.Add(a);

        foreach (var (key, value) in env) psi.Environment[key] = value;

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {resolved.File}.");
        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"`docker compose {string.Join(' ', args)}` exited with code {p.ExitCode}.");
    }
}

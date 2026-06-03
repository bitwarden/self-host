namespace Bit.SelfHost.Engine;

/// <summary>
/// One container in a deployment's graph. Several fields exist only because the Docker
/// Engine API has no equivalent of the compose concept the installer relies on today:
///   EnvFiles  -> compose `env_file` (API takes a flat Env list; we merge ourselves)
///   DependsOn -> compose `depends_on` (API has no ordering; we topo-sort)
///   Networks  -> compose multi-network (API create attaches one; rest via ConnectNetwork)
///   Binds     -> compose relative volumes (API needs absolute host paths or named volumes)
/// A deployment produces these; the Orchestrator consumes them. The orchestration code is
/// identical whether a deployment returns 11 specs (standard) or 1 (lite).
/// </summary>
public sealed record ServiceSpec
{
    public required string Name { get; init; }
    public required string ContainerName { get; init; }
    public required string Image { get; init; }
    public string[] EnvFiles { get; init; } = [];
    public (string Host, string Container)[] Binds { get; init; } = [];
    public string[] Networks { get; init; } = [];
    public (int Host, int Container)[] Ports { get; init; } = [];
    public string[] DependsOn { get; init; } = [];
    public bool RestartAlways { get; init; } = true;
    public int? StopTimeoutSeconds { get; init; }
}

public sealed record NetworkSpec(string Name, bool Internal);

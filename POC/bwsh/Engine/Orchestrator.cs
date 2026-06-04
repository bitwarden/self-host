using System.Collections.Concurrent;
using Spectre.Console;

namespace Bit.SelfHost.Engine;

/// <summary>
/// The orchestration loop that replaces run.sh's `docker compose up`. Pure logic over
/// IContainerEngine, shared by every deployment. Sequence: ensure networks -> per service
/// in dependency order: pull -> create -> connect extra networks -> start -> wait healthy.
/// </summary>
public sealed class Orchestrator(IContainerEngine engine, IReadOnlyList<NetworkSpec> networks)
{
    private const int MaxConcurrentPulls = 4;

    public async Task UpAsync(IReadOnlyList<ServiceSpec> services, CancellationToken ct, string title = "Bitwarden")
    {
        // One live region for the whole bring-up: a single table where every service walks through
        // pull -> create -> start -> health in place. Workers write the concurrent `status` map; the
        // single render loop owns the table, so nothing mutates the table off-thread.
        var status = new ConcurrentDictionary<string, (string Text, string Color)>();
        foreach (var s in services) status[s.Name] = ("pending", "grey");
        var phase = "preparing";
        var settledAll = false;

        var table = new Table().Border(TableBorder.None).AddColumns("Service", "Status");
        var panel = new Panel(table).Expand().RoundedBorder().BorderColor(Color.Grey);

        var frames = Spinner.Known.Dots.Frames;
        void Render(string header, string spinner)
        {
            panel.Header = new PanelHeader($" {header} ");
            table.Rows.Clear();
            foreach (var s in services)
            {
                var (text, color) = status[s.Name];
                var lead = color == "yellow" ? $"{spinner} " : "  "; // spin only the in-progress rows
                table.AddRow(Markup.Escape(s.Name), $"[{color}]{lead}{text}[/]");
            }
        }

        // Condense a `docker pull` line (non-TTY: "<layer>: <status>") to a short status for the table.
        static string? PullSummary(string line)
        {
            line = line.Trim();
            if (line.Length == 0) return null;
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            var text = colon is > 0 and < 16 ? line[(colon + 2)..] : line;
            return text.Length > 40 ? text[..40] : text;
        }

        async Task RunAsync()
        {
            // Pull (parallel, capped; skip images already local).
            phase = "pulling";
            foreach (var s in services) status[s.Name] = ("pulling", "yellow");
            using var gate = new SemaphoreSlim(MaxConcurrentPulls);
            await Task.WhenAll(services.GroupBy(s => s.Image).Select(async group =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    if (!await engine.ImageExistsAsync(group.Key, ct))
                    {
                        using var pullCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        pullCts.CancelAfter(TimeSpan.FromMinutes(15));
                        var progress = new Progress<string>(line =>
                        {
                            if (PullSummary(line) is { } summary)
                                foreach (var svc in group) status[svc.Name] = ($"pulling: {summary}", "yellow");
                        });
                        await engine.PullAsync(group.Key, progress, pullCts.Token);
                    }
                    foreach (var s in group) status[s.Name] = ("image ready", "grey");
                }
                finally { gate.Release(); }
            }));

            foreach (var net in networks) await engine.EnsureNetworkAsync(net, ct);

            // Create ALL (so every DNS alias exists before any start), then start ALL.
            phase = "creating";
            await Task.WhenAll(services.Select(async spec =>
            {
                status[spec.Name] = ("creating", "yellow");
                await engine.RemoveAsync(spec.ContainerName, force: true, ct); // idempotent recreate
                var env = EnvFileParser.Merge(spec.EnvFiles);
                var id = await engine.CreateAsync(spec, env, ct);
                foreach (var extra in spec.Networks.Skip(1))
                    await engine.ConnectNetworkAsync(id, extra, spec.Name, ct);
                status[spec.Name] = ("created", "grey");
            }));

            phase = "starting";
            await Task.WhenAll(services.Select(async spec =>
            {
                status[spec.Name] = ("starting", "yellow");
                await engine.StartAsync(spec.ContainerName, ct);
            }));

            // Poll health in place until everything settles (or times out).
            phase = "checking health";
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
            while (true)
            {
                settledAll = true;
                foreach (var s in services)
                {
                    var (text, color, settled) = Classify(await engine.InspectAsync(s.ContainerName, ct));
                    status[s.Name] = (text, color);
                    if (!settled) settledAll = false;
                }
                if (settledAll || DateTime.UtcNow >= deadline) break;
                await Task.Delay(750, ct);
            }
        }

        await AnsiConsole.Live(panel).StartAsync(async ctx =>
        {
            var work = RunAsync();
            var tick = 0;
            while (!work.IsCompleted)
            {
                Render($"[yellow]{Markup.Escape(title)}[/] [grey]— {phase}[/]", frames[tick++ % frames.Count]);
                ctx.Refresh();
                await Task.Delay(200, ct);
            }
            await work; // surface any worker exception, after the loop exits cleanly
            var unhealthy = services.Count(s => status[s.Name].Color == "red");
            var summary = !settledAll ? "[yellow]still starting[/]"
                : unhealthy == 0 ? "[green]ready[/]"
                : $"[red]{unhealthy} unhealthy — run `logs <service>`[/]";
            Render($"[bold]{Markup.Escape(title)}[/] — {summary}", " ");
            ctx.Refresh();
        });
    }

    private static (string Text, string Color, bool Settled) Classify(ContainerState s) => s switch
    {
        { Exists: false } => ("not created", "grey", true),
        { Health: "healthy" } => ("healthy", "green", true),
        { Health: "unhealthy" } => ("unhealthy", "red", true),
        { Health: null, Running: true } => ("running", "green", true),
        { Running: false, Status: "exited" or "dead" } => (s.Status, "red", true), // crashed on boot
        _ => ("starting", "yellow", false),
    };

    public async Task DownAsync(IReadOnlyList<ServiceSpec> services, bool purge, Action<string> report, CancellationToken ct)
    {
        foreach (var spec in TopoSort.Order(services).Reverse())
        {
            report($"removing {spec.ContainerName}");
            await engine.RemoveAsync(spec.ContainerName, force: true, ct);
        }

        // Removing containers leaves volumes and networks behind — purge must clear them explicitly
        // so a repro can be torn down to nothing.
        if (!purge) return;

        var volumes = services
            .SelectMany(s => s.Binds)
            .Select(b => b.Host)
            .Where(h => !h.Contains('/') && !h.Contains('\\')) // bare name => named volume, not host path
            .Distinct();
        foreach (var vol in volumes)
        {
            report($"removing volume {vol}");
            await engine.RemoveVolumeAsync(vol, ct);
        }

        foreach (var net in networks)
        {
            report($"removing network {net.Name}");
            await engine.RemoveNetworkAsync(net.Name, ct);
        }
    }

    /// <summary>run.sh `updatebw` equivalent: compare running image tag against the target.</summary>
    public async Task<bool> NeedsUpdateAsync(ServiceSpec spec, CancellationToken ct)
    {
        var state = await engine.InspectAsync(spec.ContainerName, ct);
        if (!state.Exists) return true;
        var current = await engine.ImageOfAsync(spec.ContainerName, ct);
        return !string.Equals(current, spec.Image, StringComparison.Ordinal);
    }

}

/// <summary>Kahn topological sort over DependsOn. Replaces compose's implicit ordering.</summary>
public static class TopoSort
{
    public static IReadOnlyList<ServiceSpec> Order(IReadOnlyList<ServiceSpec> services)
    {
        var byName = services.ToDictionary(s => s.Name);
        var emitted = new HashSet<string>();
        var ready = new List<string>(services.Where(s => s.DependsOn.All(d => !byName.ContainsKey(d)))
                                             .Select(s => s.Name));
        var ordered = new List<ServiceSpec>();

        while (ready.Count > 0)
        {
            var name = ready[0];
            ready.RemoveAt(0);
            if (!emitted.Add(name)) continue;
            ordered.Add(byName[name]);

            foreach (var s in services)
            {
                if (emitted.Contains(s.Name) || ready.Contains(s.Name)) continue;
                if (s.DependsOn.Where(byName.ContainsKey).All(emitted.Contains)) ready.Add(s.Name);
            }
        }

        if (ordered.Count != services.Count)
            throw new InvalidOperationException("Dependency cycle detected in service graph.");
        return ordered;
    }
}

/// <summary>Parses and merges compose-style env_file lists (later files win).</summary>
public static class EnvFileParser
{
    public static IList<string> Merge(IEnumerable<string> paths)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                merged[line[..eq]] = line[(eq + 1)..];
            }
        }
        return merged.Select(kv => $"{kv.Key}={kv.Value}").ToList();
    }
}

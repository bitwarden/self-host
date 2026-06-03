using Bit.SelfHost.Engine;
using NSubstitute;
using Spectre.Console;
using Xunit;

namespace Bit.SelfHost.Test;

public class OrchestratorTests
{
    public OrchestratorTests()
    {
        // Suppress Spectre's live output during tests (Orchestrator renders a Live panel).
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null) });
    }

    private static IContainerEngine HealthyEngine()
    {
        var engine = Substitute.For<IContainerEngine>();
        engine.ImageExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true); // skip pulls
        engine.CreateAsync(Arg.Any<ServiceSpec>(), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns("cid-0123456789ab");
        engine.InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ContainerState(true, true, "healthy", "running")); // settles immediately
        return engine;
    }

    [Fact]
    public async Task UpAsync_ensures_networks_and_creates_and_starts_every_service()
    {
        var engine = HealthyEngine();
        var orch = new Orchestrator(engine, [new NetworkSpec("net", false)]);
        ServiceSpec[] services =
        [
            new() { Name = "mssql", ContainerName = "bw-mssql", Image = "mssql:1", Networks = ["net"] },
            new() { Name = "api", ContainerName = "bw-api", Image = "api:1", Networks = ["net"] },
        ];

        await orch.UpAsync(services, CancellationToken.None, "Test");

        await engine.Received(1).EnsureNetworkAsync(Arg.Is<NetworkSpec>(n => n.Name == "net"), Arg.Any<CancellationToken>());
        await engine.Received(1).CreateAsync(Arg.Is<ServiceSpec>(s => s.Name == "mssql"), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
        await engine.Received(1).CreateAsync(Arg.Is<ServiceSpec>(s => s.Name == "api"), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
        await engine.Received(1).StartAsync("bw-mssql", Arg.Any<CancellationToken>());
        await engine.Received(1).StartAsync("bw-api", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpAsync_skips_pull_when_image_present()
    {
        var engine = HealthyEngine(); // ImageExists => true
        var orch = new Orchestrator(engine, []);

        await orch.UpAsync([new() { Name = "a", ContainerName = "bw-a", Image = "a:1" }], CancellationToken.None, "T");

        await engine.DidNotReceive().PullAsync(Arg.Any<string>(), Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpAsync_pulls_when_image_absent()
    {
        var engine = HealthyEngine();
        engine.ImageExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        var orch = new Orchestrator(engine, []);

        await orch.UpAsync([new() { Name = "a", ContainerName = "bw-a", Image = "a:1" }], CancellationToken.None, "T");

        await engine.Received(1).PullAsync("a:1", Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NeedsUpdate_true_when_running_image_differs_from_target()
    {
        var engine = Substitute.For<IContainerEngine>();
        engine.InspectAsync("bw-a", Arg.Any<CancellationToken>()).Returns(new ContainerState(true, true, "healthy", "running"));
        engine.ImageOfAsync("bw-a", Arg.Any<CancellationToken>()).Returns("a:OLD");
        var orch = new Orchestrator(engine, []);

        Assert.True(await orch.NeedsUpdateAsync(new() { Name = "a", ContainerName = "bw-a", Image = "a:NEW" }, CancellationToken.None));
        Assert.False(await orch.NeedsUpdateAsync(new() { Name = "a", ContainerName = "bw-a", Image = "a:OLD" }, CancellationToken.None));
    }
}

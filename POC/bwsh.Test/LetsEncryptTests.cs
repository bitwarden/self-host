using Bit.SelfHost.Engine;
using Bit.SelfHost.Setup;
using NSubstitute;
using Spectre.Console;
using Xunit;

namespace Bit.SelfHost.Test;

public class LetsEncryptTests
{
    public LetsEncryptTests() =>
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null) });

    private static IContainerEngine Engine(long exit = 0)
    {
        var e = Substitute.For<IContainerEngine>();
        e.ImageExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        e.CreateAsync(Arg.Any<ServiceSpec>(), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>()).Returns("cid");
        e.WaitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(exit);
        e.ContainerLogsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns("certbot output");
        return e;
    }

    private static StandardConfig LeConfig() => new() { Url = "https://vault.example.com", SslManagedLetsEncrypt = true };

    [Fact]
    public async Task Provision_runs_certbot_certonly_and_frees_nginx()
    {
        var e = Engine();
        var root = Directory.CreateTempSubdirectory().FullName;

        await LetsEncrypt.ProvisionIfNeeded(e, root, LeConfig(), "admin@example.com", default);

        await e.Received().RemoveAsync("bitwarden-nginx", true, Arg.Any<CancellationToken>());
        await e.Received().CreateAsync(Arg.Is<ServiceSpec>(s =>
            s.Image == "certbot/certbot"
            && s.Command[0] == "certonly"
            && s.Command.Contains("vault.example.com")
            && s.Command.Contains("--email")
            && !s.RestartAlways), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
        await e.Received().WaitAsync("bitwarden-certbot", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provision_without_email_registers_unsafely()
    {
        var e = Engine();
        await LetsEncrypt.ProvisionIfNeeded(e, Directory.CreateTempSubdirectory().FullName, LeConfig(), null, default);
        await e.Received().CreateAsync(Arg.Is<ServiceSpec>(s =>
            s.Command.Contains("--register-unsafely-without-email")), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provision_skips_when_not_lets_encrypt()
    {
        var e = Engine();
        await LetsEncrypt.ProvisionIfNeeded(e, Directory.CreateTempSubdirectory().FullName,
            new StandardConfig { Url = "https://v", SslManagedLetsEncrypt = false }, null, default);
        await e.DidNotReceive().CreateAsync(Arg.Any<ServiceSpec>(), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provision_skips_when_cert_already_present()
    {
        var e = Engine();
        var root = Directory.CreateTempSubdirectory().FullName;
        var live = Directory.CreateDirectory(Path.Combine(root, "letsencrypt", "live", "vault.example.com")).FullName;
        File.WriteAllText(Path.Combine(live, "fullchain.pem"), "x");

        await LetsEncrypt.ProvisionIfNeeded(e, root, LeConfig(), null, default);

        await e.DidNotReceive().CreateAsync(Arg.Any<ServiceSpec>(), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Provision_throws_with_logs_on_failure()
    {
        var e = Engine(exit: 1);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LetsEncrypt.ProvisionIfNeeded(e, Directory.CreateTempSubdirectory().FullName, LeConfig(), null, default));
        Assert.Contains("certbot output", ex.Message);
    }

    [Fact]
    public async Task Renew_force_passes_force_renew()
    {
        var e = Engine();
        await LetsEncrypt.RenewAsync(e, Directory.CreateTempSubdirectory().FullName, force: true, default);
        await e.Received().CreateAsync(Arg.Is<ServiceSpec>(s =>
            s.Command[0] == "renew" && s.Command.Contains("--force-renew")), Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
    }
}

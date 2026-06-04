using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Setup;

/// <summary>
/// Let's Encrypt provisioning and renewal via a one-off certbot container (standard deployments).
/// certbot runs in --standalone mode, so nginx is dropped first to free the challenge ports.
/// </summary>
public static class LetsEncrypt
{
    private const string Image = "certbot/certbot";
    private const string Container = "bitwarden-certbot";
    private const string Nginx = "bitwarden-nginx";

    /// <summary>Obtain a cert when the install is Let's Encrypt-managed and one isn't present yet.</summary>
    public static async Task ProvisionIfNeeded(IContainerEngine engine, string root, StandardConfig config,
        string? email, CancellationToken ct)
    {
        if (!config.SslManagedLetsEncrypt) return;
        var domain = config.Domain ?? "localhost";
        if (File.Exists(Path.Combine(root, "letsencrypt", "live", domain, "fullchain.pem"))) return;

        AnsiConsole.MarkupLine($"Obtaining a Let's Encrypt certificate for [green]{Markup.Escape(domain)}[/]…");
        await RunAsync(engine, root, CertonlyArgs(domain, email), needHttps: false, ct);
    }

    /// <summary>Renew the existing Let's Encrypt cert.</summary>
    public static Task RenewAsync(IContainerEngine engine, string root, bool force, CancellationToken ct) =>
        RunAsync(engine, root, RenewArgs(force), needHttps: true, ct);

    private static string[] CertonlyArgs(string domain, string? email)
    {
        string[] account = string.IsNullOrWhiteSpace(email)
            ? ["--register-unsafely-without-email"]
            : ["--email", email];
        return
        [
            "certonly", "--standalone", "--non-interactive", "--agree-tos", "--preferred-challenges", "http",
            "-d", domain, "--keep-until-expiring", "--logs-dir", "/etc/letsencrypt/logs",
            .. account,
        ];
    }

    private static string[] RenewArgs(bool force) =>
        force
            ? ["renew", "--force-renew", "--logs-dir", "/etc/letsencrypt/logs"]
            : ["renew", "--logs-dir", "/etc/letsencrypt/logs"];

    private static async Task RunAsync(IContainerEngine engine, string root, string[] args, bool needHttps, CancellationToken ct)
    {
        await engine.RemoveAsync(Nginx, force: true, ct);     // free 80 (and 443 for renew)
        await engine.RemoveAsync(Container, force: true, ct); // clear any stale certbot

        if (!await engine.ImageExistsAsync(Image, ct))
            await engine.PullAsync(Image, new Progress<string>(_ => { }), ct);

        var spec = new ServiceSpec
        {
            Name = "certbot",
            ContainerName = Container,
            Image = Image,
            Command = args,
            Ports = needHttps ? [(80, 80), (443, 443)] : [(80, 80)],
            Binds = [($"{root}/letsencrypt", "/etc/letsencrypt")],
            RestartAlways = false,
            Networks = [],
        };

        var id = await engine.CreateAsync(spec, [], ct);
        await engine.StartAsync(id, ct);
        var code = await engine.WaitAsync(Container, ct);
        var logs = await engine.ContainerLogsAsync(Container, 50, ct);
        await engine.RemoveAsync(Container, force: true, ct);

        if (code != 0)
            throw new InvalidOperationException($"certbot exited with code {code}:\n{logs.Trim()}");
    }
}

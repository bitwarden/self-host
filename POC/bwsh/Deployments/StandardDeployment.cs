using Bit.SelfHost.Engine;
using Spectre.Console;

namespace Bit.SelfHost.Deployments;

/// <summary>
/// Standard (multi-container) deployment — the bitwarden.sh / run.sh / Setup-container path.
/// Topology transcribed from server/util/Setup/Templates/DockerCompose.hbs (built-in MSSQL).
/// </summary>
public sealed class StandardDeployment : IDeployment
{
    public DeploymentKind Kind => DeploymentKind.Standard;

    public string InstalledMarker => "config.yml";

    public IReadOnlyList<string> ConfigFiles { get; } =
    [
        "config.yml",
        "env/global.override.env",
        "env/mssql.override.env",
        "env/key-connector.override.env",
    ];

    public string ResolveUrl(string root) => Setup.StandardConfig.Load(root).Url;

    public IReadOnlyList<NetworkSpec> Networks { get; } =
    [
        new("bitwarden-default", Internal: true),
        new("bitwarden-public", Internal: false),
    ];

    public IReadOnlyList<PromptSpec> InstallPrompts { get; } =
    [
        new("domain", "Enter the domain name for your Bitwarden instance (ex. bitwarden.example.com)", "localhost"),
        new("region", "Enter your region (US/EU)", "US"),
        new("installation-id", "Enter your installation id — get one at https://bitwarden.com/host",
            Validate: v => Guid.TryParse(v, out var g) && g != Guid.Empty
                ? null : "must be a valid installation id (GUID) from https://bitwarden.com/host"),
        new("installation-key", "Enter your installation key", Secret: true,
            Validate: v => string.IsNullOrWhiteSpace(v)
                ? "installation key is required (from https://bitwarden.com/host)" : null),
        new("database", "Enter the database name for your Bitwarden instance", "vault"),
    ];

    public InstallManifest ReadManifest(string root)
    {
        var config = Setup.StandardConfig.Load(root);
        var global = Setup.StandardAssetBuilder.ReadEnv(Path.Combine(root, "env/global.override.env"));
        var mssql = Setup.StandardAssetBuilder.ReadEnv(Path.Combine(root, "env/mssql.override.env"));

        var manifest = new InstallManifest
        {
            Deployment = "standard",
            Domain = config.Domain ?? "localhost",
            Region = global.GetValueOrDefault("globalSettings__baseServiceUri__cloudRegion", "US"),
            Database = mssql.GetValueOrDefault("DATABASE", "vault"),
            InstallationId = global.GetValueOrDefault("globalSettings__installation__id"),
            InstallationKey = global.GetValueOrDefault("globalSettings__installation__key"),
            EnableKeyConnector = config.EnableKeyConnector,
            EnableScim = config.EnableScim,
            Ssl = new InstallManifest.SslOptions { Enable = config.Ssl, LetsEncrypt = config.SslManagedLetsEncrypt },
            HttpPort = int.TryParse(config.HttpPort, out var http) ? http : 80,
            HttpsPort = int.TryParse(config.HttpsPort, out var https) ? https : 443,
        };

        // Preserve user-set values (SMTP, admins, HIBP, yubico, …) as passthrough so a rebuild
        // re-emits them. Skip the keys generation derives from fields above or restores as secrets.
        string[] derived =
        [
            "globalSettings__baseServiceUri__vault", "globalSettings__baseServiceUri__cloudRegion",
            "globalSettings__sqlServer__connectionString", "globalSettings__identityServer__certificatePassword",
            "globalSettings__internalIdentityKey", "globalSettings__oidcIdentityClientKey", "globalSettings__duo__aKey",
            "globalSettings__installation__id", "globalSettings__installation__key",
            "globalSettings__mail__replyToEmail", "globalSettings__pushRelayBaseUri",
        ];
        foreach (var (key, value) in global)
            if (!derived.Contains(key))
                manifest.Config[key] = value;

        return manifest;
    }

    public async Task GenerateAssetsAsync(InstallContext ctx, CancellationToken ct)
    {
        // (1) Directory scaffolding — replaces run.sh `dockerComposeVolumes`.
        foreach (var dir in new[]
        {
            "core", "core/attachments", "ca-certificates", "identity", "nginx", "ssl", "web", "letsencrypt",
            "logs/admin", "logs/api", "logs/events", "logs/icons", "logs/identity", "logs/mssql",
            "logs/nginx", "logs/notifications", "logs/sso", "mssql/backups", "docker", "env",
        })
        {
            Directory.CreateDirectory(Path.Combine(ctx.Root, dir));
        }

        // (2) Render config.yml + env files + identity cert + nginx + app-id — the Setup replacement.
        var a = ctx.Manifest;
        var ssl = a.Ssl.Enable ?? true; // HTTPS by default for standard
        var domain = string.IsNullOrEmpty(a.Domain) ? "localhost" : a.Domain;

        var config = Setup.StandardConfig.Load(ctx.Root);
        config.Url = $"http{(ssl ? "s" : "")}://{domain}";
        config.Ssl = ssl;
        config.SslManagedLetsEncrypt = a.Ssl.LetsEncrypt;
        config.HttpPort = (a.HttpPort != 0 ? a.HttpPort : 80).ToString();
        config.HttpsPort = (a.HttpsPort != 0 ? a.HttpsPort : 443).ToString();
        config.EnableKeyConnector = a.EnableKeyConnector;
        config.EnableScim = a.EnableScim;

        // Resolve the web TLS cert (HTTPS is on by default). Off => clear paths so only http renders.
        if (ssl)
            ResolveWebCert(ctx.Root, domain, a.Ssl.LetsEncrypt, config);
        else
            config.SslCertificatePath = config.SslKeyPath = config.SslCaPath = config.SslDiffieHellmanPath = null;

        if (a.EnableKeyConnector)
            Directory.CreateDirectory(Path.Combine(ctx.Root, "logs/key-connector")); // bwkc.pfx dir is made by the cert writer
        if (a.EnableScim)
            Directory.CreateDirectory(Path.Combine(ctx.Root, "logs/scim"));

        Setup.StandardAssetBuilder.BuildForInstaller(ctx.Root, config,
            new Setup.StandardAssetBuilder.InstallParams(
                InstallationId: a.InstallationId ?? string.Empty,
                InstallationKey: a.InstallationKey ?? string.Empty,
                Region: a.Region,
                Database: a.Database,
                Config: a.Config));

        await Task.CompletedTask;
    }

    /// <summary>
    /// Chooses the web TLS cert: Let's Encrypt paths, a custom cert in bwdata/ssl/&lt;domain&gt;/, or a
    /// generated self-signed cert (the default). Sets the container cert paths the nginx template renders.
    /// </summary>
    private static void ResolveWebCert(string root, string domain, bool letsEncrypt, Setup.StandardConfig config)
    {
        config.SslCaPath = null;
        config.SslDiffieHellmanPath = null;

        if (letsEncrypt)
        {
            config.SslCertificatePath = $"/etc/letsencrypt/live/{domain}/fullchain.pem";
            config.SslKeyPath = $"/etc/letsencrypt/live/{domain}/privkey.pem";
            AnsiConsole.MarkupLine("[yellow]note: Let's Encrypt provisioning isn't implemented yet; "
                + "supply certs under bwdata/letsencrypt or use a custom cert.[/]");
            return;
        }

        var customDir = Path.Combine(root, "ssl", domain);
        if (File.Exists(Path.Combine(customDir, "certificate.crt")))
        {
            config.SslCertificatePath = $"/etc/ssl/{domain}/certificate.crt";
            config.SslKeyPath = $"/etc/ssl/{domain}/private.key";
            if (File.Exists(Path.Combine(customDir, "ca.crt"))) config.SslCaPath = $"/etc/ssl/{domain}/ca.crt";
            if (File.Exists(Path.Combine(customDir, "dhparam.pem"))) config.SslDiffieHellmanPath = $"/etc/ssl/{domain}/dhparam.pem";
            return;
        }

        // Self-signed fallback: generate once, then preserve across re-renders (apply/rebuild).
        var certPath = Path.Combine(root, "ssl", "self-signed", "certificate.crt");
        var keyPath = Path.Combine(root, "ssl", "self-signed", "private.key");
        if (!File.Exists(certPath) || !File.Exists(keyPath))
            Setup.WebCert.WriteSelfSigned(certPath, keyPath, domain);
        config.SslCertificatePath = "/etc/ssl/self-signed/certificate.crt";
        config.SslKeyPath = "/etc/ssl/self-signed/private.key";
    }

    public IReadOnlyList<ServiceSpec> BuildTopology(InstallContext ctx)
    {
        var root = ctx.Root;
        var core = ctx.Manifest.CoreVersion;
        var web = ctx.Manifest.WebVersion;

        // Loaded up front: drives both the nginx host ports and the optional-service gating below.
        var config = Setup.StandardConfig.Load(root);
        var httpHost = int.TryParse(config.HttpPort, out var hp) ? hp : 80;
        var httpsHost = int.TryParse(config.HttpsPort, out var sp) ? sp : 443;
        (int Host, int Container)[] nginxPorts = config.Ssl
            ? [(httpHost, 8080), (httpsHost, 8443)]
            : [(httpHost, 8080)];
        string[] appEnv = [$"{root}/docker/global.env", $"{root}/env/uid.env", $"{root}/env/global.override.env"];
        const string D = "bitwarden-default";
        const string P = "bitwarden-public";

        (string, string) core_ = ($"{root}/core", "/etc/bitwarden/core");
        (string, string) ca = ($"{root}/ca-certificates", "/etc/bitwarden/ca-certificates");
        (string, string) id = ($"{root}/identity", "/etc/bitwarden/identity");
        (string, string) Logs(string s) => ($"{root}/logs/{s}", "/etc/bitwarden/logs");

        var services = new List<ServiceSpec>
        {
            new() { Name = "mssql", ContainerName = "bitwarden-mssql", Image = $"ghcr.io/bitwarden/mssql:{core}",
                    EnvFiles = [$"{root}/docker/mssql.env", $"{root}/env/uid.env", $"{root}/env/mssql.override.env"],
                    Networks = [D], StopTimeoutSeconds = 60,
                    Binds = [("bitwarden-mssql-data", "/var/opt/mssql/data"), Logs("mssql"),
                             ($"{root}/mssql/backups", "/etc/bitwarden/mssql/backups")] },
            new() { Name = "web", ContainerName = "bitwarden-web", Image = $"ghcr.io/bitwarden/web:{web}",
                    EnvFiles = [$"{root}/docker/global.env", $"{root}/env/uid.env"], Networks = [D],
                    Binds = [($"{root}/web", "/etc/bitwarden/web")] },
            new() { Name = "attachments", ContainerName = "bitwarden-attachments", Image = $"ghcr.io/bitwarden/attachments:{core}",
                    EnvFiles = [$"{root}/docker/global.env", $"{root}/env/uid.env"], Networks = [D],
                    Binds = [($"{root}/core/attachments", "/etc/bitwarden/core/attachments")] },
            new() { Name = "api", ContainerName = "bitwarden-api", Image = $"ghcr.io/bitwarden/api:{core}",
                    EnvFiles = appEnv, Networks = [D, P], Binds = [core_, ca, Logs("api")] },
            new() { Name = "identity", ContainerName = "bitwarden-identity", Image = $"ghcr.io/bitwarden/identity:{core}",
                    EnvFiles = appEnv, Networks = [D, P], Binds = [id, core_, ca, Logs("identity")] },
            new() { Name = "sso", ContainerName = "bitwarden-sso", Image = $"ghcr.io/bitwarden/sso:{core}",
                    EnvFiles = appEnv, Networks = [D, P], Binds = [id, core_, ca, Logs("sso")] },
            new() { Name = "admin", ContainerName = "bitwarden-admin", Image = $"ghcr.io/bitwarden/admin:{core}",
                    EnvFiles = appEnv, Networks = [D, P], DependsOn = ["mssql"], Binds = [core_, ca, Logs("admin")] },
            new() { Name = "icons", ContainerName = "bitwarden-icons", Image = $"ghcr.io/bitwarden/icons:{core}",
                    EnvFiles = [$"{root}/docker/global.env", $"{root}/env/uid.env"], Networks = [D, P],
                    Binds = [ca, Logs("icons")] },
            new() { Name = "notifications", ContainerName = "bitwarden-notifications", Image = $"ghcr.io/bitwarden/notifications:{core}",
                    EnvFiles = appEnv, Networks = [D, P], Binds = [ca, Logs("notifications")] },
            new() { Name = "events", ContainerName = "bitwarden-events", Image = $"ghcr.io/bitwarden/events:{core}",
                    EnvFiles = appEnv, Networks = [D, P], Binds = [ca, Logs("events")] },
            new() { Name = "nginx", ContainerName = "bitwarden-nginx", Image = $"ghcr.io/bitwarden/nginx:{core}",
                    EnvFiles = [$"{root}/env/uid.env"], Networks = [D, P],
                    Ports = nginxPorts, DependsOn = ["web", "admin", "api", "identity"],
                    Binds = [($"{root}/nginx", "/etc/bitwarden/nginx"), ($"{root}/letsencrypt", "/etc/letsencrypt"),
                             ($"{root}/ssl", "/etc/ssl"), ($"{root}/logs/nginx", "/var/log/nginx")] },
        };

        // Optional services, gated by the persisted config.yml (so status/update/uninstall see them
        // too — not just install).
        if (config.EnableKeyConnector)
        {
            services.Add(new ServiceSpec
            {
                Name = "key-connector",
                ContainerName = "bitwarden-key-connector",
                Image = $"ghcr.io/bitwarden/key-connector:{ctx.Manifest.KeyConnectorVersion}",
                EnvFiles = [$"{root}/env/uid.env", $"{root}/env/key-connector.override.env"],
                Networks = [D, P],
                Binds = [($"{root}/key-connector", "/etc/bitwarden/key-connector"), ca, Logs("key-connector")],
            });
        }
        if (config.EnableScim)
        {
            services.Add(new ServiceSpec
            {
                Name = "scim",
                ContainerName = "bitwarden-scim",
                Image = $"ghcr.io/bitwarden/scim:{core}",
                EnvFiles = appEnv,
                Networks = [D, P],
                Binds = [core_, ca, Logs("scim")],
            });
        }

        return services;
    }

    public bool TryResolveConfigKey(string key, out ConfigBinding binding)
    {
        // Docs: SMTP/admin settings live in env/global.override.env and need `restart`;
        // structural settings live in config.yml and need `rebuild`.
        if (key.StartsWith("globalSettings__") || key.StartsWith("adminSettings__"))
        {
            binding = new ConfigBinding("env/global.override.env", ConfigApplyAction.Restart);
            return true;
        }

        string[] configYmlKeys =
            ["url", "http_port", "https_port", "ssl", "ssl_managed_lets_encrypt",
             "enable_key_connector", "enable_scim", "enable_built_in_ms_sql"];
        if (configYmlKeys.Contains(key))
        {
            binding = new ConfigBinding("config.yml", ConfigApplyAction.Rebuild);
            return true;
        }

        binding = default!;
        return false;
    }
}

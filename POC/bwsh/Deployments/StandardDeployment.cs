using Bit.SelfHost.Engine;

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
        var ssl = a.Ssl.Enable;
        var domain = string.IsNullOrEmpty(a.Domain) ? "localhost" : a.Domain;

        var config = Setup.StandardConfig.Load(ctx.Root);
        config.Url = $"http{(ssl ? "s" : "")}://{domain}";
        config.Ssl = ssl;
        config.SslManagedLetsEncrypt = a.Ssl.LetsEncrypt;
        config.HttpPort = a.HttpPort.ToString();
        config.HttpsPort = a.HttpsPort.ToString();
        config.EnableKeyConnector = a.EnableKeyConnector;
        config.EnableScim = a.EnableScim;

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

    public IReadOnlyList<ServiceSpec> BuildTopology(InstallContext ctx)
    {
        var root = ctx.Root;
        var core = ctx.Manifest.CoreVersion;
        var web = ctx.Manifest.WebVersion;
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
                    Ports = [(80, 8080), (443, 8443)], DependsOn = ["web", "admin", "api", "identity"],
                    Binds = [($"{root}/nginx", "/etc/bitwarden/nginx"), ($"{root}/letsencrypt", "/etc/letsencrypt"),
                             ($"{root}/ssl", "/etc/ssl"), ($"{root}/logs/nginx", "/var/log/nginx")] },
        };

        // Optional services, gated by the persisted config.yml (so status/update/uninstall see them
        // too — not just install).
        var config = Setup.StandardConfig.Load(root);
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

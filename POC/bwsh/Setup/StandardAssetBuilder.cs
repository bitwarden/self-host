namespace Bit.SelfHost.Setup;

/// <summary>
/// Generates a standard-deployment bwdata/ — the Setup-container replacement. Ports
/// EnvironmentFileBuilder, CertBuilder, NginxConfigBuilder, AppIdBuilder + config.yml save.
/// Identity cert uses System.Security.Cryptography (no openssl).
/// </summary>
public static class StandardAssetBuilder
{
    public sealed record InstallParams(
        string InstallationId, string InstallationKey, string Region, string Database,
        IReadOnlyDictionary<string, string>? Config = null);

    /// <summary>
    /// Secrets read back from an existing bwdata/ so re-rendering (`apply`, or a re-run `install`)
    /// reuses them instead of minting new ones — rotating the DB/identity passwords would mismatch
    /// the already-initialized mssql volume and break the install.
    /// </summary>
    private sealed record ExistingSecrets(
        string? IdentityCertPassword, string? DbPassword,
        string? InternalIdentityKey, string? OidcIdentityClientKey, string? DuoAKey,
        string? KeyConnectorCertPassword, string? InstallationId, string? InstallationKey);

    private const string DefaultCsp = "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https://haveibeenpwned.com; " +
        "child-src 'self' https://*.duosecurity.com https://*.duofederal.com; " +
        "frame-src 'self' https://*.duosecurity.com https://*.duofederal.com; " +
        "connect-src 'self' wss://{0} https://api.pwnedpasswords.com " +
        "https://api.2fa.directory; object-src 'self' blob:;";

    public static void BuildForInstaller(string root, StandardConfig config, InstallParams install)
    {
        var existing = ReadExistingSecrets(root);

        // Order matches Setup's Install(): cert first (yields the password the env file needs).
        // Reuse the identity cert + its password when already present so `apply` doesn't rotate it.
        var identityCertPath = Path.Combine(root, "identity", "identity.pfx");
        var identityCertPassword = existing.IdentityCertPassword ?? SecureRandom.String(32);
        if (existing.IdentityCertPassword is null || !File.Exists(identityCertPath))
            Pkcs12Cert.Write(identityCertPath, "Bitwarden IdentityServer", identityCertPassword);

        WriteEnvFiles(root, config, install, identityCertPassword, existing);
        if (config.EnableKeyConnector) WriteKeyConnector(root, config, existing);
        WriteNginx(root, config);
        WriteAppId(root, config);
        config.Save(root);
    }

    /// <summary>
    /// Generates the Key Connector override env + its filesystem cert (bwkc.pfx) sharing one
    /// password. Ports Setup's keyConnectorOverrideValues + CertBuilder.BuildForUpdater.
    /// </summary>
    private static void WriteKeyConnector(string root, StandardConfig config, ExistingSecrets existing)
    {
        var certPassword = existing.KeyConnectorCertPassword ?? SecureRandom.String(32);
        var values = new Dictionary<string, string>
        {
            ["keyConnectorSettings__webVaultUri"] = config.Url,
            ["keyConnectorSettings__identityServerUri"] = "http://identity:5000",
            ["keyConnectorSettings__database__provider"] = "json",
            ["keyConnectorSettings__database__jsonFilePath"] = "/etc/bitwarden/key-connector/data.json",
            ["keyConnectorSettings__rsaKey__provider"] = "certificate",
            ["keyConnectorSettings__certificate__provider"] = "filesystem",
            ["keyConnectorSettings__certificate__filesystemPath"] = "/etc/bitwarden/key-connector/bwkc.pfx",
            ["keyConnectorSettings__certificate__filesystemPassword"] = certPassword,
        };
        WriteEnv(Path.Combine(root, "env/key-connector.override.env"), values);

        var bwkcPath = Path.Combine(root, "key-connector", "bwkc.pfx");
        if (existing.KeyConnectorCertPassword is null || !File.Exists(bwkcPath))
            Pkcs12Cert.Write(bwkcPath, "Bitwarden Key Connector", certPassword);
    }

    private static void WriteEnvFiles(string root, StandardConfig config, InstallParams install,
        string identityCertPassword, ExistingSecrets existing)
    {
        Directory.CreateDirectory(Path.Combine(root, "docker"));
        Directory.CreateDirectory(Path.Combine(root, "env"));

        var dbPassword = existing.DbPassword ?? SecureRandom.String(32);
        var database = string.IsNullOrEmpty(install.Database) ? "vault" : install.Database;

        // Manifest wins when it provides id/key; otherwise keep what's on disk (apply re-render).
        var installationId = !string.IsNullOrEmpty(install.InstallationId)
            ? install.InstallationId : existing.InstallationId ?? Guid.Empty.ToString();
        var installationKey = !string.IsNullOrEmpty(install.InstallationKey)
            ? install.InstallationKey : existing.InstallationKey ?? string.Empty;
        var connectionString =
            $"Data Source=tcp:mssql,1433;Initial Catalog={database};User ID=sa;Password={dbPassword};" +
            "MultipleActiveResultSets=False;Encrypt=True;Connect Timeout=30;TrustServerCertificate=True;Persist Security Info=False";

        var global = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["globalSettings__selfHosted"] = "true",
            ["globalSettings__baseServiceUri__vault"] = "http://localhost",
            ["globalSettings__pushRelayBaseUri"] = "https://push.bitwarden.com",
        };

        var mssql = new Dictionary<string, string>
        {
            ["ACCEPT_EULA"] = "Y",
            ["MSSQL_PID"] = "Express",
            ["SA_PASSWORD"] = "SECRET",
        };

        var globalOverride = new Dictionary<string, string>
        {
            ["globalSettings__baseServiceUri__vault"] = config.Url,
            ["globalSettings__baseServiceUri__cloudRegion"] = install.Region,
            ["globalSettings__sqlServer__connectionString"] = $"\"{connectionString.Replace("\"", "\\\"")}\"",
            ["globalSettings__identityServer__certificatePassword"] = identityCertPassword,
            ["globalSettings__internalIdentityKey"] = existing.InternalIdentityKey ?? SecureRandom.String(64),
            ["globalSettings__oidcIdentityClientKey"] = existing.OidcIdentityClientKey ?? SecureRandom.String(64),
            ["globalSettings__duo__aKey"] = existing.DuoAKey ?? SecureRandom.String(64),
            ["globalSettings__installation__id"] = installationId,
            ["globalSettings__installation__key"] = installationKey,
            ["globalSettings__yubico__clientId"] = "REPLACE",
            ["globalSettings__yubico__key"] = "REPLACE",
            ["globalSettings__mail__replyToEmail"] = $"no-reply@{config.Domain}",
            ["globalSettings__mail__smtp__host"] = "REPLACE",
            ["globalSettings__mail__smtp__port"] = "587",
            ["globalSettings__mail__smtp__ssl"] = "false",
            ["globalSettings__mail__smtp__username"] = "REPLACE",
            ["globalSettings__mail__smtp__password"] = "REPLACE",
            ["globalSettings__disableUserRegistration"] = "false",
            ["globalSettings__hibpApiKey"] = "REPLACE",
            ["adminSettings__admins"] = string.Empty,
        };
        if (!config.PushNotifications)
            globalOverride["globalSettings__pushRelayBaseUri"] = "REPLACE";

        // Manifest `config:` passthrough LAST so it overrides the defaults above (e.g. flips
        // mail__smtp__host from REPLACE to the operator's value) — mirrors LiteDeployment.
        if (install.Config is not null)
            foreach (var kv in install.Config)
                globalOverride[kv.Key] = kv.Value;

        var mssqlOverride = new Dictionary<string, string>
        {
            ["SA_PASSWORD"] = dbPassword,
            ["DATABASE"] = database,
        };

        WriteEnv(Path.Combine(root, "docker/global.env"), global);
        WriteEnv(Path.Combine(root, "docker/mssql.env"), mssql);
        WriteEnv(Path.Combine(root, "env/global.override.env"), globalOverride);
        WriteEnv(Path.Combine(root, "env/mssql.override.env"), mssqlOverride);

        var uid = Path.Combine(root, "env/uid.env");
        if (!File.Exists(uid)) File.WriteAllText(uid, string.Empty);
    }

    private static void WriteEnv(string path, IDictionary<string, string> values)
    {
        var template = Templating.Read("EnvironmentFile");
        var model = new EnvModel(values);
        File.WriteAllText(path, template(model));
        SetSecretMode(path);
    }

    private static void WriteNginx(string root, StandardConfig config)
    {
        if (!config.GenerateNginxConfig) return;
        Directory.CreateDirectory(Path.Combine(root, "nginx"));

        var model = new NginxModel
        {
            Ssl = config.Ssl,
            Domain = config.Domain,
            Url = config.Url,
            EnableKeyConnector = config.EnableKeyConnector,
            EnableScim = config.EnableScim,
            RealIps = config.RealIps,
            ContentSecurityPolicy = string.Format(
                string.IsNullOrWhiteSpace(config.NginxHeaderContentSecurityPolicy)
                    ? DefaultCsp : config.NginxHeaderContentSecurityPolicy, config.Domain),
        };
        if (config.Ssl)
        {
            model.CertificatePath = config.SslCertificatePath;
            model.KeyPath = config.SslKeyPath;
            model.CaPath = config.SslCaPath;
            model.DiffieHellmanPath = config.SslDiffieHellmanPath;
            model.SslProtocols = string.IsNullOrWhiteSpace(config.SslVersions) ? "TLSv1.2 TLSv1.3" : config.SslVersions;
            model.SslCiphers = string.IsNullOrWhiteSpace(config.SslCiphersuites)
                ? "ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:" +
                  "ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-CHACHA20-POLY1305:ECDHE-RSA-CHACHA20-POLY1305:" +
                  "DHE-RSA-AES128-GCM-SHA256:DHE-RSA-AES256-GCM-SHA384:DHE-RSA-CHACHA20-POLY1305;"
                : config.SslCiphersuites;
            model.SslCurves = string.IsNullOrWhiteSpace(config.SslCurves)
                ? "X25519:X25519MLKEM768:prime256v1:secp384r1" : config.SslCurves;
        }

        var template = Templating.Read("NginxConfig");
        File.WriteAllText(Path.Combine(root, "nginx/default.conf"), template(model));
    }

    private static void WriteAppId(string root, StandardConfig config)
    {
        Directory.CreateDirectory(Path.Combine(root, "web"));
        var template = Templating.Read("AppId");
        File.WriteAllText(Path.Combine(root, "web/app-id.json"), template(new { config.Url }));
    }

    private static void SetSecretMode(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600
    }

    /// <summary>Reads the secrets/identifiers from an existing bwdata/ (all null on a fresh install).</summary>
    private static ExistingSecrets ReadExistingSecrets(string root)
    {
        var global = ReadEnv(Path.Combine(root, "env/global.override.env"));
        var mssql = ReadEnv(Path.Combine(root, "env/mssql.override.env"));
        var kc = ReadEnv(Path.Combine(root, "env/key-connector.override.env"));
        return new ExistingSecrets(
            IdentityCertPassword: global.GetValueOrDefault("globalSettings__identityServer__certificatePassword"),
            DbPassword: mssql.GetValueOrDefault("SA_PASSWORD"),
            InternalIdentityKey: global.GetValueOrDefault("globalSettings__internalIdentityKey"),
            OidcIdentityClientKey: global.GetValueOrDefault("globalSettings__oidcIdentityClientKey"),
            DuoAKey: global.GetValueOrDefault("globalSettings__duo__aKey"),
            KeyConnectorCertPassword: kc.GetValueOrDefault("keyConnectorSettings__certificate__filesystemPassword"),
            InstallationId: global.GetValueOrDefault("globalSettings__installation__id"),
            InstallationKey: global.GetValueOrDefault("globalSettings__installation__key"));
    }

    /// <summary>Parse a KEY=VALUE env file into a dict (first '=' splits; '#'/blank lines skipped).</summary>
    private static Dictionary<string, string> ReadEnv(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return values;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            values[line[..eq]] = line[(eq + 1)..];
        }
        return values;
    }

    // Handlebars models (concrete classes so the template engine can bind by property).
    private sealed class EnvModel
    {
        public EnvModel(IEnumerable<KeyValuePair<string, string>> values) =>
            Variables = values.Select(v => new Kvp(v.Key, v.Value)).ToList();
        public IReadOnlyList<Kvp> Variables { get; }
        public sealed record Kvp(string Key, string Value);
    }

    private sealed class NginxModel
    {
        public bool Ssl { get; set; }
        public bool EnableKeyConnector { get; set; }
        public bool EnableScim { get; set; }
        public string? Domain { get; set; }
        public string? Url { get; set; }
        public string? CertificatePath { get; set; }
        public string? KeyPath { get; set; }
        public string? CaPath { get; set; }
        public string? DiffieHellmanPath { get; set; }
        public string? SslCiphers { get; set; }
        public string? SslProtocols { get; set; }
        public string? SslCurves { get; set; }
        public string? ContentSecurityPolicy { get; set; }
        public List<string>? RealIps { get; set; }
    }
}

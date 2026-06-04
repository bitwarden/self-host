using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bit.SelfHost.Setup;

/// <summary>
/// Writes a self-signed web TLS cert as the PEM pair nginx needs (certificate.crt + private.key),
/// for the default HTTPS deployment. Separate from <see cref="Pkcs12Cert"/>, which emits PFX for the
/// internal identity/Key Connector certs.
/// </summary>
public static class WebCert
{
    public static void WriteSelfSigned(string certPath, string keyPath, string domain)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(certPath))!);

        using var rsa = RSA.Create(4096);
        var request = new CertificateRequest(
            $"C=US, ST=California, L=Santa Barbara, O=Bitwarden Inc., OU=Bitwarden, CN={domain}",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // SAN is required by modern clients (CN alone is ignored).
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(domain);
        request.CertificateExtensions.Add(san.Build());

        // CA:true lets the cert be added to a device trust store.
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: false));

        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(100));

        File.WriteAllText(certPath, cert.ExportCertificatePem());
        File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600
    }
}

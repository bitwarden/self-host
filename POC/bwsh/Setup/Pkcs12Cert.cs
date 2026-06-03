using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Bit.SelfHost.Setup;

/// <summary>
/// Writes a self-signed PKCS12 cert (RSA-4096, 100-year). Replaces Setup's openssl shellouts
/// (req -x509 + pkcs12 -export) with System.Security.Cryptography — no openssl, cross-platform.
/// Used for the Identity Server cert and the Key Connector cert.
/// </summary>
public static class Pkcs12Cert
{
    public static void Write(string path, string commonName, string password)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using var rsa = RSA.Create(4096);
        var request = new CertificateRequest(
            $"CN={commonName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(100)); // Setup: -days 36500

        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600
    }
}

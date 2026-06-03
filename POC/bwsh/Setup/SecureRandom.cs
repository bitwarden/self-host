using System.Security.Cryptography;
using System.Text;

namespace Bit.SelfHost.Setup;

/// <summary>Port of Setup's Helpers.SecureRandomString — unbiased random string from a charset.</summary>
public static class SecureRandom
{
    private const string Alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string Numeric = "0123456789";

    public static string String(int length, bool alpha = true, bool numeric = true)
    {
        var characters = (alpha ? Alpha : "") + (numeric ? Numeric : "");
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (characters.Length == 0) throw new ArgumentOutOfRangeException(nameof(characters));

        const int byteSize = 0x100;
        var outOfRangeStart = byteSize - (byteSize % characters.Length);
        using var rng = RandomNumberGenerator.Create();
        var sb = new StringBuilder();
        var buffer = new byte[128];
        while (sb.Length < length)
        {
            rng.GetBytes(buffer);
            for (var i = 0; i < buffer.Length && sb.Length < length; ++i)
            {
                if (outOfRangeStart <= buffer[i]) continue; // avoid modulo bias
                sb.Append(characters[buffer[i] % characters.Length]);
            }
        }
        return sb.ToString();
    }
}

using System.Security.Cryptography;
using System.Text;

namespace CodexModelSwitcher.Infrastructure;

public static class Hashing
{
    public static string Sha256(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    public static string Sha256(string value) => Sha256(Encoding.UTF8.GetBytes(value));

    public static string? Sha256File(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

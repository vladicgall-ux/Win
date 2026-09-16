using System.Security.Cryptography;

namespace WinFileRecovery.Core.Verification;

public static class Sha256Hasher
{
    public static string ComputeHex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

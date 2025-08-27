using System.Security.Cryptography;
using System.Text;

public static class SeedGenerator
{
    public static byte[] GenerateUniqueSeed(int entropyBytes = 32)
    {
        // Secure random entropy
        byte[] randomBytes = new byte[entropyBytes];
        RandomNumberGenerator.Fill(randomBytes);

        // Timestamp entropy
        long timestamp = DateTime.UtcNow.Ticks;
        byte[] timestampBytes = BitConverter.GetBytes(timestamp);

        // GUID for uniqueness
        byte[] guidBytes = Guid.NewGuid().ToByteArray();

        // Combine all entropy sources
        byte[] combined = randomBytes
            .Concat(timestampBytes)
            .Concat(guidBytes)
            .ToArray();

        // Hash to normalize size and ensure uniformity (SHA-256 = 32 bytes)
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(combined);
    }

    public static string GenerateUniqueSeedBase64()
    {
        return Convert.ToBase64String(GenerateUniqueSeed());
    }

    public static string GenerateUniqueSeedHex()
    {
        return BitConverter.ToString(GenerateUniqueSeed()).Replace("-", "").ToLowerInvariant();
    }
}
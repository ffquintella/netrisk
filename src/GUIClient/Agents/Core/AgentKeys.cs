using System;
using System.Security.Cryptography;
using Tools;
using Tools.Security;

namespace GUIClient.Agents;

/// <summary>
/// Issues and checks agent keys (T347, S55). A key is a bearer secret handled per the Security Conventions table:
/// drawn from the CSPRNG (<see cref="RandomGenerator.RandomToken"/>, 256 bits), stored only as
/// <see cref="HashTool.CreateSha256"/>, compared with <see cref="CryptographicOperations.FixedTimeEquals"/>.
/// <c>AgentKeyHandlingTest</c> is the evidence for each.
/// </summary>
public static class AgentKeyIssuer
{
    /// <summary>Marks the value as a NetRisk agent key so secret scanners and people can recognise one.</summary>
    public const string Prefix = "nrak_";

    /// <summary>Entropy of a key, in bytes: 256 bits.</summary>
    public const int KeyEntropyBytes = 32;

    /// <summary>A presented key longer than this is refused before it is hashed (input bound, NRM §5.3.5).</summary>
    public const int MaxPresentedKeyLength = 128;

    /// <summary>A fresh key. Returned to the agent once and then forgotten — only <see cref="Hash"/> of it is kept.</summary>
    public static string NewKey() => Prefix + RandomGenerator.RandomToken(KeyEntropyBytes);

    /// <summary>The at-rest form of a key: upper-case hex SHA-256, 64 characters.</summary>
    public static string Hash(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return HashTool.CreateSha256(key);
    }

    /// <summary>
    /// Whether <paramref name="presentedKey"/> is the key hashed into <paramref name="storedHash"/>: a constant-time
    /// comparison of the decoded digests. A malformed stored hash never matches.
    /// </summary>
    public static bool Matches(string? presentedKey, string? storedHash)
    {
        if (string.IsNullOrEmpty(presentedKey) || presentedKey.Length > MaxPresentedKeyLength) return false;
        if (!TryDecodeDigest(storedHash, out var stored)) return false;

        var presented = Convert.FromHexString(HashTool.CreateSha256(presentedKey));
        return CryptographicOperations.FixedTimeEquals(presented, stored);
    }

    /// <summary>
    /// The short code shown to the agent and in the approval dialog, so the user can match the two. Derived from the
    /// pending registration's random id, never from the key.
    /// </summary>
    public static string Fingerprint(string seed)
    {
        ArgumentException.ThrowIfNullOrEmpty(seed);
        var hex = HashTool.CreateSha256("netrisk-agent-fingerprint:" + seed);
        return hex[..4] + "-" + hex[4..8];
    }

    private static bool TryDecodeDigest(string? hex, out byte[] digest)
    {
        digest = Array.Empty<byte>();
        if (hex is null || hex.Length != 64) return false;
        try
        {
            digest = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// An agent key on its way back to the agent. <see cref="ToString"/> is redacted so the key cannot reach a log by
/// accident; the value is read only through <see cref="Reveal"/>, by the one response that returns it.
/// </summary>
public sealed class IssuedAgentKey
{
    private readonly string _value;

    internal IssuedAgentKey(string value) => _value = value;

    /// <summary>The key itself. Call only to put it in the one response that returns it.</summary>
    public string Reveal() => _value;

    public override string ToString() => AgentKeyIssuer.Prefix + "[redacted]";
}

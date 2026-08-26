using System;
using System.Security.Cryptography;

namespace Tools;

/// <summary>
/// Random token generation for security-relevant values.
///
/// Every caller of this class produces something an attacker would like to guess: the JWT signing
/// key (<c>EnvironmentService.ServerSecretToken</c>), password-reset link keys
/// (<c>LinksService.CreateLink</c>), file and report access keys, generated user passwords and the
/// SAML request id the desktop client polls with. The original implementation drew all of them from
/// one shared <see cref="Random"/>, whose 256-bit xoshiro256** state is fully recoverable from a
/// handful of observed outputs — and several of those outputs are handed to the user by design (a
/// reset link arrives by e-mail, a file key comes back in the upload response). Recovering the state
/// from values an attacker legitimately receives therefore let them predict *other* people's reset
/// keys. That is CWE-338, and the fix is to draw from the operating system CSPRNG instead.
/// </summary>
public static class RandomGenerator
{
    /// <summary>
    /// The alphabet tokens are drawn from. Deliberately unchanged from the original: existing
    /// callers embed these values in URLs and file names, and widening it would change nothing
    /// about the security while risking a round-trip somewhere.
    /// </summary>
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnoprstuvxijtuwyz0123456789";

    /// <summary>
    /// A random string of <paramref name="length"/> characters drawn uniformly from
    /// <see cref="Chars"/> using the platform CSPRNG.
    /// </summary>
    /// <remarks>
    /// <see cref="RandomNumberGenerator.GetInt32(int)"/> is used rather than reducing raw bytes
    /// modulo the alphabet size: the alphabet has 65 characters, which does not divide 256, so a
    /// modulo would make the first 60 characters slightly likelier than the rest. That bias is
    /// small, but it is free to avoid and a biased key is a shorter key.
    /// </remarks>
    public static string RandomString(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));

        var buffer = new char[length];
        for (var i = 0; i < length; i++)
            buffer[i] = Chars[RandomNumberGenerator.GetInt32(Chars.Length)];

        return new string(buffer);
    }

    /// <summary>
    /// A URL-safe base64 token carrying <paramref name="byteCount"/> bytes of entropy.
    ///
    /// Preferred over <see cref="RandomString"/> for anything new: it says how much entropy it
    /// carries in the unit that matters, rather than leaving the reader to work out that 40
    /// characters of a 65-character alphabet is about 240 bits.
    /// </summary>
    public static string RandomToken(int byteCount = 32)
    {
        if (byteCount <= 0) throw new ArgumentOutOfRangeException(nameof(byteCount));

        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

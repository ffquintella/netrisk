namespace Tools.Security;

public static class HashTool
{
    /// <summary>
    /// MD5 of the input. Retained only to read data hashed by older versions — see
    /// <see cref="CreateSha256"/> for anything new. MD5 is collision-broken, so it must never be
    /// used to decide whether two values are the same when an attacker controls either of them.
    /// </summary>
    public static string CreateMD5(string input)
    {
        // Use input string to calculate MD5 hash
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);

        return Convert.ToHexString(hashBytes); // .NET 5 +
    }

    /// <summary>
    /// SHA-1 of the input. Retained for compatibility with stored values only; SHA-1 is
    /// collision-broken and must not be used for new security decisions.
    /// </summary>
    public static string CreateSha1(string input)
    {
        // Use input string to calculate SHA-1 hash
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.SHA1.HashData(inputBytes);

        return Convert.ToHexString(hashBytes); // .NET 5 +
    }

    /// <summary>
    /// SHA-256 of the input — the default for hashing a high-entropy secret into a lookup key
    /// (Track 7 milestone 7.4.2). Note that this is a plain hash, not a password KDF: it is correct
    /// for a 240-bit random token and wrong for a human-chosen password, which needs bcrypt.
    /// </summary>
    public static string CreateSha256(string input)
    {
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(inputBytes);

        return Convert.ToHexString(hashBytes);
    }
}

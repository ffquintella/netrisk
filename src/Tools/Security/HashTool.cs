namespace Tools.Security;

public static class HashTool
{
    public static string CreateMD5(string input)
    {
        // Use input string to calculate MD5 hash
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.MD5.HashData(inputBytes);

        return Convert.ToHexString(hashBytes); // .NET 5 +
    }
    
    public static string CreateSha1(string input)
    {
        // Use input string to calculate MD5 hash
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hashBytes = System.Security.Cryptography.SHA1.HashData(inputBytes);

        return Convert.ToHexString(hashBytes); // .NET 5 +
    }
}
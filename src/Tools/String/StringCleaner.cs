namespace Tools.String;

public static class StringCleaner
{
    public static string CleanEmptyChars(string input, string replacement = "_")
    {
        var output = input.Replace(" ", replacement);

        return output;
    }
    
    public static string ReplaceNonAlphanumeric(string input, string replacement = "_")
    {
        var output = System.Text.RegularExpressions.Regex.Replace(input, "[^a-zA-Z0-9]", replacement);

        return output;
    }
}
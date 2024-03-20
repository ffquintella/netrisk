using System.Text.RegularExpressions;

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
    
    public static bool IsSafeFilename(string filename)
    {
        if ((filename.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0) == false) return false;
        // Define a regular expression that matches any character that is not a letter, number, underscore or hyphen
        var regex = new Regex(@"[^a-zA-Z0-9_-]");

        // If the fileId matches this regular expression, it contains unsafe characters
        if (regex.IsMatch(filename))
        {
            return false;
        }

        return true;
    }
}
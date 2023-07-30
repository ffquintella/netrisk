namespace Tools.String;

public static class StringCleaner
{
    public static string CleanEmptyChars(string input, string replacement = "_")
    {
        var output = input.Replace(" ", replacement);

        return output;
    }
}
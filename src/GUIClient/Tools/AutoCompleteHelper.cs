using System.Text.RegularExpressions;

namespace GUIClient.Tools;

public static class AutoCompleteHelper
{
    public static int? ExtractNumber(string text)
    {
        var outputArr = text.Split('(', ')');

        if (outputArr.Length < 2) return null;

        return int.Parse(outputArr[1]);
    }
}
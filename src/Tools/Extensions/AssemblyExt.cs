using System.Reflection;

namespace Tools.Extensions;

public static class AssemblyExt
{
    public static string? AssemblyDirectory (this Assembly? value)
    {
        if(value == null) return null;
        string location = value.Location;
        string path = string.Empty;
        if (location.StartsWith("https"))
        {
            UriBuilder uri = new UriBuilder(location);
            path = Uri.UnescapeDataString(uri.Path);
        }
        else path = location;

        return Path.GetDirectoryName(path);
    }
}
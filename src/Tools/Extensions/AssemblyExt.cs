using System.Reflection;

namespace Tools.Extensions;

public static class AssemblyExt
{
    public static string? AssemblyDirectory (this Assembly? value)
    {
        if(value == null) return null;
        string location = value.Location;
        UriBuilder uri = new UriBuilder(location);
        string path = Uri.UnescapeDataString(uri.Path);
        return Path.GetDirectoryName(path);
    }
}
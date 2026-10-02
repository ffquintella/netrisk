using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>Locates the client's source and resource files for the Hosts view tests.</summary>
internal static class HostsTestFiles
{
    public static readonly string[] ResourceFiles =
        ["Localization.resx", "Localization.en-US.resx", "Localization.pt-BR.resx"];

    public static string GuiClientRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var project = Path.Combine(directory.FullName, "GUIClient", "GUIClient.csproj");
            if (File.Exists(project)) return Path.GetDirectoryName(project)!;
        }

        throw new InvalidOperationException(
            $"Could not find GUIClient/GUIClient.csproj walking up from {AppContext.BaseDirectory}.");
    }

    public static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(GuiClientRoot(), relativePath));

    public static HashSet<string> DeclaredKeys(string resourceFile) =>
        XDocument.Load(Path.Combine(GuiClientRoot(), "Resources", resourceFile)).Root!
            .Elements("data")
            .Select(d => d.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The keys from <paramref name="keys"/> missing from any of the three resource files.</summary>
    public static List<string> MissingFromAnyResource(IEnumerable<string> keys)
    {
        var wanted = keys.Distinct(StringComparer.Ordinal).ToList();
        var missing = new List<string>();

        foreach (var file in ResourceFiles)
        {
            var declared = DeclaredKeys(file);
            missing.AddRange(wanted.Where(k => !declared.Contains(k)).Select(k => $"{k} (missing from {file})"));
        }

        return missing;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using GUIClient.Tools.RiskIndex;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// The client's real resource text for the Cyber Risk Overview tests: the formatters are checked against the sentences the
/// user reads, not against stand-ins, so a template that loses a placeholder fails here.
/// </summary>
internal static class RiskIndexTestText
{
    public static readonly string[] ResourceFiles = ["Localization.resx", "Localization.en-US.resx", "Localization.pt-BR.resx"];

    public static string ClientDirectory([CallerFilePath] string thisFile = "")
    {
        var directory = new FileInfo(thisFile).Directory;
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "src", "GUIClient")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), "src",
            "GUIClient");
    }

    public static IReadOnlyDictionary<string, string> Load(string file) =>
        XDocument.Load(Path.Combine(ClientDirectory(), "Resources", file)).Root!
            .Elements("data")
            .Where(d => d.Attribute("name") is not null)
            .GroupBy(d => d.Attribute("name")!.Value, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Element("value")?.Value ?? "", StringComparer.Ordinal);

    public static TextLookup Lookup(string file)
    {
        var texts = Load(file);
        return key => texts.TryGetValue(key, out var value) ? value : null;
    }

    public static TextLookup Portuguese { get; } = Lookup("Localization.pt-BR.resx");

    public static TextLookup English { get; } = Lookup("Localization.resx");
}

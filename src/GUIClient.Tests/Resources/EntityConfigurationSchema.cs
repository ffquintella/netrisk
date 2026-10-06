using System.IO;
using System.Runtime.CompilerServices;
using Model.Entities;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace GUIClient.Tests.Resources;

/// <summary>
/// Loads <c>src/API/EntitiesConfiguration.yaml</c> the way <c>EntitiesService.GetEntitiesConfigurationAsync</c>
/// does — same model, same camel-case convention — so the guards below see the definitions, labels
/// and icons the desktop client is actually sent.
/// </summary>
internal static class EntityConfigurationSchema
{
    public static EntitiesConfiguration Load([CallerFilePath] string thisFile = "")
    {
        // .../src/GUIClient.Tests/Resources/EntityConfigurationSchema.cs → .../src
        var src = new FileInfo(thisFile).Directory!.Parent!.Parent!.FullName;
        var path = Path.Combine(src, "API", "EntitiesConfiguration.yaml");

        Assert.True(File.Exists(path), $"{path} is missing.");

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<EntitiesConfiguration>(File.ReadAllText(path));
    }

    /// <summary>The three resource files the client ships: the neutral fallback plus two cultures.</summary>
    public static readonly string[] ResourceFiles =
    [
        "Localization.resx", "Localization.en-US.resx", "Localization.pt-BR.resx"
    ];

    public static string ResourcePath(string file, [CallerFilePath] string thisFile = "")
    {
        var src = new FileInfo(thisFile).Directory!.Parent!.Parent!.FullName;
        return Path.Combine(src, "GUIClient", "Resources", file);
    }
}

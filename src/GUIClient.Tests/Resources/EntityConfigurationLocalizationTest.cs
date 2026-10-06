using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace GUIClient.Tests.Resources;

/// <summary>
/// Every definition name and every property label in <c>EntitiesConfiguration.yaml</c> resolves in
/// all three resource files (Stage 9.1, S41 §8).
///
/// The entity form builds itself from that file and passes each label, and each definition name, to
/// the localizer with the raw text as the fallback. A missing key therefore does not fail anywhere:
/// the form renders, the build is clean, and a Portuguese user reads an English camel-case word. That
/// is how <c>applicationModule.parentApplication</c>'s label <c>Application</c> and the
/// <c>team</c> type shipped — found while specifying Stage 9.1, fixed with this test. The scan is
/// over the whole schema, not just the types the stage adds, so the next property cannot repeat it.
/// </summary>
public class EntityConfigurationLocalizationTest
{
    private static HashSet<string> DeclaredKeys(string file) =>
        XDocument.Load(EntityConfigurationSchema.ResourcePath(file)).Root!
            .Elements("data")
            .Select(d => d.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

    private static List<(string Key, string Where)> SchemaKeys()
    {
        var configuration = EntityConfigurationSchema.Load();
        var keys = new List<(string, string)>();

        foreach (var (definitionName, definition) in configuration.Definitions)
        {
            keys.Add((definitionName, $"definition {definitionName}"));

            foreach (var (propertyName, property) in definition.Properties)
                keys.Add((property.Label, $"{definitionName}.{propertyName}.label"));
        }

        return keys;
    }

    [Fact]
    public void TestTheSchemaIsReadAndCarriesTheStage91Types()
    {
        var configuration = EntityConfigurationSchema.Load();

        // A parse that silently produced nothing would make every assertion below vacuous.
        Assert.Contains("strategicObjective", configuration.Definitions.Keys);
        Assert.Contains("itService", configuration.Definitions.Keys);
        Assert.True(SchemaKeys().Count > 50);
    }

    [Theory]
    [InlineData("Localization.resx")]
    [InlineData("Localization.en-US.resx")]
    [InlineData("Localization.pt-BR.resx")]
    public void TestEveryDefinitionNameAndLabelResolvesIn(string file)
    {
        var declared = DeclaredKeys(file);

        var missing = SchemaKeys()
            .Where(k => !declared.Contains(k.Key))
            .Select(k => $"'{k.Key}' ({k.Where})")
            .Distinct()
            .OrderBy(m => m)
            .ToList();

        Assert.True(missing.Count == 0,
            $"The entity form falls back to the raw text for a key {file} does not declare, so these "
            + "would render untranslated:" + string.Join("", missing.Select(m => "\n  " + m)));
    }

    [Fact]
    public void TestTheResourceFilesTheGuardReadsExist()
    {
        foreach (var file in EntityConfigurationSchema.ResourceFiles)
            Assert.True(File.Exists(EntityConfigurationSchema.ResourcePath(file)), $"{file} is missing.");
    }
}

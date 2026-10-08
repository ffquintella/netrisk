using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using DAL.Enums;
using Model.AiGovernance;
using Model.ThirdParties;
using Xunit;

namespace GUIClient.Tests.Track9;

public class Track9RegistersLocalizationTest
{
    [Fact]
    public void TestDynamicRegisterEnumKeysExistInEveryResource()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AddEnum<ThirdPartyStatus>(keys);
        AddEnum<ThirdPartyLinkKind>(keys);
        AddEnum<ThirdPartyDataLocationPurpose>(keys);
        AddEnum<HecvatVariant>(keys);
        AddEnum<HecvatAnswer>(keys);
        AddEnum<HecvatState>(keys);
        AddEnum<SbomFormat>(keys);
        AddEnum<PersonalDataCategory>(keys);
        AddEnum<LgpdLegalBasis>(keys);
        AddEnum<InternationalTransferMechanism>(keys);
        AddEnum<DataLocationPurpose>(keys);
        AddEnum<LegalRequirementKind>(keys);
        AddEnum<DpiaStatus>(keys);
        AddEnum<DpiaResidualRisk>(keys);
        AddEnum<DpiaLinkKind>(keys);
        AddEnum<AiModelKind>(keys);
        AddEnum<AiModelSource>(keys);
        AddEnum<AiModelStatus>(keys);
        AddEnum<AiModelRiskTier>(keys);
        AddEnum<AiHumanOversight>(keys);
        AddEnum<AiModelDataUsage>(keys);
        AddEnum<AiModelMetric>(keys);
        AddEnum<AiModelEvaluationState>(keys);

        foreach (var resource in new[] { "Localization.resx", "Localization.en-US.resx", "Localization.pt-BR.resx" })
        {
            var path = Path.Combine(GuiClientRoot(), "Resources", resource);
            var present = XDocument.Load(path).Descendants("data")
                .Select(element => (string?)element.Attribute("name"))
                .Where(name => name is not null)
                .ToHashSet(StringComparer.Ordinal);
            var missing = keys.Where(key => !present.Contains(key)).OrderBy(key => key).ToList();
            Assert.True(missing.Count == 0, $"{resource} is missing dynamic Track 9 enum keys: {string.Join(", ", missing)}");
        }
    }

    private static void AddEnum<T>(ISet<string> keys) where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>()) keys.Add($"Track9{typeof(T).Name}{value}");
    }

    private static string GuiClientRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "GUIClient");
            if (File.Exists(Path.Combine(path, "GUIClient.csproj"))) return path;
        }
        throw new DirectoryNotFoundException("GUIClient project was not found.");
    }
}

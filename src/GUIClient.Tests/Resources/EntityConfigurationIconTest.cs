using System;
using System.Linq;
using Material.Icons;
using Xunit;

namespace GUIClient.Tests.Resources;

/// <summary>
/// Every <c>iconKind</c> in <c>EntitiesConfiguration.yaml</c> names a real <see cref="MaterialIconKind"/>
/// (Stage 9.1, S41 §8).
///
/// <c>ExtensionMethods.GetIcon</c> parses the name with <c>Enum.TryParse</c> and falls back to
/// <see cref="MaterialIconKind.Forbid"/> when it does not parse — so a misspelt icon shows a "no
/// entry" sign in the entities tree and nothing fails. The parse here is the same call.
/// </summary>
public class EntityConfigurationIconTest
{
    [Fact]
    public void TestEveryIconKindParsesToAMaterialIcon()
    {
        var configuration = EntityConfigurationSchema.Load();

        var offenders = configuration.Definitions
            .Where(d => d.Value.IconKind is null || !Enum.TryParse<MaterialIconKind>(d.Value.IconKind, out _))
            .Select(d => $"{d.Key}: '{d.Value.IconKind ?? "(none)"}'")
            .OrderBy(o => o)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These would render as MaterialIconKind.Forbid:" + string.Join("", offenders.Select(o => "\n  " + o)));
    }

    [Theory]
    [InlineData("strategicObjective", MaterialIconKind.BullseyeArrow)]
    [InlineData("itService", MaterialIconKind.ServerNetwork)]
    public void TestTheStage91TypesCarryTheirSpecifiedIcons(string definition, MaterialIconKind expected)
    {
        var configuration = EntityConfigurationSchema.Load();

        Assert.True(Enum.TryParse<MaterialIconKind>(configuration.Definitions[definition].IconKind, out var icon));
        Assert.Equal(expected, icon);
    }
}

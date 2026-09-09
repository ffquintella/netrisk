using GUIClient.Tools;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// The scope cell of the governance grids (Track 8 milestone 8.3.3).
///
/// Each case here is a cell somebody read and could not act on: a numeric id where a name belonged,
/// and a blank where "the whole organization" belonged.
/// </summary>
[TestSubject(typeof(EntityScopeLabel))]
public class EntityScopeLabelTest
{
    [Fact]
    public void ANamedEntityIsShownByName()
    {
        Assert.Equal("Retail Bank", EntityScopeLabel.Describe(3, "Retail Bank", "Global"));
    }

    [Fact]
    public void TheOrganizationWideRowIsShownWithTheLocalizedGlobalWord()
    {
        // The word arrives from the view model's localizer, so a Portuguese install says "Global"
        // in its own words rather than showing an empty cell.
        Assert.Equal("Globais", EntityScopeLabel.Describe(null, null, "Globais"));

        // A name that somehow travelled with a null entity id does not change the answer: the row is
        // the organization-wide one, whatever else is attached to it.
        Assert.Equal("Global", EntityScopeLabel.Describe(null, "Retail Bank", "Global"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEntityWithNoUsableNameFallsBackToItsMarkedId(string? name)
    {
        // "#3", not "" and not "3": a blank cell reads as no scope at all, and a bare number reads
        // as a count beside the other numeric columns.
        Assert.Equal("#3", EntityScopeLabel.Describe(3, name, "Global"));
    }
}

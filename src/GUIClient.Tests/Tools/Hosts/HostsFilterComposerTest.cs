using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>
/// The Hosts list's one server-side filter (S38 §3.1). The server reads comma as AND and splits on
/// unescaped commas and pipes before it reads a value, so both the composition and the escaping are
/// what decides which hosts the user sees.
/// </summary>
[TestSubject(typeof(HostsFilterComposer))]
public class HostsFilterComposerTest
{
    [Fact]
    public void NothingChosenIsTheEmptyFilter()
    {
        Assert.Equal(string.Empty, HostsFilterComposer.Compose(null, null, null, null, null));
        Assert.Equal(string.Empty, HostsFilterComposer.Compose("   ", null, null, null, "  "));
    }

    [Fact]
    public void SearchTextAloneIsAHostNameContainsClause()
    {
        Assert.Equal("hostName@=web", HostsFilterComposer.Compose("  web ", null, null, null, null));
    }

    [Theory]
    [InlineData(42, "status==42")]
    [InlineData(27, "status==27")]
    public void StatusAloneIsAnEqualityClause(int status, string expected)
    {
        Assert.Equal(expected, HostsFilterComposer.Compose(null, status, null, null, null));
    }

    [Fact]
    public void TeamAloneIsATeamIdClause()
    {
        Assert.Equal("teamId==3", HostsFilterComposer.Compose("", null, 3, null, null));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void ACriticalityLevelIsAnEqualityClause(int level)
    {
        Assert.Equal($"criticality=={level}", HostsFilterComposer.Compose(null, null, null, level, null));
    }

    [Fact]
    public void NotSetCriticalitySelectsHostsWithNone()
    {
        Assert.Equal("criticality==null",
            HostsFilterComposer.Compose(null, null, null, HostsFilterComposer.CriticalityNotSet, null));
    }

    [Fact]
    public void EnvironmentIsAnExactMatchAndKeepsItsAccents()
    {
        Assert.Equal("environment==Produção", HostsFilterComposer.Compose(null, null, null, null, " Produção "));
    }

    [Fact]
    public void EveryFacetTogetherIsOneAndedFilterInAFixedOrder()
    {
        Assert.Equal("hostName@=web,status==42,teamId==3,criticality==5,environment==Produção",
            HostsFilterComposer.Compose("web", 42, 3, 5, "Produção"));
    }

    [Fact]
    public void NotSetCombinesWithTheOtherFacets()
    {
        Assert.Equal("hostName@=db,status==27,criticality==null",
            HostsFilterComposer.Compose("db", 27, null, HostsFilterComposer.CriticalityNotSet, null));
    }

    [Fact]
    public void EmptyTextDoesNotSwallowTheFacets()
    {
        Assert.Equal("teamId==7,environment==Homolog",
            HostsFilterComposer.Compose("", null, 7, null, "Homolog"));
    }

    [Theory]
    [InlineData("web,db", @"hostName@=web\,db")]
    [InlineData("a|b", @"hostName@=a\|b")]
    [InlineData(@"c:\temp", @"hostName@=c:\\temp")]
    public void SeparatorsInTheSearchTextAreEscaped(string text, string expected)
    {
        Assert.Equal(expected, HostsFilterComposer.Compose(text, null, null, null, null));
    }

    [Fact]
    public void SeparatorsInTheEnvironmentAreEscaped()
    {
        Assert.Equal(@"environment==Prod\,DR", HostsFilterComposer.Compose(null, null, null, null, "Prod,DR"));
        // Pinned on the server side by HostFilteringEndToEndTest.Environment_with_filter_separators_selects_only_its_host.
        Assert.Equal(@"environment==Prod\, EU\|A", HostsFilterComposer.Compose(null, null, null, null, "Prod, EU|A"));
    }
}

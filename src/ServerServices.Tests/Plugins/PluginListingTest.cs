using System.Collections.Generic;
using JetBrains.Annotations;
using Model.Plugins;
using ServerServices.Plugins;
using Xunit;

namespace ServerServices.Tests.Plugins;

/// <summary>
/// How the administration list behaves when the same plugin is installed more than once.
///
/// This is the regression cover for the reported defect: BastionVaultPlugin appeared twice, at
/// 1.2.0 and 1.2.1, each row with its own enabled switch, because each uploaded release had landed
/// in its own directory. The install path now replaces rather than accumulates
/// (<see cref="PluginPackageInstallerTest"/>), and this is the second half of the fix: a duplicate
/// that arrived some other way is listed once.
/// </summary>
[TestSubject(typeof(PluginListing))]
public class PluginListingTest
{
    private static PluginInfo Plugin(string name, string version, string package, bool enabled = false) =>
        new() { Name = name, Version = version, PackageName = package, IsEnabled = enabled };

    [Fact]
    public void TestTwoVersionsOfOnePluginCollapseToTheNewer()
    {
        var listed = PluginListing.CollapseVersions(
        [
            Plugin("BastionVaultPlugin", "1.2.0", "BastionVaultPlugin-1.2.0"),
            Plugin("BastionVaultPlugin", "1.2.1", "BastionVaultPlugin-1.2.1", enabled: true)
        ]);

        var plugin = Assert.Single(listed);
        Assert.Equal("BastionVaultPlugin", plugin.Name);
        Assert.Equal("1.2.1", plugin.Version);
        Assert.Equal("BastionVaultPlugin-1.2.1", plugin.PackageName);
    }

    /// <summary>
    /// The newer version wins whichever order the loaders were enumerated in -- the directory
    /// listing's order is the filesystem's business, not a version ordering.
    /// </summary>
    [Fact]
    public void TestTheOlderVersionDoesNotWinByComingFirst()
    {
        var listed = PluginListing.CollapseVersions(
        [
            Plugin("BastionVaultPlugin", "1.2.1", "b"),
            Plugin("BastionVaultPlugin", "1.2.0", "a")
        ]);

        Assert.Equal("1.2.1", Assert.Single(listed).Version);
    }

    [Fact]
    public void TestDifferentPluginsAreAllListed()
    {
        var listed = PluginListing.CollapseVersions(
        [
            Plugin("FaceIdPlugin", "1.0.1", "FaceIdPlugin"),
            Plugin("BastionVaultPlugin", "1.2.1", "BastionVaultPlugin")
        ]);

        Assert.Equal(2, listed.Count);
        Assert.Contains(listed, p => p.Name == "FaceIdPlugin");
        Assert.Contains(listed, p => p.Name == "BastionVaultPlugin");
    }

    /// <summary>
    /// Grouping is ordinal, because <c>Plugin_&lt;name&gt;_Enabled</c> is read with the plugin's own
    /// spelling: two plugins differing only in case are two settings, so collapsing them would show
    /// one switch that drives the other's plugin.
    /// </summary>
    [Fact]
    public void TestNamesDifferingOnlyInCaseAreDifferentPlugins()
    {
        var listed = PluginListing.CollapseVersions(
        [
            Plugin("MyPlugin", "1.0.0", "one"),
            Plugin("myplugin", "2.0.0", "two")
        ]);

        Assert.Equal(2, listed.Count);
    }

    [Fact]
    public void TestAnEmptyListStaysEmpty()
    {
        Assert.Empty(PluginListing.CollapseVersions(new List<PluginInfo>()));
    }

    /// <summary>
    /// A version string no one can parse must still produce exactly one row. Which of the two is
    /// shown is arbitrary; that only one is shown is not.
    /// </summary>
    [Fact]
    public void TestUnparseableVersionsStillCollapse()
    {
        var listed = PluginListing.CollapseVersions(
        [
            Plugin("Odd", "2026.1-rc1", "a"),
            Plugin("Odd", "", "b")
        ]);

        Assert.Single(listed);
    }
}

[TestSubject(typeof(PluginVersionOrder))]
public class PluginVersionOrderTest
{
    [Theory]
    [InlineData("1.2.0", "1.2.1")]
    [InlineData("1.2.9", "1.2.10")]
    [InlineData("1.9", "2.0")]
    [InlineData("1.0.0", "1.0.0.1")]
    public void TestTheNewerVersionOrdersHigher(string older, string newer)
    {
        Assert.True(PluginVersionOrder.Compare(older, newer) < 0);
        Assert.True(PluginVersionOrder.Compare(newer, older) > 0);
    }

    [Fact]
    public void TestEqualVersionsOrderEqually()
    {
        Assert.Equal(0, PluginVersionOrder.Compare("1.2.3", "1.2.3"));
        Assert.Equal(0, PluginVersionOrder.Compare(" 1.2.3 ", "1.2.3"));
    }

    /// <summary>
    /// "1.2.10" against "1.2.9" is the case an ordinal comparison gets backwards, which is the whole
    /// reason this helper exists rather than <c>string.Compare</c>.
    /// </summary>
    [Fact]
    public void TestOrderingIsNumericNotTextual()
    {
        Assert.True(string.CompareOrdinal("1.2.10", "1.2.9") < 0);
        Assert.True(PluginVersionOrder.Compare("1.2.10", "1.2.9") > 0);
    }

    [Fact]
    public void TestAVersionThatIsNotDottedNumericsStillOrdersTotally()
    {
        Assert.True(PluginVersionOrder.Compare("2026.1-rc1", "2026.1-rc2") < 0);
        Assert.True(PluginVersionOrder.Compare(null, "1.0.0") < 0);
        Assert.Equal(0, PluginVersionOrder.Compare(null, ""));
    }
}

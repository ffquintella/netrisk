using System.Collections.Generic;
using System.Linq;
using DAL.Entities;
using GUIClient.Tools.Track9;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Xunit;

namespace GUIClient.Tests.Track9;

/// <summary>
/// Stage 9.9 (S50 R10): no bulk selection in the user or role permission editor can grant the third-line marker,
/// which makes its holder read-only, administrators included; ticking it on purpose still can.
/// </summary>
[TestSubject(typeof(Track9ThirdLineMarker))]
public class Track9ThirdLineMarkerTest
{
    private static readonly Permission Marker = Perm(40, ThirdLineAssurance.PermissionKey);

    private static List<Permission> All() =>
        [Perm(1, "riskmanagement"), Perm(2, "submit_risks"), Marker, Perm(3, "third_party_manage")];

    [Fact]
    public void TestTheBulkSelectableListNeverOffersTheMarker()
    {
        var listed = Track9ThirdLineMarker.Selectable(All());

        Assert.Equal(new[] { "riskmanagement", "submit_risks", "third_party_manage" }, listed.Select(p => p.Key));
        Assert.Same(Marker, Track9ThirdLineMarker.Find(All()));
    }

    /// <summary>
    /// Regression: the editors listed every permission, so selecting the whole list — a range, a keyboard select-all,
    /// or every entry moved across the role editor at once — and saving granted the marker too.
    /// </summary>
    [Fact]
    public void TestSelectingEverythingListedAndSavingNeverGrantsTheMarker()
    {
        var everythingListed = Track9ThirdLineMarker.Selectable(All()).Cast<Permission?>();

        var userSave = Track9ThirdLineMarker.Compose(everythingListed, Marker, granted: false);
        var roleSave = Track9ThirdLineMarker.Compose(
            Track9ThirdLineMarker.Selectable(All()).Select(p => p.Key), Marker, granted: false);

        Assert.DoesNotContain(userSave, p => p!.Key == ThirdLineAssurance.PermissionKey);
        Assert.Equal(3, userSave.Count);
        Assert.DoesNotContain(ThirdLineAssurance.PermissionKey, roleSave);
        Assert.Equal(3, roleSave.Count);
    }

    [Fact]
    public void TestTickingTheMarkerOnPurposeGrantsItExactlyOnce()
    {
        var userSave = Track9ThirdLineMarker.Compose([Perm(1, "riskmanagement")], Marker, granted: true);
        var roleSave = Track9ThirdLineMarker.Compose(["riskmanagement"], Marker, granted: true);

        Assert.Equal(new[] { "riskmanagement", ThirdLineAssurance.PermissionKey }, userSave.Select(p => p!.Key));
        Assert.Equal(new[] { "riskmanagement", ThirdLineAssurance.PermissionKey }, roleSave);
    }

    /// <summary>The box is the only way in: a marker that reached the selection some other way is dropped, or kept once.</summary>
    [Fact]
    public void TestAMarkerInTheSelectionDoesNotDecideTheGrant()
    {
        Assert.Single(Track9ThirdLineMarker.Compose([Perm(1, "riskmanagement"), Marker, null], Marker, granted: false));
        Assert.Equal(2, Track9ThirdLineMarker.Compose([Perm(1, "riskmanagement"), Marker], Marker, granted: true).Count);
        Assert.Equal(new[] { "governance" },
            Track9ThirdLineMarker.Compose(["governance", ThirdLineAssurance.PermissionKey], Marker, granted: false));
    }

    [Fact]
    public void TestAnInstallationWithoutTheMarkerSavesWithoutIt()
    {
        var listed = Track9ThirdLineMarker.Selectable([Perm(1, "riskmanagement")]);

        Assert.Null(Track9ThirdLineMarker.Find(listed));
        Assert.Single(Track9ThirdLineMarker.Compose(listed.Cast<Permission?>(), null, granted: true));
        Assert.Equal(new[] { "riskmanagement" }, Track9ThirdLineMarker.Compose(["riskmanagement"], null, granted: true));
    }

    [Fact]
    public void TestLoadingShowsTheBoxTickedOnlyForAHolder()
    {
        Assert.True(Track9ThirdLineMarker.IsGranted(["reports", ThirdLineAssurance.PermissionKey]));
        Assert.False(Track9ThirdLineMarker.IsGranted(["reports", "third_line"]));
        Assert.False(Track9ThirdLineMarker.IsGranted([]));
        Assert.False(Track9ThirdLineMarker.IsGranted(null));
    }

    private static Permission Perm(int id, string key) => new() { Id = id, Key = key, Name = key, Description = key };
}

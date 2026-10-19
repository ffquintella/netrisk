using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T251 (S40 §5.1, §5.6.5, §5.13): the top bar's rules — the breadcrumb, the chips, the states shown before a value
/// (awaiting the first snapshot, no approved profile, suspended publication), history and the date picker's blackout days.
/// </summary>
[TestSubject(typeof(RiskOverviewTopBar))]
public class RiskOverviewTopBarTest
{
    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Dictionary<string, string> NoArgs = new();

    private static RiskIndexProfileSummaryDto Profile(int version, RiskIndexProfileStatus status, DateTime? retiredAt = null,
        string name = "Equilibrado", string? preset = "balanced") =>
        new(version, version, name, preset, status, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), retiredAt, "ana", "bruno",
            false, "ATA");

    private static RiskIndexOverviewDto Overview(RiskIndexNodeRef node, params RiskIndexNodeRef[] path) =>
        new(node, path, new DateOnly(2026, 10, 5), Now, RiskIndexProvenance.Official, Profile(3, RiskIndexProfileStatus.Active),
            RiskIndexMode.Interim, null, [], [], [], new RiskIndexQualitySummaryDto(0, 0, 0, "low", []), 0, [], []);

    [Fact]
    public void TestTheBreadcrumbEndsOnTheNodeAsTextAndNamesTheRootInTheInterfaceLanguage()
    {
        var org = new RiskIndexNodeRef("org", "Organization", "organization");
        var unit = new RiskIndexNodeRef("e:3", "Unidade X", "organizationUnit");
        var lab = new RiskIndexNodeRef("e:9", "Lab L1", "subOrganizationUnit");

        var segments = RiskOverviewTopBar.Breadcrumb(Overview(lab, org, unit), "Organização", "Meu escopo");

        Assert.Equal(["Organização", "Unidade X", "Lab L1"], segments.Select(s => s.Label));
        Assert.Equal([false, false, true], segments.Select(s => s.IsCurrent));
        Assert.Equal(["org", "e:3", "e:9"], segments.Select(s => s.Key));
    }

    [Fact]
    public void TestTheOrganizationIsNotRepeatedWhenItIsTheNode()
    {
        var org = new RiskIndexNodeRef("org", "Organization", "organization");

        var segments = RiskOverviewTopBar.Breadcrumb(Overview(org, org), "Organização", "Meu escopo");

        Assert.Equal("Organização", Assert.Single(segments).Label);
        Assert.True(segments[0].IsCurrent);
        Assert.Equal("Meu escopo", Assert.Single(RiskOverviewTopBar.Breadcrumb(
            Overview(new RiskIndexNodeRef("scope", "My scope", "scope")), "Organização", "Meu escopo")).Label);
    }

    [Fact]
    public void TestASeveralEntityScopeIsHeadedByMyScope()
    {
        var unit = new RiskIndexNodeRef("e:3", "Unidade X", "organizationUnit");
        var lab = new RiskIndexNodeRef("e:9", "Lab L1", "subOrganizationUnit");

        var segments = RiskOverviewTopBar.Breadcrumb(Overview(lab, unit), "Organização", "Meu escopo", myScopeRoot: true);
        Assert.Equal(["scope", "e:3", "e:9"], segments.Select(s => s.Key));
        Assert.Equal("Meu escopo", segments[0].Label);

        // A single-entity scope is rooted at the entity itself, and "Meu escopo" never heads itself.
        Assert.Equal(["e:3", "e:9"], RiskOverviewTopBar.Breadcrumb(Overview(lab, unit), "Organização", "Meu escopo")
            .Select(s => s.Key));
        Assert.Single(RiskOverviewTopBar.Breadcrumb(Overview(new RiskIndexNodeRef("scope", "My scope", "scope")),
            "Organização", "Meu escopo", myScopeRoot: true));
    }

    [Fact]
    public void TestWithoutASnapshotAnApprovedProfileMeansWaitingForTheFirstOne()
    {
        Assert.Equal(RiskOverviewEmptyState.AwaitingFirstSnapshot,
            RiskOverviewTopBar.WithoutSnapshot([Profile(1, RiskIndexProfileStatus.Active)], Now));
        Assert.Equal(RiskOverviewEmptyState.NoApprovedProfile,
            RiskOverviewTopBar.WithoutSnapshot([Profile(1, RiskIndexProfileStatus.Draft),
                Profile(2, RiskIndexProfileStatus.PendingApproval)], Now));
        Assert.Equal(RiskOverviewEmptyState.NoApprovedProfile, RiskOverviewTopBar.WithoutSnapshot([], Now));
        Assert.Equal(RiskOverviewEmptyState.NoApprovedProfile,
            RiskOverviewTopBar.WithoutSnapshot([Profile(1, RiskIndexProfileStatus.Active, Now.AddDays(-1))], Now));
    }

    [Fact]
    public void TestARetiredProfileWithNoSuccessorSuspendsThePublication()
    {
        var retired = Profile(4, RiskIndexProfileStatus.Retired, new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));

        var suspended = RiskOverviewTopBar.Suspended(retired, [retired, Profile(3, RiskIndexProfileStatus.Retired)], Now);

        Assert.Equal(4, suspended!.Version);
        Assert.Equal(new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc), suspended.RetiredAtUtc);
    }

    [Fact]
    public void TestAHistoricalSnapshotOfARetiredProfileWithASuccessorIsNotASuspension()
    {
        var retired = Profile(3, RiskIndexProfileStatus.Retired, new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc));

        Assert.Null(RiskOverviewTopBar.Suspended(retired, [retired, Profile(4, RiskIndexProfileStatus.Active)], Now));
        Assert.Null(RiskOverviewTopBar.Suspended(Profile(4, RiskIndexProfileStatus.Active),
            [Profile(4, RiskIndexProfileStatus.Active)], Now));
    }

    [Fact]
    public void TestTheBlackoutDaysAreTheGapsBetweenSnapshots()
    {
        var ranges = RiskOverviewTopBar.BlackoutRanges(
        [
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7),
            new DateOnly(2026, 10, 5)
        ]);

        Assert.Equal([(new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4)), (new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 6))],
            ranges);
        Assert.Empty(RiskOverviewTopBar.BlackoutRanges([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)]));
        Assert.Empty(RiskOverviewTopBar.BlackoutRanges([]));
    }

    [Fact]
    public void TestAnEarlierDateIsHistory()
    {
        Assert.True(RiskOverviewTopBar.IsHistory(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)));
        Assert.False(RiskOverviewTopBar.IsHistory(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)));
        Assert.False(RiskOverviewTopBar.IsHistory(new DateOnly(2026, 10, 5), null));
    }

    [Fact]
    public void TestTheChipsNameProvenanceModeAndProfile()
    {
        Assert.Equal("RiskProvenance_PartialRestricted", RiskOverviewTopBar.ProvenanceKey(RiskIndexProvenance.PartialRestricted));
        Assert.True(RiskOverviewTopBar.ProvenanceIsWarning(RiskIndexProvenance.PartialRestricted));
        Assert.True(RiskOverviewTopBar.ProvenanceIsWarning(RiskIndexProvenance.UnofficialPreview));
        Assert.False(RiskOverviewTopBar.ProvenanceIsWarning(RiskIndexProvenance.Official));
        Assert.False(RiskOverviewTopBar.ProvenanceIsWarning(RiskIndexProvenance.OfficialDerived));
        Assert.Equal(("RiskMode_Interim", "RiskMode_InterimHint"),
            (RiskOverviewTopBar.ModeKey(RiskIndexMode.Interim), RiskOverviewTopBar.ModeHintKey(RiskIndexMode.Interim)));

        var pt = CultureInfo.GetCultureInfo("pt-BR");
        Assert.Equal("Perfil v3 · Equilibrado",
            RiskOverviewTopBar.ProfileChip(Profile(3, RiskIndexProfileStatus.Active), RiskIndexTestText.Portuguese, pt));
        Assert.Equal("Perfil v3 · balanced",
            RiskOverviewTopBar.ProfileChip(Profile(3, RiskIndexProfileStatus.Active, name: " "), RiskIndexTestText.Portuguese, pt));
    }

    [Fact]
    public void TestThePilotChipShowsOnlyDuringTheShadowPilot()
    {
        Assert.True(RiskOverviewTopBar.ShowsPilotChip(RiskIndexPublicationState.Shadow));
        Assert.False(RiskOverviewTopBar.ShowsPilotChip(RiskIndexPublicationState.Published));
        Assert.False(RiskOverviewTopBar.ShowsPilotChip(null));
    }

    [Fact]
    public void TestThePartialHintNamesTheEavCauseOnlyWhenTheServerMarksIt()
    {
        Assert.Equal("RiskState_PartialCauseEav",
            RiskOverviewTopBar.PartialCauseKey([new RiskIndexChipDto("closure_through_eav", NoArgs, null, null)]));
        Assert.Equal("RiskState_PartialCauseEither",
            RiskOverviewTopBar.PartialCauseKey([new RiskIndexChipDto("partial_visibility", NoArgs, null, null)]));
    }
}

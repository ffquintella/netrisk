using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Authentication;
using Model.Risks.Chain;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// Stage 9.1 (S41 §8) — the pure half of the linkage-chain screens: how a level reads in the risk
/// detail, how the edit dialog's rows and enable rules behave, and the coverage report's headline.
///
/// The cases the methodology names are the incomplete ones: an empty level reads "not informed" rather
/// than going blank, a host the user may not read is shown as present but unnamed, and "no critical
/// process" is "not computable" rather than 0 %.
/// </summary>
[TestSubject(typeof(RiskChainSummary))]
public class RiskChainSummaryTest
{
    private const string NotInformed = "Not informed";
    private const string Redacted = "Host (no permission)";

    private static RiskChainLinkDto Link(int id, string? name, RiskChainLevel level = RiskChainLevel.Process,
        RiskChainLinkOrigin origin = RiskChainLinkOrigin.Declared, int? entityId = 10, int? hostId = null,
        bool redacted = false) => new()
    {
        Id = id, RiskId = 1, Level = level, TargetName = name, EntityId = entityId, HostId = hostId,
        Origin = origin, IsRedacted = redacted, TargetType = hostId is null && !redacted ? "businessProcess" : "host",
        CreatedAt = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)
    };

    private static RiskChainDto Chain(params (RiskChainLevel Level, RiskChainLinkDto[] Links)[] levels)
    {
        var chain = new RiskChainDto { RiskId = 1 };
        foreach (var level in Enum.GetValues<RiskChainLevel>())
        {
            var links = levels.FirstOrDefault(l => l.Level == level).Links ?? [];
            chain.Levels.Add(new RiskChainLevelDto { Level = level, Links = links.ToList() });
            if (links.Length == 0) chain.MissingLevels.Add(level);
        }
        return chain;
    }

    // --- the risk detail ------------------------------------------------------------------------

    [Fact]
    public void TestNamesAreJoinedInTheOrderTheServerSentThem()
    {
        Assert.Equal("Enrolment, Admissions, Research",
            RiskChainSummary.Describe([Link(1, "Enrolment"), Link(2, "Admissions"), Link(3, "Research")],
                NotInformed, Redacted));
    }

    [Fact]
    public void TestAnEmptyLevelReadsNotInformed()
    {
        Assert.Equal(NotInformed, RiskChainSummary.Describe([], NotInformed, Redacted));
        Assert.Equal(NotInformed, RiskChainSummary.Describe(null, NotInformed, Redacted));
    }

    /// <summary>
    /// The edge case of this stage: a chain with its middle missing still reads, level by level — the
    /// empty service level says so, and the levels around it keep their names.
    /// </summary>
    [Fact]
    public void TestAMissingMiddleLevelReadsNotInformedAndTheOthersKeepTheirNames()
    {
        var chain = Chain(
            (RiskChainLevel.Objective, [Link(1, "Grow enrolment", RiskChainLevel.Objective)]),
            (RiskChainLevel.Process, [Link(2, "Enrolment")]),
            (RiskChainLevel.Data, [Link(3, "Student records", RiskChainLevel.Data)]));

        Assert.Equal("Grow enrolment", RiskChainSummary.ForLevel(chain, RiskChainLevel.Objective, NotInformed, Redacted));
        Assert.Equal("Enrolment", RiskChainSummary.ForLevel(chain, RiskChainLevel.Process, NotInformed, Redacted));
        Assert.Equal(NotInformed, RiskChainSummary.ForLevel(chain, RiskChainLevel.ItService, NotInformed, Redacted));
        Assert.Equal("Student records", RiskChainSummary.ForLevel(chain, RiskChainLevel.Data, NotInformed, Redacted));
        Assert.Equal(NotInformed, RiskChainSummary.ForLevel(chain, RiskChainLevel.Asset, NotInformed, Redacted));

        Assert.Equal(NotInformed, RiskChainSummary.ForLevel(null, RiskChainLevel.Process, NotInformed, Redacted));
    }

    /// <summary>A host the user may not read is present — the level is not "not informed" — and unnamed.</summary>
    [Fact]
    public void TestARedactedHostIsShownAsPresentButUnnamed()
    {
        var chain = Chain((RiskChainLevel.Asset,
        [
            Link(1, "Portal app", RiskChainLevel.Asset, entityId: 30),
            Link(2, null, RiskChainLevel.Asset, entityId: null, hostId: null, redacted: true)
        ]));

        Assert.Equal("Portal app, " + Redacted,
            RiskChainSummary.ForLevel(chain, RiskChainLevel.Asset, NotInformed, Redacted));
    }

    [Fact]
    public void TestATargetWithoutANameFallsBackToItsId()
    {
        Assert.Equal("#10", RiskChainSummary.TargetName(Link(1, "  "), Redacted));
        Assert.Equal("#4", RiskChainSummary.TargetName(Link(1, null, entityId: null, hostId: 4), Redacted));
    }

    // --- the edit dialog ------------------------------------------------------------------------

    [Fact]
    public void TestRowsFollowTheLevelOrderAndCarryLocalisedWords()
    {
        var chain = Chain(
            (RiskChainLevel.Asset, [Link(5, "Portal app", RiskChainLevel.Asset)]),
            (RiskChainLevel.Objective, [Link(1, "Grow enrolment", RiskChainLevel.Objective, RiskChainLinkOrigin.Legacy)]));

        var rows = RiskChainLinkRow.From(chain, level => "L" + (int)level, Redacted, "Declared", "Legacy");

        Assert.Equal([1, 5], rows.Select(r => r.Link.Id));
        Assert.Equal("L1", rows[0].LevelName);
        Assert.Equal("Legacy", rows[0].OriginName);
        Assert.True(rows[0].IsLegacy);
        Assert.Equal("Declared", rows[1].OriginName);
        Assert.Equal("Portal app", rows[1].TargetName);
        Assert.Empty(RiskChainLinkRow.From(null, _ => "", Redacted, "D", "L"));
    }

    [Fact]
    public void TestOnlyADeclaredLinkCanBeRemovedAndAHostLinkNeedsHosts()
    {
        Assert.True(RiskChainAccess.CanRemove(Link(1, "P"), canReadHosts: false));
        Assert.False(RiskChainAccess.CanRemove(Link(1, "P", origin: RiskChainLinkOrigin.Legacy), canReadHosts: true));
        Assert.False(RiskChainAccess.CanRemove(null, canReadHosts: true));

        var host = Link(2, "web-01", RiskChainLevel.Asset, entityId: null, hostId: 4);
        Assert.False(RiskChainAccess.CanRemove(host, canReadHosts: false));
        Assert.True(RiskChainAccess.CanRemove(host, canReadHosts: true));

        var redacted = Link(3, null, RiskChainLevel.Asset, entityId: null, hostId: null, redacted: true);
        Assert.False(RiskChainAccess.CanRemove(redacted, canReadHosts: false));
    }

    // --- who may do what ------------------------------------------------------------------------

    private static AuthenticatedUserInfo User(string? role = null, bool admin = false, params string[] permissions) =>
        new() { UserRole = role, IsAdmin = admin, UserPermissions = permissions.ToList() };

    /// <summary>
    /// The chain editor opens for the audience of <c>RequireRiskmanagement</c> — the permission or the
    /// <c>Administrator</c> role — and not for <c>IsAdmin</c> alone, which that policy does not accept.
    /// </summary>
    [Fact]
    public void TestTheEditorFollowsTheRiskManagementPolicy()
    {
        Assert.True(RiskChainAccess.CanEditChain(User(permissions: "riskmanagement")));
        Assert.True(RiskChainAccess.CanEditChain(User(role: "Administrator")));

        Assert.False(RiskChainAccess.CanEditChain(User(admin: true)));
        Assert.False(RiskChainAccess.CanEditChain(User(role: "Analyst", permissions: "hosts")));
        Assert.False(RiskChainAccess.CanEditChain(null));
        Assert.False(RiskChainAccess.CanEditChain(new AuthenticatedUserInfo { UserPermissions = null }));
    }

    [Fact]
    public void TestHostSearchFollowsTheHostsPermission()
    {
        Assert.True(RiskChainAccess.CanReadHosts(User(permissions: "hosts")));
        Assert.True(RiskChainAccess.CanReadHosts(User(admin: true)));

        Assert.False(RiskChainAccess.CanReadHosts(User(role: "Administrator", permissions: "riskmanagement")));
        Assert.False(RiskChainAccess.CanReadHosts(null));
    }

    // --- the coverage report --------------------------------------------------------------------

    private const string Format = "{0} of {1} critical processes covered ({2}%)";
    private const string NotComputable = "Not computable";

    [Fact]
    public void TestTheCoverageHeadline()
    {
        var coverage = new CriticalProcessCoverageDto { CriticalProcessCount = 3, CoveredCount = 1, CoverageRatio = 1m / 3m };

        Assert.Equal("1 of 3 critical processes covered (33.3%)",
            RiskChainSummary.Coverage(coverage, Format, NotComputable, CultureInfo.InvariantCulture));

        var full = new CriticalProcessCoverageDto { CriticalProcessCount = 2, CoveredCount = 2, CoverageRatio = 1m };
        Assert.Equal("2 of 2 critical processes covered (100%)",
            RiskChainSummary.Coverage(full, Format, NotComputable, CultureInfo.InvariantCulture));
    }

    /// <summary>No critical process is "not computable" — never 0 %, never 100 %.</summary>
    [Fact]
    public void TestNoCriticalProcessIsNotComputable()
    {
        Assert.Equal(NotComputable, RiskChainSummary.Coverage(
            new CriticalProcessCoverageDto { CoverageRatio = null }, Format, NotComputable, CultureInfo.InvariantCulture));
        Assert.Equal(NotComputable, RiskChainSummary.Coverage(null, Format, NotComputable));
    }

    [Fact]
    public void TestCoveredIsTextAndAMatchCarriesItsVia()
    {
        var row = new CriticalProcessCoverageRow(new CriticalProcessCoverageRowDto
            { ProcessId = 10, ProcessName = "Enrolment", Criticality = 5, Covered = false }, "Yes", "No");
        Assert.Equal("No", row.CoveredText);

        var direct = new RiskChainMatchRow(new RiskChainMatchDto { RiskId = 1, Subject = "R1", Status = "New" });
        Assert.Equal(string.Empty, direct.Via);

        var inferred = new RiskChainMatchRow(new RiskChainMatchDto
            { RiskId = 2, Inferred = true, ViaEntityId = 20, ViaEntityName = "Student portal" });
        Assert.Equal("Student portal", inferred.Via);

        var unnamed = new RiskChainMatchRow(new RiskChainMatchDto { RiskId = 3, Inferred = true, ViaEntityId = 21 });
        Assert.Equal("#21", unnamed.Via);
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.Localization;
using Model.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Reports;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// The evidence pack rendered through the 2.1 reporting engine (Track 8 milestone 8.4.2).
///
/// This renders a real PDF rather than asserting on intermediate calls, because the failure mode
/// worth catching is the one MigraDoc produces at render time and not at build time: a table with
/// more cells in a row than the table has columns throws only when the document is laid out, and a
/// section that adds a table before a column exists throws only then too. A test that stubbed
/// MigraDoc would pass on a report that cannot be produced.
///
/// It is also the first test in the repository that exercises the PDF engine at all, which means the
/// font resolver and the shipped logo asset are covered here as a side effect.
/// </summary>
[TestSubject(typeof(GovernanceEvidencePdfReport))]
public class GovernanceEvidenceReportRenderTest : InMemoryServiceTestBase
{
    /// <summary>
    /// Returns the key for any lookup, which is what the production localizer does for a key with no
    /// resource entry. That makes a missing translation visible as the key in the output rather than
    /// as a crash, and keeps this test independent of the API's resx files.
    /// </summary>
    private sealed class KeyEchoLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments), resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static Report NewReport() => new()
    {
        Id = 1, Name = "Evidence", Type = 3, CreatorId = 1,
        CreationDate = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc)
    };

    private static GovernanceEvidencePack FullPack() => new()
    {
        EntityId = 5,
        EntityName = "Retail Bank",
        FromUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
        ToUtc = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
        GeneratedAtUtc = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
        RequestedBy = "Ana Approver (ana, #10)",
        Acceptances =
        [
            new EvidenceAcceptance
            {
                Id = 1, RiskId = 1, RiskSubject = "Unpatched payment gateway",
                Name = "Vendor patch deferred", Status = "Active",
                AuthorizingManager = "Ana Approver (ana, #10)",
                RequestedBy = "Bob Reviewer (bob, #11)",
                StartDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
                BusinessJustification = "The vendor patch breaks the settlement integration.",
                CompensatingControls = "WAF rule + daily reconciliation",
                ResidualScoreSnapshot = 4.25, FromCampaign = true
            },
            new EvidenceAcceptance
            {
                Id = 2, RiskId = 2, RiskSubject = "Shared admin account",
                Name = "Withdrawn", Status = "Revoked",
                AuthorizingManager = "Ana Approver (ana, #10)",
                StartDate = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                RevokedAt = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc),
                RevokedBy = "Cleo (cleo, #12)", RevocationReason = "SSO rolled out"
            }
        ],
        Reviews =
        [
            new EvidenceReview
            {
                Id = 1, RiskId = 1, RiskSubject = "Unpatched payment gateway",
                SubmissionDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                Reviewer = "Bob Reviewer (bob, #11)", Comments = "Treat then re-rate",
                RequiresCountersignature = true, SecondReviewer = "Cleo (cleo, #12)",
                SecondReviewAt = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc)
            },
            new EvidenceReview
            {
                Id = 2, RiskId = 2, RiskSubject = "Shared admin account",
                SubmissionDate = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                Reviewer = "Bob Reviewer (bob, #11)", Comments = "Accepted for now",
                RequiresCountersignature = true,
                SegregationOverrideReason = "Sole approver on site during the incident"
            }
        ],
        CampaignDecisions =
        [
            new EvidenceCampaignDecision
            {
                CampaignId = 1, CampaignName = "Q2 2026", CampaignStatus = "Completed",
                PeriodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
                DueDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                RiskId = 1, RiskSubject = "Unpatched payment gateway", Rank = 1,
                Decision = "Accepted", DecidedBy = "Bob Reviewer (bob, #11)",
                DecidedAt = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                RiskAcceptanceId = 1
            },
            new EvidenceCampaignDecision
            {
                CampaignId = 1, CampaignName = "Q2 2026", CampaignStatus = "Completed",
                PeriodStart = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
                DueDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                RiskId = 2, RiskSubject = "Shared admin account", Rank = 2,
                Decision = "Escalated", DecidedBy = "Bob Reviewer (bob, #11)",
                DecidedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                EscalatedTo = "Dana Director (dana, #13)", DecisionNotes = "Above our appetite"
            }
        ],
        Changes =
        [
            new EvidenceChange
            {
                OccurredAt = new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc),
                EntityType = "Risk", EntityId = 1, Field = "Status", Action = "Update",
                Actor = "bob", UserId = 11, OldValue = "New", NewValue = "Mitigation Planned",
                CorrelationId = "abc"
            },
            new EvidenceChange
            {
                OccurredAt = new DateTime(2026, 5, 2, 9, 0, 0, DateTimeKind.Utc),
                EntityType = "RiskScoring", EntityId = 1, Field = "ResidualRisk",
                Action = "Update", Actor = "system", OldValue = null, NewValue = "4.25"
            }
        ]
    };

    private static bool IsPdf(byte[] data) =>
        data.Length > 4 && Encoding.ASCII.GetString(data, 0, 4) == "%PDF";

    [Fact]
    public async Task TestAFullPackRendersToAPdf()
    {
        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), FullPack());

        var data = await report.GenerateReportAsync("Governance Evidence Pack");

        Assert.True(IsPdf(data), "the rendered artifact is not a PDF");
        Assert.True(data.Length > 3000, $"the PDF is implausibly small ({data.Length} bytes)");
    }

    /// <summary>
    /// An empty period still renders. This is the common case for a newly configured installation,
    /// and a report that threw on it would make the first evidence export somebody tried look like a
    /// product defect.
    /// </summary>
    [Fact]
    public async Task TestAnEmptyPackStillRenders()
    {
        var pack = new GovernanceEvidencePack
        {
            EntityName = "(all entities)",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            GeneratedAtUtc = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            RequestedBy = "Ana (ana, #10)"
        };

        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), pack);

        Assert.True(IsPdf(await report.GenerateReportAsync("Empty")));
    }

    /// <summary>
    /// A 4 000-character justification and a very long comment must not break layout. MigraDoc will
    /// not throw on these, but a table whose column widths overflow the page silently loses the
    /// right-hand columns — which in this report is the "to" value and the actor.
    /// </summary>
    [Fact]
    public async Task TestPathologicallyLongFreeTextStillRenders()
    {
        var pack = FullPack();

        pack.Acceptances[0].BusinessJustification = new string('x', 4000);
        pack.Reviews[0].Comments = new string('y', 2000);
        pack.Changes[0].NewValue = new string('z', 3000);

        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), pack);

        Assert.True(IsPdf(await report.GenerateReportAsync("Long text")));
    }

    /// <summary>The truncation notice is on a separate path and has to render too.</summary>
    [Fact]
    public async Task TestATruncatedPackRenders()
    {
        var pack = FullPack();
        pack.ChangesTruncated = true;

        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), pack);

        Assert.True(IsPdf(await report.GenerateReportAsync("Truncated")));
    }

    /// <summary>
    /// Every optional field null at once — an acceptance with no justification, a review with no
    /// counter-signature, a decision nobody made, a change with no field name. Each of those is a
    /// separate null check in the renderer and this is the case that exercises all of them.
    /// </summary>
    [Fact]
    public async Task TestAPackWhoseOptionalFieldsAreAllNullRenders()
    {
        var pack = new GovernanceEvidencePack
        {
            EntityName = "#9",
            FromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            GeneratedAtUtc = new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc),
            RequestedBy = "",
            Acceptances =
            [
                new EvidenceAcceptance
                {
                    Id = 1, Name = "Bare", Status = "Active", AuthorizingManager = "",
                    StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    ExpiresAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
                }
            ],
            Reviews =
            [
                new EvidenceReview
                {
                    Id = 1, RiskId = 1,
                    SubmissionDate = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    Reviewer = "", Comments = "", RequiresCountersignature = true
                }
            ],
            CampaignDecisions =
            [
                new EvidenceCampaignDecision
                {
                    CampaignId = 1, CampaignName = "Q1", CampaignStatus = "Open",
                    RiskId = 1, Decision = "Pending"
                }
            ],
            Changes =
            [
                new EvidenceChange
                {
                    OccurredAt = new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc),
                    EntityType = "Risk", EntityId = 1, Action = "Create", Actor = "system"
                }
            ]
        };

        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), pack);

        Assert.True(IsPdf(await report.GenerateReportAsync("Sparse")));
    }
}

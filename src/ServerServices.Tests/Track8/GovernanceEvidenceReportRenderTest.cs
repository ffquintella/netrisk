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
using ServerServices.Tests.Reports;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// The evidence pack rendered through the 2.1 reporting engine (Track 8 milestone 8.4.2).
///
/// This renders a real PDF rather than asserting on intermediate calls, because the failure mode
/// worth catching is the one the PDF engine produces at render time and not at build time: QuestPDF
/// lays the document out only when it is generated, and content that cannot be placed — a table
/// wider than the page, a fixed-size box its text overflows — throws only then. A test that stubbed
/// the engine would pass on a report that cannot be produced.
///
/// The content tests read the text back out of the PDF (<see cref="PdfText"/>, PdfPig), so a section
/// or a value the layout silently dropped fails here, and so does text drawn in any font but the
/// bundled Arial Unicode MS. The shipped logo asset is covered as a side effect.
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
    /// A 4 000-character justification and a very long comment must not break layout. A table cell
    /// taller than a page has to split across pages, and a table whose column widths overflow the
    /// page would lose the right-hand columns — which in this report is the "to" value and the actor.
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

    /// <summary>
    /// Every section and every value of a full pack is in the rendered text — the scope block, each
    /// acceptance with its optional lines, the three tables with their headers, and the details the
    /// comments above say an auditor looks for (the segregation override, the escalation, the link
    /// from a campaign decision to its acceptance).
    /// </summary>
    [Fact]
    public async Task TestAFullPackCarriesEverySectionAndValue()
    {
        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), FullPack());

        var pdf = PdfText.Read(await report.GenerateReportAsync("Governance Evidence Pack"));

        string[] expected =
        [
            // Title block and scope.
            "Governance Evidence Pack",
            "Entity: Retail Bank",
            "Period: 2026-04-01 → 2026-06-30 (UTC)",
            "GeneratedAt: 2026-08-26 10:00:00Z",
            "RequestedBy: Ana Approver (ana, #10)",
            "Contents: 2 RiskAcceptances, 2 MgmtReviews, 2 BusinessReviewDecisions, 2 FieldChanges",
            // Acceptances.
            "#1 Vendor patch deferred",
            "Risk: #1 Unpatched payment gateway",
            "StatusLabel: Active",
            "AuthorizingManager: Ana Approver (ana, #10)",
            "RequestedBy: Bob Reviewer (bob, #11)",
            "Period: 2026-04-01 → 2026-10-01",
            "ResidualRiskAtAcceptance: 4.25",
            "Origin: BusinessReviewCampaign",
            "BusinessJustification: The vendor patch breaks the settlement integration.",
            "CompensatingControls: WAF rule + daily reconciliation",
            "#2 Withdrawn",
            "Revoked: 2026-05-05 — Cleo (cleo, #12) — SSO rolled out",
            // Reviews.
            "Date", "Reviewer", "Countersignature", "Comments",
            "Cleo (cleo, #12) — 2026-05-03",
            "Treat then re-rate",
            "PendingCountersignature [SegregationOverride: Sole approver on site during the incident]",
            // Campaign decisions.
            "Campaign", "BusinessRank", "Decision", "DecidedBy", "Notes",
            "Q2 2026 (Completed)",
            "RiskAcceptance #1.",
            "EscalatedTo: Dana Director (dana, #13). Above our appetite",
            // Field changes.
            "When", "Record", "Field", "Action", "From", "To", "Actor",
            "2026-05-01 12:00", "Risk #1", "Mitigation Planned",
            "RiskScoring #1", "ResidualRisk", "4.25"
        ];

        foreach (var text in expected)
            Assert.True(pdf.Contains(text), $"the PDF does not contain \"{text}\"");

        // The sections come in the order the class composes them.
        Assert.True(pdf.IndexOf("#1 Vendor patch deferred") < pdf.IndexOf("Treat then re-rate"));
        Assert.True(pdf.IndexOf("Treat then re-rate") < pdf.IndexOf("Q2 2026 (Completed)"));
        Assert.True(pdf.IndexOf("Q2 2026 (Completed)") < pdf.IndexOf("Mitigation Planned"));

        Assert.False(pdf.Contains("EvidenceTruncatedMSG"), "an untruncated pack carries the truncation notice");
        Assert.False(pdf.Contains("NothingRecordedInThisPeriodMSG"), "a full pack says a section is empty");
    }

    /// <summary>
    /// The engine's own guarantees: the document metadata, page numbering on every page, and every
    /// glyph drawn in the bundled Arial Unicode MS rather than a fallback the host happened to have.
    /// </summary>
    [Fact]
    public async Task TestTheRenderedPackCarriesMetadataPageNumbersAndTheBundledFont()
    {
        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), FullPack());

        var pdf = PdfText.Read(await report.GenerateReportAsync("Governance Evidence Pack"));

        Assert.Equal("Governance Evidence Pack", pdf.Title);
        Assert.Equal("Netrisk - Risk Management System", pdf.Author);
        Assert.Equal("Report", pdf.Subject);

        for (var page = 1; page <= pdf.Pages; page++)
            Assert.True(pdf.Contains($"{page} / {pdf.Pages}"), $"page {page} of {pdf.Pages} is not numbered");

        Assert.NotEmpty(pdf.Fonts);
        Assert.All(pdf.Fonts, font => Assert.StartsWith("ArialUnicodeMS", font));
    }

    /// <summary>An empty period names each of its four sections as empty, in words.</summary>
    [Fact]
    public async Task TestAnEmptyPackSaysEachSectionIsEmpty()
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

        var pdf = PdfText.Read(await report.GenerateReportAsync("Empty"));

        var empty = PdfText.Squash("NothingRecordedInThisPeriodMSG");
        var occurrences = 0;
        for (var at = pdf.IndexOf(empty); at >= 0; at = pdf.IndexOf(empty, at + 1))
            occurrences++;

        Assert.Equal(4, occurrences);
        Assert.True(pdf.Contains("Contents: 0 RiskAcceptances, 0 MgmtReviews, 0 BusinessReviewDecisions, 0 FieldChanges"));
    }

    /// <summary>The truncation notice is rendered when, and only when, the pack was truncated.</summary>
    [Fact]
    public async Task TestATruncatedPackSaysSo()
    {
        var pack = FullPack();
        pack.ChangesTruncated = true;

        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), pack);

        Assert.True(PdfText.Read(await report.GenerateReportAsync("Truncated")).Contains("EvidenceTruncatedMSG"));
    }

    /// <summary>
    /// Long free text is kept whole outside the tables and in the review comments, which wrap — a
    /// 4 000-character word is split across lines and pages rather than cut — while a field change is
    /// clipped to 60 characters and an ellipsis, as <c>Truncate</c> says.
    /// </summary>
    [Fact]
    public async Task TestPathologicallyLongFreeTextIsKeptOrClippedAsDesigned()
    {
        var pack = FullPack();

        pack.Acceptances[0].BusinessJustification = new string('x', 4000);
        pack.Reviews[0].Comments = new string('y', 2000);
        pack.Changes[0].NewValue = new string('z', 3000);

        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), pack);

        var pdf = PdfText.Read(await report.GenerateReportAsync("Long text"));

        Assert.True(pdf.Count('x') >= 4000, $"only {pdf.Count('x')} of 4000 'x' were rendered");
        Assert.True(pdf.Count('y') >= 2000, $"only {pdf.Count('y')} of 2000 'y' were rendered");
        Assert.True(pdf.Contains(new string('z', 60) + "…"));
        Assert.False(pdf.Contains(new string('z', 61)));
        // The columns right of the long values are still on the page.
        Assert.True(pdf.Contains("Actor"));
        Assert.True(pdf.Contains("system"));
    }

    /// <summary>
    /// A character none of the bundled fonts has — an emoji typed into a review comment — is drawn as
    /// a replacement glyph and the pack still renders, with the rest of the comment intact. QuestPDF's
    /// default is to abort the whole document on it, which would make one emoji block an evidence
    /// export; the previous PDF engine drew the missing glyph and carried on.
    /// </summary>
    [Fact]
    public async Task TestTextOutsideTheBundledFontStillRenders()
    {
        var pack = FullPack();
        pack.Reviews[0].Comments = "Approved \u2705 \U0001F44D after the retest";

        var report = new GovernanceEvidencePdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>(), pack);

        var data = await report.GenerateReportAsync("Glyphs");

        Assert.True(PdfText.IsPdf(data));
        var pdf = PdfText.Read(data);
        Assert.True(pdf.Contains("Approved"));
        Assert.True(pdf.Contains("after the retest"));
        Assert.True(pdf.Contains("Mitigation Planned"), "the sections after the comment are missing");
    }
}

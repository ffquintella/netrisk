using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.2 (S42 §8) — the standalone hypothesis (T152): a pending risk registered directly rather
/// than raised by an assessment answer, triaged in the same queue, and promoted with its origin and
/// author preserved — the methodology's edge case for this record type.
/// </summary>
[TestSubject(typeof(RisksService))]
public class HypothesisInMemoryTest : InMemoryServiceTestBase
{
    private const int Author = 7;
    private const int Triager = 8;
    private const int Owner = 9;

    private IRisksService Risks => GetService<IRisksService>();

    public HypothesisInMemoryTest()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(Author, "author"));
            ctx.Users.Add(NewUser(Triager, "triager"));
            ctx.Users.Add(NewUser(Owner, "owner"));
            ctx.Categories.Add(new Category { Value = 1, Name = "Operational" });
            ctx.Sources.Add(new Source { Value = 1, Name = "Assessment" });
        });
    }

    private static User NewUser(int id, string name) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@x.test"
    };

    private Task<PendingRiskListing> Register(string subject = "Lab instruments reachable from the campus Wi-Fi") =>
        Risks.CreateHypothesisAsync(new HypothesisRequest
        {
            Subject = subject,
            Description = "Noticed during a walkthrough; nobody has tested it.",
            OwnerId = Owner,
            AffectedAssets = "lab-net"
        }, Author);

    /// <summary>
    /// H1 — a standalone hypothesis is a pending row with no assessment, origin Standalone and its
    /// author recorded, and it joins the same untriaged queue as the assessment-raised rows.
    /// </summary>
    [Fact]
    public async Task TestH1_AStandaloneHypothesisJoinsTheTriageQueue()
    {
        var created = await Register();

        Assert.Equal(PendingRiskOrigin.Standalone, created.Origin);
        Assert.Null(created.AssessmentId);
        Assert.Null(created.AssessmentAnswerId);
        Assert.Equal(Author, created.SubmittedById);
        Assert.Equal(PendingRiskStatus.Pending, created.Status);
        Assert.Equal(0f, created.Score);

        var queued = Assert.Single(await Risks.GetPendingRisksAsync());
        Assert.Equal(created.Id, queued.Id);
        Assert.Equal("Lab instruments reachable from the campus Wi-Fi", queued.Subject);
        Assert.Equal("Noticed during a walkthrough; nobody has tested it.", queued.Comment);
        Assert.Equal(Owner, queued.OwnerId);
        Assert.Equal(PendingRiskOrigin.Standalone, queued.Origin);
    }

    /// <summary>H2 — without a subject there is nothing to triage, and nothing is written.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task TestH2_AHypothesisWithoutASubjectIsRefused(string? subject)
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = subject }, Author));

        Assert.Equal(nameof(HypothesisRequest.Subject), ex.ParameterName);
        await using var db = OpenContext();
        Assert.Empty(db.PendingRisks);
    }

    /// <summary>H3 — an owner who does not exist is not found, and nothing is written.</summary>
    [Fact]
    public async Task TestH3_AHypothesisWithAnUnknownOwnerIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = "Something", OwnerId = 404 }, Author));

        await using var db = OpenContext();
        Assert.Empty(db.PendingRisks);
    }

    /// <summary>
    /// H4 — the edge case the methodology names: promotion preserves the origin and the author. The
    /// risk's reference says it came from a hypothesis, its submitter is the hypothesis's author and
    /// not the triager, the triager stays on the pending row, and the link back is kept.
    /// </summary>
    [Fact]
    public async Task TestH4_PromotingAStandaloneHypothesisPreservesOriginAndAuthor()
    {
        var created = await Register();

        var risk = await Risks.PromotePendingRiskAsync(created.Id, new PendingRiskPromotion
        {
            CategoryId = 1, SourceId = 1
        }, actingUserId: Triager);

        Assert.Equal($"HYP-{created.Id}", risk.ReferenceId);
        Assert.Equal(Author, risk.SubmittedBy);
        Assert.Equal(Owner, risk.Owner);

        await using var db = OpenContext();
        var pending = db.PendingRisks.Single(p => p.Id == created.Id);
        Assert.Equal(PendingRiskStatus.Promoted, pending.Status);
        Assert.Equal(PendingRiskOrigin.Standalone, pending.Origin);
        Assert.Equal(Author, pending.SubmittedById);
        Assert.Equal(Triager, pending.TriagedById);
        Assert.Equal(risk.Id, pending.PromotedRiskId);
        Assert.NotNull(db.RiskScorings.SingleOrDefault(s => s.Id == risk.Id));
    }

    /// <summary>
    /// H5 — promotion does not by itself produce evidence: without an explicit level the risk enters
    /// the register as a Hypothesis (S42 §11, D5).
    /// </summary>
    [Fact]
    public async Task TestH5_APromotedHypothesisDefaultsToHypothesisConfidence()
    {
        var created = await Register();

        var risk = await Risks.PromotePendingRiskAsync(created.Id, new PendingRiskPromotion(), Triager);

        Assert.Equal(EvidenceConfidence.Hypothesis, risk.EvidenceConfidence);
    }

    /// <summary>H6 — the triager can promote with a structured scenario and a stronger confidence.</summary>
    [Fact]
    public async Task TestH6_PromotionCarriesTheScenarioAndAnExplicitConfidence()
    {
        var created = await Register();

        var risk = await Risks.PromotePendingRiskAsync(created.Id, new PendingRiskPromotion
        {
            ScenarioCause = "An attacker on the campus Wi-Fi",
            ScenarioVulnerability = "Flat network between guest Wi-Fi and the lab VLAN",
            ScenarioCentralEvent = "Instrument control software is tampered with",
            ScenarioConsequences = "Corrupted research data",
            EvidenceConfidence = EvidenceConfidence.Indicative
        }, Triager);

        Assert.Equal(EvidenceConfidence.Indicative, risk.EvidenceConfidence);
        Assert.Equal("Instrument control software is tampered with", risk.ScenarioCentralEvent);
        Assert.Equal("Corrupted research data", risk.ScenarioConsequences);
        Assert.Equal("An attacker on the campus Wi-Fi", risk.ScenarioCause);
        Assert.Equal("Flat network between guest Wi-Fi and the lab VLAN", risk.ScenarioVulnerability);
    }

    /// <summary>H7 — an undefined confidence on promotion is refused before anything is written.</summary>
    [Fact]
    public async Task TestH7_AnUndefinedConfidenceOnPromotionIsRefused()
    {
        var created = await Register();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Risks.PromotePendingRiskAsync(created.Id,
                new PendingRiskPromotion { EvidenceConfidence = (EvidenceConfidence)7 }, Triager));

        await using var db = OpenContext();
        Assert.Empty(db.Risks);
        Assert.Equal(PendingRiskStatus.Pending, db.PendingRisks.Single().Status);
    }

    /// <summary>
    /// H8 — the assessment path is unchanged: an assessment row keeps its ASMT reference, has no author,
    /// and its risk is submitted by whoever promoted it, as before Stage 9.2. Its confidence defaults
    /// to Hypothesis like any promotion.
    /// </summary>
    [Fact]
    public async Task TestH8_AnAssessmentRowPromotesAsBefore()
    {
        Seed(ctx => ctx.PendingRisks.Add(new PendingRisk
        {
            Id = 50, AssessmentId = 3, AssessmentAnswerId = 4,
            Subject = Encoding.UTF8.GetBytes("Shared credentials in the deployment script"),
            Score = 6.5f, Comment = "Raised by the quarterly assessment.",
            SubmissionDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = PendingRiskStatus.Pending
        }));

        var listed = Assert.Single(await Risks.GetPendingRisksAsync());
        Assert.Equal(PendingRiskOrigin.Assessment, listed.Origin);
        Assert.Null(listed.SubmittedById);

        var risk = await Risks.PromotePendingRiskAsync(50, new PendingRiskPromotion(), Triager);

        Assert.Equal("ASMT-3-4", risk.ReferenceId);
        Assert.Equal(Triager, risk.SubmittedBy);
        Assert.Equal(EvidenceConfidence.Hypothesis, risk.EvidenceConfidence);
    }

    /// <summary>H9 — a standalone hypothesis is dismissed with a reason exactly like an assessment row.</summary>
    [Fact]
    public async Task TestH9_AStandaloneHypothesisIsDismissedWithAReason()
    {
        var created = await Register();

        await Risks.DismissPendingRiskAsync(created.Id, "Lab VLAN is already isolated.", Triager);

        Assert.Empty(await Risks.GetPendingRisksAsync());
        var dismissed = Assert.Single(await Risks.GetPendingRisksAsync(PendingRiskStatus.Dismissed));
        Assert.Equal("Lab VLAN is already isolated.", dismissed.DismissalReason);
        Assert.Equal(PendingRiskOrigin.Standalone, dismissed.Origin);
    }
}

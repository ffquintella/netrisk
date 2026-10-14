using System;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using ServerServices.Reports;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Reports;

/// <summary>
/// What the Detailed Entities Risks report says about a risk's last review and its mitigation,
/// rendered for real and read back (<see cref="PdfText"/>). The pre/post-treatment table has its own
/// tests in <c>Track8/PrePostTreatmentReportTest</c>.
/// </summary>
[TestSubject(typeof(DetailedEntitiesRisksPdfReport))]
public class DetailedEntitiesRisksPdfReportTest : InMemoryServiceTestBase
{
    private const int EntityId = 811;
    private const int RiskId = 811;

    private static readonly DateTime EarlierReview = new(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterReview = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The report prints dates with the current culture's short pattern.</summary>
    private static string Shown(DateTime date) => date.ToString("d");

    private async Task<PdfText> RenderAsync()
    {
        var report = new DetailedEntitiesRisksPdfReport(
            new Report { Id = 1, Name = "Entities risks", Type = 0, CreatorId = 1 },
            new KeyEchoLocalizer(), GetService<IDalService>());

        return PdfText.Read(await report.GenerateReportAsync("Detailed Entities Risks Report"));
    }

    /// <summary>One entity holding one risk, which is what the report walks.</summary>
    private void SeedRisk(Action<AuditableContext, Risk>? configure = null)
    {
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = 800, Name = "Reporter", Login = "reporter", Email = "r@example.test",
                Enabled = true, Lockout = 0, Type = "local", Salt = "s", Password = new byte[60],
                Admin = true, RoleId = 1
            });
            ctx.Entities.Add(new Entity
            {
                Id = EntityId, DefinitionName = "organization", DefinitionVersion = "1",
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow,
                CreatedBy = 800, UpdatedBy = 800, Status = "active"
            });
            ctx.EntitiesProperties.Add(new EntitiesProperty
            {
                Id = EntityId, Entity = EntityId, Type = "name", Value = "Retail Bank",
                Name = "name", OldValue = ""
            });

            var risk = new Risk
            {
                Id = RiskId, Status = "New", Subject = "Subject of the risk", ReferenceId = "R-811",
                Assessment = string.Empty, Notes = string.Empty,
                RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
                EntityId = EntityId,
                SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            risk.Entities.Add(ctx.Entities.Find(EntityId)!);
            ctx.Risks.Add(risk);
            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = RiskId, ScoringMethod = 1, CalculatedRisk = 5.0f, ClassicLikelihood = 3, ClassicImpact = 3
            });

            configure?.Invoke(ctx, risk);
        });
    }

    private static MgmtReview Review(int id, DateTime submitted) => new()
    {
        Id = id, RiskId = RiskId, Reviewer = 800, SubmissionDate = submitted,
        Review = 1, NextStep = 1, Comments = "c", NextReview = new DateOnly(2027, 1, 1)
    };

    /// <summary>
    /// "Last Review" is the most recent review. The query ordered ascending and took the first row,
    /// so a risk reviewed twice showed the date of its earliest review.
    /// </summary>
    [Fact]
    public async Task TestLastReviewIsTheMostRecentReview()
    {
        // The earlier review is added second, so insertion order cannot explain the result.
        SeedRisk((ctx, _) =>
        {
            ctx.MgmtReviews.Add(Review(1, LaterReview));
            ctx.MgmtReviews.Add(Review(2, EarlierReview));
        });

        var pdf = await RenderAsync();

        Assert.True(pdf.Contains("( Last Review " + Shown(LaterReview) + ") "),
            "the last review is not the most recent one");
        Assert.False(pdf.Contains(Shown(EarlierReview)), "the earlier review date is shown");
    }

    [Fact]
    public async Task TestARiskWithNoReviewShowsNoLastReview()
    {
        SeedRisk();

        var pdf = await RenderAsync();

        Assert.True(pdf.Contains("Subject of the risk"));
        Assert.False(pdf.Contains("Last Review"));
    }

    /// <summary>
    /// The effort label is the one the mitigation's effort id names. It was looked up in the effort
    /// table with the cost id, so a mitigation whose two ids differ showed another row's label.
    /// </summary>
    [Fact]
    public async Task TestMitigationEffortIsResolvedByTheEffortIdNotTheCostId()
    {
        SeedRisk((ctx, risk) =>
        {
            ctx.PlanningStrategies.Add(new PlanningStrategy { Value = 1, Name = "Mitigate" });
            ctx.MitigationCosts.Add(new MitigationCost { Value = 2, Name = "CostTwoLabel" });
            ctx.MitigationCosts.Add(new MitigationCost { Value = 3, Name = "CostThreeLabel" });
            ctx.MitigationEfforts.Add(new MitigationEffort { Value = 2, Name = "EffortTwoLabel" });
            ctx.MitigationEfforts.Add(new MitigationEffort { Value = 3, Name = "EffortThreeLabel" });

            ctx.Mitigations.Add(new Mitigation
            {
                Id = RiskId, RiskId = RiskId, PlanningStrategy = 1, MitigationCost = 2, MitigationEffort = 3,
                MitigationOwner = 800, SubmittedBy = 800, MitigationPercent = 10,
                CurrentSolution = string.Empty, SecurityRequirements = string.Empty,
                SecurityRecommendations = string.Empty,
                SubmissionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                PlanningDate = new DateOnly(2026, 6, 1)
            });
            risk.MitigationId = RiskId;
        });

        var pdf = await RenderAsync();

        Assert.True(pdf.Contains("Mitigation Effort: EffortThreeLabel"), "the effort label is not the effort id's");
        Assert.False(pdf.Contains("Mitigation Effort: EffortTwoLabel"), "the effort was resolved by the cost id");
        Assert.True(pdf.Contains("Mitigation Cost: CostTwoLabel"), "the cost label is not the cost id's");
        Assert.False(pdf.Contains("CostThreeLabel"));
    }
}

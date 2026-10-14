using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.Localization;
using ServerServices.Reports;
using ServerServices.Services;
using ServerServices.Tests.Reports;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// The pre/post-treatment table on the Detailed Entities Risks report (Track 8 milestone 8.2.3).
///
/// The report renders a real PDF, so what is asserted is that it renders at all across the shapes
/// that break table layout: a risk with no residual score, a risk with no mitigation, an entity with
/// no risks, and a reduction that went the wrong way. The PDF engine (QuestPDF) raises those at
/// layout time rather than at build time, so a test that stubbed the renderer would pass on a report
/// that cannot be produced. The content test reads the text back (<see cref="PdfText"/>, PdfPig).
/// </summary>
[TestSubject(typeof(DetailedEntitiesRisksPdfReport))]
public class PrePostTreatmentReportTest : InMemoryServiceTestBase
{
    private sealed class KeyEchoLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments), resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static Report NewReport() => new()
    {
        Id = 1, Name = "Entities risks", Type = 0, CreatorId = 1,
        CreationDate = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Risk NewRisk(int id, int entityId) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        EntityId = entityId,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>
    /// Adds a risk linked to its entity the way the report reads it. The report walks
    /// <c>Entity.Risks</c>, which is the many-to-many risk/entity association, not the
    /// <c>Risk.EntityId</c> column — a risk carrying only the column is invisible to it, and these
    /// tests used to render an empty report without noticing.
    /// </summary>
    private static Risk AddRisk(AuditableContext ctx, int id, int entityId)
    {
        var risk = NewRisk(id, entityId);
        risk.Entities.Add(ctx.Entities.Find(entityId)!);
        ctx.Risks.Add(risk);
        return risk;
    }

    private void SeedEntity(int entityId)
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
                Id = entityId, DefinitionName = "organization", DefinitionVersion = "1",
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow,
                CreatedBy = 800, UpdatedBy = 800, Status = "active"
            });

            ctx.EntitiesProperties.Add(new EntitiesProperty
            {
                Id = entityId, Entity = entityId, Type = "name", Value = "Retail Bank",
                Name = "name", OldValue = ""
            });
        });
    }

    private async Task<byte[]> RenderAsync()
    {
        var report = new DetailedEntitiesRisksPdfReport(NewReport(), new KeyEchoLocalizer(),
            GetService<IDalService>());

        return await report.GenerateReportAsync("Detailed Entities Risks Report");
    }

    private static bool IsPdf(byte[] data) =>
        data.Length > 4 && Encoding.ASCII.GetString(data, 0, 4) == "%PDF";

    /// <summary>
    /// The mixed case that exercises every column: one risk treated, one untreated, one whose
    /// residual is *higher* than its inherent.
    /// </summary>
    [Fact]
    public async Task TestTheReportRendersWithTreatedUntreatedAndWorsenedRisks()
    {
        SeedEntity(801);

        SeedUnscoped(ctx =>
        {
            AddRisk(ctx, 801, 801);
            AddRisk(ctx, 802, 801);
            AddRisk(ctx, 803, 801);

            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 801, ScoringMethod = 1, CalculatedRisk = 8.0f, ResidualRisk = 2.0f,
                ClassicLikelihood = 4, ClassicImpact = 4
            });
            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 802, ScoringMethod = 1, CalculatedRisk = 6.0f,
                ClassicLikelihood = 3, ClassicImpact = 3
            });
            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 803, ScoringMethod = 1, CalculatedRisk = 3.0f, ResidualRisk = 5.0f,
                ClassicLikelihood = 2, ClassicImpact = 2
            });
        });

        Assert.True(IsPdf(await RenderAsync()));
    }

    /// <summary>
    /// An entity whose risks all lack a scoring row. The table has a header and no body rows, which
    /// the engine is happy with but a naive `rows[0]` would not be.
    /// </summary>
    [Fact]
    public async Task TestAnEntityWhoseRisksHaveNoScoringStillRenders()
    {
        SeedEntity(804);

        SeedUnscoped(ctx => AddRisk(ctx, 804, 804));

        Assert.True(IsPdf(await RenderAsync()));
    }

    /// <summary>
    /// A subject long enough to wrap several times inside a 185pt column, which is the realistic
    /// shape of an imported risk title.
    /// </summary>
    [Fact]
    public async Task TestAVeryLongSubjectDoesNotBreakTheTable()
    {
        SeedEntity(805);

        SeedUnscoped(ctx =>
        {
            var risk = AddRisk(ctx, 805, 805);
            risk.Subject = string.Join(" ", Enumerable.Repeat("unpatched", 80));

            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 805, ScoringMethod = 1, CalculatedRisk = 9.0f, ResidualRisk = 1.0f,
                ClassicLikelihood = 5, ClassicImpact = 5
            });
        });

        Assert.True(IsPdf(await RenderAsync()));
    }

    /// <summary>
    /// No entities with risks at all — a fresh installation. The report is empty but must still be a
    /// PDF rather than an exception the caller sees as a 500.
    /// </summary>
    [Fact]
    public async Task TestAnEmptyRegisterStillProducesAReport()
    {
        Assert.True(IsPdf(await RenderAsync()));
    }

    /// <summary>
    /// The same mixed register, read back: the entity block, each risk's narrative, and the
    /// pre/post-treatment table with its headers, its dash for a missing residual, its signed deltas,
    /// and its rows ordered smallest reduction first (803 got worse, 802 has no residual, 801 dropped
    /// by 6) — after the narrative, which lists the risks in register order.
    /// </summary>
    [Fact]
    public async Task TestTheReportCarriesTheNarrativeAndTheOrderedTreatmentTable()
    {
        SeedEntity(806);

        SeedUnscoped(ctx =>
        {
            AddRisk(ctx, 806, 806);
            AddRisk(ctx, 807, 806);
            AddRisk(ctx, 808, 806);

            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 806, ScoringMethod = 1, CalculatedRisk = 8.0f, ResidualRisk = 2.0f,
                ClassicLikelihood = 4, ClassicImpact = 4
            });
            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 807, ScoringMethod = 1, CalculatedRisk = 6.0f,
                ClassicLikelihood = 3, ClassicImpact = 3
            });
            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 808, ScoringMethod = 1, CalculatedRisk = 3.0f, ResidualRisk = 5.0f,
                ClassicLikelihood = 2, ClassicImpact = 2
            });
        });

        var data = await RenderAsync();
        Assert.True(IsPdf(data));

        var pdf = PdfText.Read(data);
        string F(double value, string format = "0.00") => value.ToString(format, CultureInfo.CurrentCulture);

        foreach (var text in new[]
                 {
                     "Detailed Entities Risks Report",
                     "Entity: Retail Bank ( organization )",
                     "-- Risks --",
                     "ID: 806", "Subject: Risk 806", "Notes:",
                     "Score", "Base Likelihood: " + F(4, "G"), "Base Impact: " + F(4, "G"),
                     "Final Score:",
                     "PrePostTreatment",
                     "ID", "Subject", "InherentRisk", "ResidualRisk", "Delta", "Implementation %",
                     "Risk 808" + F(3) + F(5) + F(2, "+0.00;-0.00;0.00") + "—",
                     "Risk 807" + F(6) + "———",
                     "Risk 806" + F(8) + F(2) + F(-6, "+0.00;-0.00;0.00") + "—"
                 })
            Assert.True(pdf.Contains(text), $"the PDF does not contain \"{text}\" ");

        var table = pdf.IndexOf("PrePostTreatment");
        var worsened = pdf.IndexOf("Risk 808", table);
        var unscored = pdf.IndexOf("Risk 807", table);
        var treated = pdf.IndexOf("Risk 806", table);

        Assert.True(table > pdf.IndexOf("Subject: Risk 808"), "the table is not after the narrative");
        Assert.True(table < worsened && worsened < unscored && unscored < treated,
            "the table is not ordered smallest reduction first");
    }
}

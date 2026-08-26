using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.Extensions.Localization;
using ServerServices.Reports;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// The pre/post-treatment table on the Detailed Entities Risks report (Track 8 milestone 8.2.3).
///
/// The report renders a real PDF, so what is asserted is that it renders at all across the shapes
/// that break table layout: a risk with no residual score, a risk with no mitigation, an entity with
/// no risks, and a reduction that went the wrong way. MigraDoc raises those at layout time rather
/// than at build time, so a test that stubbed the renderer would pass on a report that cannot be
/// produced.
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
            ctx.Risks.Add(NewRisk(801, 801));
            ctx.Risks.Add(NewRisk(802, 801));
            ctx.Risks.Add(NewRisk(803, 801));

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
    /// MigraDoc is happy with but a naive `rows[0]` would not be.
    /// </summary>
    [Fact]
    public async Task TestAnEntityWhoseRisksHaveNoScoringStillRenders()
    {
        SeedEntity(804);

        SeedUnscoped(ctx => ctx.Risks.Add(NewRisk(804, 804)));

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
            var risk = NewRisk(805, 805);
            risk.Subject = string.Join(" ", Enumerable.Repeat("unpatched", 80));
            ctx.Risks.Add(risk);

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
}

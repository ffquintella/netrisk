using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using BackgroundJobs.Jobs;
using BackgroundJobs.Jobs.Calculation;
using BackgroundJobs.Jobs.Governance;
using BackgroundJobs.Tests.DI;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Model.Governance;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Security;
using ServerServices.Services;
using Xunit;

namespace BackgroundJobs.Tests.Jobs;

/// <summary>
/// T236 and T237 (S39 D-01, D-03): the job host as production composes it.
///
/// Two defects hid each other. Every job ran under a principal with no entity-scope claim, so it read
/// <c>DenyAll</c> and saw no risk, host, incident or finding; and five scheduled Track 8 jobs were never
/// registered, so Hangfire could not even construct them. Underneath both, the matrix and residual passes
/// would overwrite a quantitatively scored risk the moment they saw one. These tests run the real
/// registrations, the real accessor and the real <see cref="DalService.GetContext"/> on an in-memory
/// store — each fails on the code before the fix, for the reason its name gives.
/// </summary>
[TestSubject(typeof(ConfigurationManager))]
public class RecurringJobsContainerTest : IDisposable
{
    private const int EntityA = 100;
    private const int EntityB = 200;

    private readonly ServiceProvider _sp = JobHostContainer.Build(Guid.NewGuid().ToString());

    public void Dispose() => _sp.Dispose();

    /// <summary>What the activator Hangfire uses hands back for <paramref name="type"/>, or why it could not.</summary>
    private object? Activate(Type type, out string? failure)
    {
        failure = null;
        try
        {
            var job = new HangfireActivator(_sp).ActivateJob(type);
            if (job is null) failure = "not registered";
            return job;
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return null;
        }
    }

    private T Activate<T>() where T : class
    {
        var job = Activate(typeof(T), out var failure);
        Assert.True(job is not null, $"{typeof(T).Name}: {failure}");
        return (T)job!;
    }

    private DAL.Context.AuditableContext Unscoped() =>
        _sp.GetRequiredService<DalService>().GetContext(bypassEntityScope: true);

    private static Risk NewRisk(int id, int? entityId) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        EntityId = entityId,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Mitigation NewMitigation(int id, int riskId, int percent) => new()
    {
        Id = id, RiskId = riskId, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
        MitigationOwner = 1, SubmittedBy = 1, MitigationPercent = percent,
        CurrentSolution = string.Empty, SecurityRequirements = string.Empty,
        SecurityRecommendations = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PlanningDate = new DateOnly(2026, 6, 1)
    };

    /// <summary>Two business entities, a risk and a host in each — the shape no scoped principal sees whole.</summary>
    private void SeedTwoEntities()
    {
        using var db = Unscoped();

        foreach (var id in new[] { EntityA, EntityB })
            db.Entities.Add(new Entity
            {
                Id = id, DefinitionName = "unit", DefinitionVersion = "1", Status = "Active",
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow
            });

        db.Risks.AddRange(NewRisk(1, EntityA), NewRisk(2, EntityB));
        db.Hosts.AddRange(
            new Host { Id = 1, HostName = "a", Source = "test", EntityId = EntityA, RegistrationDate = DateTime.UtcNow },
            new Host { Id = 2, HostName = "b", Source = "test", EntityId = EntityB, RegistrationDate = DateTime.UtcNow });

        db.SaveChanges();
    }

    // --- T237: registration -------------------------------------------------------------------------

    /// <summary>
    /// Every job <c>JobsManager</c> schedules resolves through the activator Hangfire runs it with. An
    /// unregistered type comes back null and Hangfire fails the run before the job exists; this is the
    /// check that turns "scheduled but not registered" into a red test.
    /// </summary>
    [Fact]
    public void EveryScheduledJobResolvesFromTheJobHostContainer()
    {
        var schedule = JobHostContainer.Schedule(_sp);
        Assert.NotEmpty(schedule);

        var unresolved = schedule
            .Select(j => (j.Id, j.Type, Job: Activate(j.Type, out var failure), failure))
            .Where(r => r.Job is null)
            .Select(r => $"{r.Id} ({r.Type.Name}): {r.failure}")
            .ToList();

        Assert.True(unresolved.Count == 0,
            "Scheduled but not resolvable from the job host's container:\n  " + string.Join("\n  ", unresolved));
    }

    /// <summary>The five Track 8 jobs D-01 found scheduled and unregistered, named, so the list cannot shrink silently.</summary>
    [Theory]
    [InlineData(typeof(ResidualRiskCalculation))]
    [InlineData(typeof(GovernanceRetentionJob))]
    [InlineData(typeof(RiskAcceptanceExpiryPass))]
    [InlineData(typeof(RiskReviewCadenceJob))]
    [InlineData(typeof(RiskReviewCampaignJob))]
    public void TheFiveTrack8JobsAreScheduledAndResolve(Type jobType)
    {
        Assert.Contains(JobHostContainer.Schedule(_sp), j => j.Type == jobType);

        var job = Activate(jobType, out var failure);
        Assert.True(job is not null, $"{jobType.Name}: {failure}");
    }

    /// <summary>
    /// The job a request enqueues (<c>ReportSchedulesController</c>) is not in the schedule above, so its registration —
    /// and the creator-scope resolver it now needs (T237 review) — is checked here: unregistered, it would fail every run.
    /// </summary>
    [Fact]
    public void TheRequestEnqueuedReportJobResolvesWithItsScopeResolver()
    {
        var job = Activate(typeof(ServerServices.Services.ScheduledReportJob), out var failure);
        Assert.True(job is not null, $"{nameof(ServerServices.Services.ScheduledReportJob)}: {failure}");

        using var scope = _sp.CreateScope();
        Assert.IsType<ServerServices.Security.UserEntityScopeResolver>(
            scope.ServiceProvider.GetRequiredService<ServerServices.Security.IUserEntityScopeResolver>());
    }

    // --- T237: scope ----------------------------------------------------------------------------------

    [Fact]
    public void TheJobHostPrincipalCarriesTheGlobalScopeClaimAndReadsUnrestricted()
    {
        var user = _sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;

        Assert.True(user.HasClaim(BackgroundServiceHttpContextAccessor.ScopeClaimType,
            BackgroundServiceHttpContextAccessor.GlobalScope));
        Assert.Equal(BackgroundServiceHttpContextAccessor.ServiceAccountSid, user.FindFirstValue(ClaimTypes.Sid));

        Assert.True(_sp.GetRequiredService<IDalService>().GetCurrentEntityScope().IsUnrestricted);
    }

    /// <summary>
    /// Every scheduled job, as Hangfire would construct it, reads the rows of both entities — through the
    /// <see cref="DalService"/> the job holds and through the <see cref="IDalService"/> its services hold. Before
    /// the fix both read <c>DenyAll</c>: zero risks and zero hosts, whatever the database held.
    /// </summary>
    [Fact]
    public void EveryScheduledJobReadsTheSeededRowsOfEveryEntity()
    {
        SeedTwoEntities();

        foreach (var scheduled in JobHostContainer.Schedule(_sp))
        {
            var job = Activate(scheduled.Type, out var failure);
            Assert.True(job is not null, $"{scheduled.Type.Name}: {failure}");

            if (job is not BaseJob baseJob) continue;

            using var db = baseJob.DalService.GetContext();
            Assert.True(db.Risks.Count() == 2, $"{scheduled.Type.Name} sees {db.Risks.Count()} of 2 risks");
            Assert.True(db.Hosts.Count() == 2, $"{scheduled.Type.Name} sees {db.Hosts.Count()} of 2 hosts");
        }

        using var services = _sp.GetRequiredService<IDalService>().GetContext();
        Assert.Equal(new[] { 1, 2 }, services.Risks.Select(r => r.Id).OrderBy(id => id).ToList());
        Assert.Equal(new[] { 1, 2 }, services.Hosts.Select(h => h.Id).OrderBy(id => id).ToList());
    }

    /// <summary>
    /// The write half. Under <c>DenyAll</c> the write guard refused any scoped insert — the scheduled Vision One
    /// and SecurityScorecard syncs failed on their first host. The job host now writes to any entity, and the
    /// row is attributed to the background account.
    /// </summary>
    [Fact]
    public async Task TheJobHostWritesAScopedRowToAnyEntity()
    {
        SeedTwoEntities();

        await using (var db = _sp.GetRequiredService<IDalService>().GetContext())
        {
            Assert.Equal(1, db.UserId);

            db.Hosts.Add(new Host
            {
                Id = 3, HostName = "c", Source = "vision-one", EntityId = EntityB, RegistrationDate = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        await using var check = Unscoped();
        Assert.Equal(EntityB, check.Hosts.Single(h => h.Id == 3).EntityId);
    }

    /// <summary>
    /// The review cadence, run end to end: a risk flagged for review in another entity reaches its owner. Before
    /// the fix the job could not be constructed and, once constructed, read no flagged risk.
    /// </summary>
    [Fact]
    public async Task TheReviewCadenceJobNotifiesTheOwnerOfAFlaggedRiskInAnyEntity()
    {
        SeedTwoEntities();

        await using (var db = Unscoped())
        {
            var risk = db.Risks.Single(r => r.Id == 2);
            risk.ReviewRequested = true;
            risk.ReviewRequestedAt = DateTime.UtcNow;
            risk.ReviewRequestedReason = "A Critical vulnerability was linked.";
            risk.Owner = 10;
            await db.SaveChangesAsync();
        }

        Activate<RiskReviewCadenceJob>().Run();

        await using var check = Unscoped();
        var message = Assert.Single(check.Messages, m => m.UserId == 10);
        Assert.Contains("Risk 2", message.Message1);
    }

    // --- T236: the two matrix passes leave a quantitative risk alone ------------------------------------

    /// <summary>
    /// The ordering constraint of S39 §14.1, observed: with the scope fixed the two-hourly matrix pass and the
    /// nightly residual pass see every risk, and neither may rewrite one the FAIR-lite simulation scored. The
    /// classic risk beside it proves the passes did run over the rows — a test where both risks were untouched
    /// would pass under <c>DenyAll</c> too.
    /// </summary>
    [Fact]
    public async Task TheMatrixAndResidualJobsLeaveAQuantitativeRiskAsTheSimulationScoredIt()
    {
        const int classic = 1, quantitative = 2;

        await using (var db = Unscoped())
        {
            db.Risks.AddRange(NewRisk(classic, EntityA), NewRisk(quantitative, EntityB));
            // custom_risk_model_values is keyless, so the in-memory store cannot hold a row of it: with no cell
            // for (3, 3) the matrix pass writes 0, which is what shows it ran over the classic risk.
            db.RiskScorings.AddRange(
                new RiskScoring
                    { Id = classic, ScoringMethod = 1, CalculatedRisk = 8f, ClassicImpact = 3, ClassicLikelihood = 3 },
                new RiskScoring
                    { Id = quantitative, ScoringMethod = 1, ClassicImpact = 3, ClassicLikelihood = 3 });
            db.Mitigations.AddRange(NewMitigation(1, classic, 50), NewMitigation(2, quantitative, 50));

            // The floor of the simulator, so the nightly re-simulation is quick; it reads the same setting.
            db.Settings.Add(new Setting { Name = QuantitativeRiskService.IterationsSetting, Value = "2000" });
            await db.SaveChangesAsync();
        }

        await _sp.GetRequiredService<IQuantitativeRiskService>().ComputeAndSaveAsync(quantitative,
            new QuantitativeRiskInput
            {
                LossEventFrequencyMin = 1, LossEventFrequencyMostLikely = 2, LossEventFrequencyMax = 4,
                LossMagnitudeMin = 10_000, LossMagnitudeMostLikely = 50_000, LossMagnitudeMax = 500_000,
                Seed = 42
            });

        RiskScoring Scoring(int id)
        {
            using var db = Unscoped();
            return db.RiskScorings.Single(s => s.Id == id);
        }

        var simulated = Scoring(quantitative);
        Assert.Equal(QuantitativeRiskService.QuantitativeScoringMethod, simulated.ScoringMethod);
        Assert.NotNull(simulated.ResidualRisk);
        // Neither number may coincide with what the matrix passes would write, or the test proves nothing.
        Assert.NotEqual(0f, simulated.CalculatedRisk);
        Assert.NotEqual(simulated.CalculatedRisk * 0.5f, simulated.ResidualRisk!.Value, 3);
        Assert.Null(Scoring(classic).ResidualRisk);

        Activate<RiskScoreCalculation>().Run();

        // Checked between the two jobs: the residual job re-simulates first, which would restore the inherent
        // score the matrix pass overwrote and hide the overwrite from a check made only at the end.
        Assert.Equal(0f, Scoring(classic).CalculatedRisk);
        Assert.Equal(simulated.CalculatedRisk, Scoring(quantitative).CalculatedRisk);

        Activate<ResidualRiskCalculation>().Run();

        // The classic risk got a residual — its treatment applied to the inherent score the matrix wrote.
        var classicAfter = Scoring(classic);
        Assert.NotNull(classicAfter.ResidualRisk);
        Assert.Equal(0f, classicAfter.ResidualRisk!.Value, 3);

        var quantitativeAfter = Scoring(quantitative);
        Assert.Equal(QuantitativeRiskService.QuantitativeScoringMethod, quantitativeAfter.ScoringMethod);
        Assert.Equal(simulated.CalculatedRisk, quantitativeAfter.CalculatedRisk);
        Assert.Equal(simulated.ResidualRisk!.Value, quantitativeAfter.ResidualRisk!.Value, 3);
    }
}

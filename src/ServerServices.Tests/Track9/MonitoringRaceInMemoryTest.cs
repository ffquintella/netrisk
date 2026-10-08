using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Model.Exceptions;
using Model.Monitoring;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.8 (S49 §4.7, D8) — the losers of the races the unique indexes settle. The in-memory provider enforces no
/// unique index, so a save interceptor stands in for the concurrent writer: it lets a "winner" write first through
/// another context and then fails the service's save the way MariaDB 1062 does, naming the index. DAL.IntegrationTests
/// (Track9KriReassessmentSchemaTests R1–R3) runs the real race on MariaDB.
///
/// What each loser must do: a lost episode or trigger is re-read and the evaluation continues, raising only what is
/// still missing (RC1, RC2); a failure that is not that duplicate key is not a race and propagates (RC3); a second
/// declaration of an incident is the same 409 as when it is found first (RC4); applying an event to a risk a concurrent
/// request just applied it to is idempotent success, not a 500 (RC5).
/// </summary>
[TestSubject(typeof(MonitoringService))]
public class MonitoringRaceInMemoryTest
{
    private const int Kri = 10;
    private const int Opening = 100;
    private const int Risk = 1;
    private const int User = 7;

    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly Racer _racer = new();
    private readonly INotificationEventPublisher _publisher = Substitute.For<INotificationEventPublisher>();
    private readonly MonitoringService _service;

    public MonitoringRaceInMemoryTest()
    {
        _service = new MonitoringService(Substitute.For<Serilog.ILogger>(), new RacingDal(Options(_racer)), _publisher);

        using var db = Plain();
        db.Risks.Add(NewRisk(Risk));
        db.Kris.Add(new DAL.Entities.Kri
        {
            Id = Kri, Name = "ERP hours down", Category = KriCategory.Unavailability, Source = "Zabbix", Unit = "hours",
            Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 8m, ToleranceRationale = "Board minute",
            MaxReadingAgeDays = 31, CreatedAt = DateTime.UtcNow.AddDays(-30)
        });
        db.KriRisks.Add(new KriRisk { KriId = Kri, RiskId = Risk, CreatedAt = DateTime.UtcNow.AddDays(-30) });
        db.KriReadings.Add(new KriReading
        {
            Id = Opening, KriId = Kri, Value = 12m, ObservedAt = DateTime.UtcNow.AddDays(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        });
        db.SaveChanges();
    }

    // --- the racing database -------------------------------------------------------------------------

    private DbContextOptions<NRDbContext> Options(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<NRDbContext>().UseInMemoryDatabase(_name, _root);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return builder.Options;
    }

    /// <summary>A context with no racer: the winner's connection, and the test's eyes.</summary>
    private AuditableContext Plain() => new(Options());

    private static Risk NewRisk(int id) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R{id}", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", SubmissionDate = DateTime.UtcNow, LastUpdate = DateTime.UtcNow
    };

    private sealed class RacingDal(DbContextOptions<NRDbContext> options) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) => new(options);

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    /// <summary>
    /// Fails the first save whose tracked changes match <see cref="When"/>, after letting <see cref="Winner"/> write — the
    /// concurrent writer — with <see cref="Message"/> as the database's error.
    /// </summary>
    private sealed class Racer : SaveChangesInterceptor
    {
        public Func<DbContext, bool>? When { get; set; }

        public Action? Winner { get; set; }

        public string Message { get; set; } = string.Empty;

        public int Thrown { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Race(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Race(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Race(DbContext? context)
        {
            if (context is null || Thrown > 0 || When is null || !When(context)) return;

            Thrown++;
            Winner?.Invoke();
            throw new DbUpdateException("An error occurred while saving the entity changes.",
                new InvalidOperationException(Message));
        }
    }

    private static bool Adding<T>(DbContext context) where T : class =>
        context.ChangeTracker.Entries<T>().Any(e => e.State == EntityState.Added);

    private static string Duplicate(string index) => $"Duplicate entry '1-1' for key '{index}'";

    private void WinnerEpisode()
    {
        using var db = Plain();
        db.ReassessmentEvents.Add(new ReassessmentEvent
        {
            TriggerType = ReassessmentTriggerType.NewDataOrKriBreach, Origin = ReassessmentEventOrigin.KriBreach,
            Title = "KRI 'ERP hours down' beyond its tolerance", OccurredAt = DateTime.UtcNow.AddDays(-1), KriId = Kri,
            KriReadingId = Opening, CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private int Count<T>() where T : class
    {
        using var db = Plain();
        return db.Set<T>().Count();
    }

    // --- RC1–RC5 -------------------------------------------------------------------------------------

    /// <summary>
    /// RC1 — the evaluation that loses the trigger race re-reads and raises what is still missing: the trigger exists once
    /// and is announced once. Dropping the batch on the duplicate — the first implementation — left the risk without a
    /// trigger until the next nightly pass.
    /// </summary>
    [Fact]
    public async Task TestRC1_TheLoserOfTheTriggerRaceReReadsAndContinues()
    {
        _racer.When = Adding<RiskReassessmentTrigger>;
        _racer.Message = Duplicate(MonitoringService.TriggerIndex);

        var summary = await _service.EvaluateAllAsync();

        Assert.Equal(1, _racer.Thrown);
        Assert.Equal(1, summary.TriggersRaised);
        Assert.Equal((1, 1), (Count<ReassessmentEvent>(), Count<RiskReassessmentTrigger>()));
        await _publisher.Received(1).RiskReassessmentTriggeredAsync(Arg.Any<Risk>(), Arg.Any<double?>(),
            Arg.Any<ReassessmentEvent>());

        using var db = Plain();
        Assert.True(db.Risks.Single(r => r.Id == Risk).ReviewRequested);
    }

    /// <summary>
    /// RC2 — the evaluation that loses the episode race uses the winner's episode: one event, one trigger, and the breach
    /// is announced by the winner, not again by the loser.
    /// </summary>
    [Fact]
    public async Task TestRC2_TheLoserOfTheEpisodeRaceUsesTheWinnersEpisode()
    {
        _racer.When = Adding<ReassessmentEvent>;
        _racer.Winner = WinnerEpisode;
        _racer.Message = Duplicate(MonitoringService.EpisodeIndex);

        var summary = await _service.EvaluateAllAsync();

        Assert.Equal(1, _racer.Thrown);
        Assert.Equal((0, 1), (summary.EpisodesOpened, summary.TriggersRaised));
        Assert.Equal((1, 1), (Count<ReassessmentEvent>(), Count<RiskReassessmentTrigger>()));
        await _publisher.DidNotReceive().KriToleranceBreachedAsync(Arg.Any<DAL.Entities.Kri>(), Arg.Any<decimal>(),
            Arg.Any<DateTime>(), Arg.Any<int>());
    }

    /// <summary>
    /// RC3 — a save failure that is not the duplicate key of the race (a foreign key, a CHECK, a lost connection) is not
    /// swallowed as one: it propagates from an evaluation run on request, and the nightly pass logs it per KRI.
    /// </summary>
    [Fact]
    public async Task TestRC3_AFailureThatIsNotTheRacePropagates()
    {
        _racer.When = Adding<RiskReassessmentTrigger>;
        _racer.Message = "Cannot add or update a child row: a foreign key constraint fails (fk_risk_reassessment_triggers_risk_id)";

        await Assert.ThrowsAsync<DbUpdateException>(() => _service.LinkRiskAsync(Kri, Risk, User));

        Assert.Equal(0, Count<RiskReassessmentTrigger>());
    }

    /// <summary>
    /// RC4 — two declarations of the same incident at once: the one that loses the unique index answers the same 409, naming
    /// the winner's event, instead of a 500.
    /// </summary>
    [Fact]
    public async Task TestRC4_ASecondDeclarationOfAnIncidentInARaceIsAConflict()
    {
        await using (var db = Plain())
        {
            db.Incidents.Add(new Incident { Id = 5, Name = "2026-5", Description = "Ransomware", Kind = IncidentKind.Incident });
            await db.SaveChangesAsync();
        }

        _racer.When = Adding<ReassessmentEvent>;
        _racer.Message = Duplicate(MonitoringService.IncidentIndex);
        _racer.Winner = () =>
        {
            using var db = Plain();
            db.ReassessmentEvents.Add(new ReassessmentEvent
            {
                Id = 77, TriggerType = ReassessmentTriggerType.SignificantIncidentOrNearMiss,
                Origin = ReassessmentEventOrigin.Declared, Title = "Ransomware", OccurredAt = DateTime.UtcNow,
                IncidentId = 5, DeclaredById = 8, CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        };

        var ex = await Assert.ThrowsAsync<DataAlreadyExistsException>(() => _service.DeclareEventAsync(
            new ReassessmentEventRequest
            {
                Type = ReassessmentTriggerType.SignificantIncidentOrNearMiss, Title = "Ransomware on the file server",
                OccurredAt = DateTime.UtcNow.AddHours(-1), IncidentId = 5, RiskIds = [Risk]
            }, User));

        Assert.Equal("77", ex.Identification);
        Assert.Equal(1, Count<ReassessmentEvent>());
    }

    /// <summary>
    /// RC5 — applying an event to a risk that a concurrent request has just applied it to is the idempotent call it is: the
    /// risk is listed as already triggered and the trigger exists once, rather than a 500.
    /// </summary>
    [Fact]
    public async Task TestRC5_ApplyingAnEventInARaceIsIdempotent()
    {
        await using (var db = Plain())
        {
            db.Risks.Add(NewRisk(2));
            db.ReassessmentEvents.Add(new ReassessmentEvent
            {
                Id = 50, TriggerType = ReassessmentTriggerType.NewRegulation, Origin = ReassessmentEventOrigin.Declared,
                Title = "ANPD resolution", OccurredAt = DateTime.UtcNow, DeclaredById = User, CreatedAt = DateTime.UtcNow
            });
            db.RiskReassessmentTriggers.Add(new RiskReassessmentTrigger
                { EventId = 50, RiskId = Risk, RaisedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        _racer.When = Adding<RiskReassessmentTrigger>;
        _racer.Message = Duplicate(MonitoringService.TriggerIndex);
        _racer.Winner = () =>
        {
            using var db = Plain();
            db.RiskReassessmentTriggers.Add(new RiskReassessmentTrigger
                { EventId = 50, RiskId = 2, RaisedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
        };

        var result = await _service.AddEventRisksAsync(50, new ReassessmentRisksRequest { RiskIds = [2] }, User);

        Assert.Equal(1, _racer.Thrown);
        Assert.Equal([2], result.AlreadyTriggeredRiskIds);
        Assert.Equal(2, result.Triggers.Count);
        Assert.Equal(2, Count<RiskReassessmentTrigger>());
    }
}

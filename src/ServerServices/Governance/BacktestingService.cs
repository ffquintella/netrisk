using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.DecisionCycle;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.DecisionCycle;

namespace ServerServices.Governance;

/// <summary>
/// Stage 9.9 (S50 §4.4) — backtesting incidents and near misses against the register, the calibration the methodology's
/// Phase 4 asks the cut to be checked by ("backtesting de incidentes e near misses … taxa de falsos negativos").
///
/// A person decides which registered risks describe an incident; the dates decide whether those risks foresaw it
/// (<see cref="BacktestClassifier"/>, pure). The one rule that matters is not left to the person: a risk registered at or
/// after the incident occurred is never counted as foreseeing it (S50 D6).
///
/// The outcome is computed with every match, read unscoped once the incident is known visible, so it never depends on
/// who asks (S50 D7); the matches outside the caller's scope are counted, not disclosed.
/// </summary>
public class BacktestingService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IBacktestingService
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The clock, replaceable by tests.</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    public async Task<BacktestIncidentDto> GetIncidentAsync(int incidentId)
    {
        await using var db = DalService.GetContext();

        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId)
                       ?? throw new DataNotFoundException("incidents", incidentId.ToString(Invariant));

        return (await BuildAsync(db, [incident])).Single();
    }

    public async Task<BacktestIncidentDto> AssessAsync(int incidentId, BacktestAssessmentRequest request, int actingUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        var riskIds = (request.RiskIds ?? []).ToList();
        if (riskIds.Any(id => id <= 0))
            throw new InvalidParameterException(nameof(BacktestAssessmentRequest.RiskIds), "A risk is a positive id.");

        riskIds = riskIds.Distinct().ToList();
        if (riskIds.Count > DecisionCycleLimits.MaxBacktestRisks)
            throw new InvalidParameterException(nameof(BacktestAssessmentRequest.RiskIds),
                $"An incident is matched to at most {DecisionCycleLimits.MaxBacktestRisks} risks.");

        if (riskIds.Count == 0 && !request.NoCorrespondingScenario)
            throw new InvalidParameterException(nameof(BacktestAssessmentRequest.NoCorrespondingScenario),
                "Name the registered risks that describe the incident, or state that none does — an empty list is not " +
                "taken as an answer by accident.");

        if (riskIds.Count > 0 && request.NoCorrespondingScenario)
            throw new InvalidParameterException(nameof(BacktestAssessmentRequest.NoCorrespondingScenario),
                "Risks were named and 'no corresponding scenario' was stated: one or the other.");

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > DecisionCycleLimits.MaxBacktestNoteLength })
            throw new InvalidParameterException(nameof(BacktestAssessmentRequest.Note),
                $"At most {DecisionCycleLimits.MaxBacktestNoteLength} characters.");

        var now = Clock();

        await using var db = DalService.GetContext();
        db.UserId = actingUserId;

        var incident = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == incidentId)
                       ?? throw new DataNotFoundException("incidents", incidentId.ToString(Invariant));

        var visible = await db.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id)).Select(r => r.Id).ToListAsync();
        var missing = riskIds.Except(visible).OrderBy(id => id).FirstOrDefault();
        if (missing != 0) throw new DataNotFoundException("risks", missing.ToString(Invariant));

        var backtest = await db.IncidentBacktests.Include(b => b.Risks).FirstOrDefaultAsync(b => b.IncidentId == incidentId);
        if (backtest is null)
        {
            backtest = new IncidentBacktest { IncidentId = incidentId, CreatedAt = now };
            db.IncidentBacktests.Add(backtest);
        }
        else
        {
            backtest.UpdatedAt = now;
        }

        backtest.Note = note;
        backtest.AssessedAt = now;
        backtest.AssessedById = actingUserId;

        // Replaced, not merged: the assessment is the assessor's current answer. A removed match stays in the trail. The
        // links read here are the caller's — a match outside their scope is neither shown nor removed.
        foreach (var link in backtest.Risks.Where(l => !riskIds.Contains(l.RiskId)).ToList())
            db.IncidentBacktestRisks.Remove(link);

        foreach (var riskId in riskIds.Where(id => backtest.Risks.All(l => l.RiskId != id)))
            backtest.Risks.Add(new IncidentBacktestRisk { RiskId = riskId, CreatedAt = now });

        await db.SaveChangesAsync();

        Logger.Information("Incident {Incident} backtested by user {User} against {Count} risk(s)", incidentId,
            actingUserId, riskIds.Count);

        return (await BuildAsync(db, [incident])).Single();
    }

    public async Task<BacktestReportDto> GetReportAsync(DateTime? from, DateTime? to, int? entityId)
    {
        var now = Clock();
        var toUtc = to ?? now;
        var fromUtc = from ?? toUtc.AddDays(-DecisionCycleLimits.DefaultReportDays);

        if (toUtc < fromUtc)
            throw new InvalidParameterException("to", "'to' is before 'from'.");
        if ((toUtc - fromUtc).TotalDays > DecisionCycleLimits.MaxReportDays)
            throw new InvalidParameterException("from", $"A report covers at most {DecisionCycleLimits.MaxReportDays} days.");
        if (entityId is <= 0)
            throw new InvalidParameterException("entityId", "The entity is an entity id.");

        await using var db = DalService.GetContext();

        // A coarse filter in the database, the exact occurrence (the earliest of three dates) in memory. The occurrence is
        // never after the creation, so an incident that occurred in the period was created at or after its start.
        var incidents = await db.Incidents.AsNoTracking()
            .Where(i => entityId == null || i.EntityId == entityId)
            .Where(i => i.CreationDate >= fromUtc)
            .ToListAsync();

        incidents = incidents
            .Where(i =>
            {
                var occurred = BacktestClassifier.OccurrenceOf(i.StartDate, i.ReportDate, i.CreationDate);
                return occurred >= fromUtc && occurred <= toUtc;
            })
            .ToList();

        var items = await BuildAsync(db, incidents);

        var report = new BacktestReportDto { From = fromUtc, To = toUtc, EntityId = entityId };
        BacktestClassifier.Summarize(report, items.Select(i => (i.Kind, i.Outcome)));

        report.Truncated = items.Count > DecisionCycleLimits.MaxReportItems;
        report.Items = items.OrderByDescending(i => i.OccurredAt).ThenByDescending(i => i.IncidentId)
            .Take(DecisionCycleLimits.MaxReportItems).ToList();
        return report;
    }

    // --- internals ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The backtest of each incident: its matches read unscoped (the outcome must not depend on who asks), each match's
    /// registration date and what the register had decided about it when the incident occurred, and the matches the
    /// caller may see.
    /// </summary>
    private async Task<List<BacktestIncidentDto>> BuildAsync(AuditableContext db, List<Incident> incidents)
    {
        if (incidents.Count == 0) return [];

        var incidentIds = incidents.Select(i => i.Id).ToList();

        await using var system = DalService.GetContext(withIdentity: false, bypassEntityScope: true);

        var backtests = await system.IncidentBacktests.AsNoTracking()
            .Where(b => incidentIds.Contains(b.IncidentId))
            .Include(b => b.Risks)
            .ToDictionaryAsync(b => b.IncidentId);

        var riskIds = backtests.Values.SelectMany(b => b.Risks).Select(l => l.RiskId).Distinct().ToList();

        var risks = await system.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Subject, r.SubmissionDate, r.Status, r.CloseId })
            .ToDictionaryAsync(r => r.Id);
        var visible = (await db.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id)).Select(r => r.Id).ToListAsync())
            .ToHashSet();
        var histories = await HistoriesAsync(system, riskIds);

        return incidents.Select(incident =>
        {
            var occurredAt = BacktestClassifier.OccurrenceOf(incident.StartDate, incident.ReportDate, incident.CreationDate);
            backtests.TryGetValue(incident.Id, out var backtest);

            var links = (backtest?.Risks ?? [])
                .Where(l => risks.ContainsKey(l.RiskId))
                .Select(l =>
                {
                    var risk = risks[l.RiskId];
                    var reasons = BacktestClassifier.DismissalReasons(
                        histories.GetValueOrDefault(l.RiskId) ?? RiskCutHistory.None, occurredAt);
                    return (Link: new BacktestLink(l.RiskId, risk.SubmissionDate, reasons), risk.Subject);
                })
                .OrderBy(l => l.Link.RiskId)
                .ToList();

            return new BacktestIncidentDto
            {
                IncidentId = incident.Id,
                IncidentName = incident.Name,
                Kind = incident.Kind,
                EntityId = incident.EntityId,
                OccurredAt = occurredAt,
                Outcome = BacktestClassifier.Classify(backtest is not null, occurredAt, links.Select(l => l.Link).ToList()),
                AssessedAt = backtest?.AssessedAt,
                AssessedById = backtest?.AssessedById,
                Note = backtest?.Note,
                Risks = links.Where(l => visible.Contains(l.Link.RiskId)).Select(l => new BacktestRiskDto
                {
                    RiskId = l.Link.RiskId,
                    Subject = l.Subject,
                    RegisteredAt = l.Link.RegisteredAt,
                    RegisteredBeforeOccurrence = BacktestClassifier.Foresees(l.Link.RegisteredAt, occurredAt),
                    DismissedAtOccurrence = l.Link.Dismissed,
                    DismissalReasons = l.Link.DismissalReasons.ToList()
                }).ToList(),
                HiddenRiskCount = links.Count(l => !visible.Contains(l.Link.RiskId))
            };
        }).ToList();
    }

    /// <summary>What the register had decided about each risk over time: archives, the current closure, acceptances, decisions.</summary>
    private static async Task<Dictionary<int, RiskCutHistory>> HistoriesAsync(AuditableContext system, List<int> riskIds)
    {
        if (riskIds.Count == 0) return [];

        var archives = await system.RiskArchives.AsNoTracking().Where(a => riskIds.Contains(a.RiskId))
            .Select(a => new { a.RiskId, a.ArchivedAt, a.ReopenedAt, a.Status, a.ClosureId }).ToListAsync();

        var risks = await system.Risks.AsNoTracking().Where(r => riskIds.Contains(r.Id))
            .Select(r => new { r.Id, r.Status }).ToDictionaryAsync(r => r.Id);

        var closures = await system.Closures.AsNoTracking().Where(c => riskIds.Contains(c.RiskId))
            .Select(c => new { c.Id, c.RiskId, c.ClosureDate }).ToListAsync();

        var acceptances = await system.RiskAcceptances.AsNoTracking()
            .Where(a => a.RiskId != null && riskIds.Contains(a.RiskId.Value))
            .Select(a => new { RiskId = a.RiskId!.Value, a.StartDate, a.ExpiresAt, a.RevokedAt }).ToListAsync();

        var decisions = await system.RiskDecisions.AsNoTracking().Where(d => riskIds.Contains(d.RiskId))
            .Select(d => new { d.RiskId, d.DecidedAt, d.Decision }).ToListAsync();

        return riskIds.ToDictionary(id => id, id =>
        {
            var closed = risks.TryGetValue(id, out var risk) &&
                         string.Equals(risk.Status, RiskWorkflowService.StatusClosed, StringComparison.OrdinalIgnoreCase);

            // An archive's window ends when it was reopened; one superseded by the legacy reopen route ends with its
            // closure — which no longer exists, so its end is unknown and only the current closure (below) can say.
            var windows = archives.Where(a => a.RiskId == id)
                .Select(a => new ArchiveWindow(a.ArchivedAt,
                    a.Status == RiskArchiveStatus.Reopened ? a.ReopenedAt
                    : closed && closures.Any(c => c.Id == a.ClosureId) ? null
                    : a.ArchivedAt))
                .ToList();

            return new RiskCutHistory(
                windows,
                closed ? closures.Where(c => c.RiskId == id).Select(c => (DateTime?)c.ClosureDate).Min() : null,
                acceptances.Where(a => a.RiskId == id)
                    .Select(a => new AcceptanceWindow(a.StartDate, a.ExpiresAt, a.RevokedAt)).ToList(),
                decisions.Where(d => d.RiskId == id).Select(d => new DatedDecision(d.DecidedAt, d.Decision)).ToList());
        });
    }
}

using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Findings;

/// <summary>
/// Finding triage lifecycle and audit trail (Track 3 milestone 3.2).
///
/// Two invariants hold everywhere in this class: a status only ever changes through
/// <see cref="FindingStatusMachine"/>, and a status change and its history row are written in the
/// same <c>SaveChanges</c>. The second is what makes the timeline trustworthy — a finding whose
/// status moved without a history row is indistinguishable from one that was tampered with.
/// </summary>
public class FindingLifecycleService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IFindingLifecycleService
{
    /// <summary>
    /// Pre-expiry warning points, in days before expiry. The spec's T-30/T-7; a caller may override.
    /// Descending so the pass reports the smallest threshold crossed.
    /// </summary>
    public static readonly int[] DefaultWarningThresholds = [30, 7];

    public async Task<Vulnerability> TransitionAsync(int findingId, FindingStatus to, int? userId,
        FindingStatusChangeSource source, string? justification = null, int? duplicateOfId = null,
        int? riskAcceptanceId = null)
    {
        await using var db = DalService.GetContext();

        var finding = await db.Vulnerabilities.FirstOrDefaultAsync(v => v.Id == findingId);
        if (finding == null)
            throw new DataNotFoundException("vulnerabilities", findingId.ToString(),
                new Exception("Finding not found"));

        var from = finding.LifecycleStatus;

        FindingStatusMachine.Validate(from, to, justification, duplicateOfId, findingId);

        if (duplicateOfId != null)
        {
            // Checked against the database rather than trusted: a duplicate pointing at a
            // nonexistent finding is a finding that has been hidden, and the FK would only catch it
            // on save with a much worse error.
            var canonicalExists = await db.Vulnerabilities.AnyAsync(v => v.Id == duplicateOfId.Value);
            if (!canonicalExists)
                throw new DataNotFoundException("vulnerabilities", duplicateOfId.Value.ToString(),
                    new Exception("Canonical finding not found"));
        }

        ApplyTransition(db, finding, to, userId, source, justification, duplicateOfId, riskAcceptanceId,
            DateTime.UtcNow);

        await db.SaveChangesAsync();

        Logger.Information(
            "Finding {Finding} moved {From} to {To} by user {User} source {Source}",
            findingId, from, to, userId, source);

        return finding;
    }

    public async Task RecordCreationAsync(int findingId, FindingStatus initialStatus, int? userId,
        FindingStatusChangeSource source, DateTime? at = null)
    {
        await using var db = DalService.GetContext();

        db.FindingStatusHistories.Add(new FindingStatusHistory
        {
            VulnerabilityId = findingId,
            // Null, not Active: there is no state a new finding came from, and writing one would
            // misrepresent creation as a transition.
            FromStatus = null,
            ToStatus = initialStatus,
            UserId = userId,
            Source = source,
            ChangedAt = at ?? DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public async Task<List<FindingStatusHistory>> GetHistoryAsync(int findingId)
    {
        await using var db = DalService.GetContext();

        return await db.FindingStatusHistories
            .AsNoTracking()
            .Include(h => h.User)
            .Where(h => h.VulnerabilityId == findingId)
            .OrderByDescending(h => h.ChangedAt)
            .ThenByDescending(h => h.Id)
            .ToListAsync();
    }

    public async Task<List<FindingStatus>> GetAllowedTransitionsAsync(int findingId)
    {
        await using var db = DalService.GetContext();

        var finding = await db.Vulnerabilities.AsNoTracking()
            .Select(v => new { v.Id, v.LifecycleStatus })
            .FirstOrDefaultAsync(v => v.Id == findingId);

        if (finding == null)
            throw new DataNotFoundException("vulnerabilities", findingId.ToString(),
                new Exception("Finding not found"));

        return FindingStatusMachine.AllowedTargets(finding.LifecycleStatus).ToList();
    }

    // --- Risk acceptance -------------------------------------------------------------------

    public async Task<RiskAcceptance> CreateAcceptanceAsync(RiskAcceptance acceptance,
        IReadOnlyList<int> findingIds, int? userId)
    {
        ValidateAcceptance(acceptance);

        await using var db = DalService.GetContext();

        var now = DateTime.UtcNow;

        acceptance.Id = 0;
        acceptance.Status = RiskAcceptanceStatus.Active;
        acceptance.CreatedAt = now;
        acceptance.CreatedById = userId;
        acceptance.RevokedAt = null;
        acceptance.RevokedById = null;
        acceptance.LastWarningDaysBefore = null;

        db.RiskAcceptances.Add(acceptance);
        await db.SaveChangesAsync();

        if (findingIds.Count > 0)
            await AttachFindingsAsync(db, acceptance, findingIds, userId, now);

        Logger.Information(
            "Risk acceptance {Id} created by user {User}, authorized by {Manager}, expires {Expiry}, covering {Count} findings",
            acceptance.Id, userId, acceptance.AuthorizingManagerId, acceptance.ExpiresAt, findingIds.Count);

        return acceptance;
    }

    public async Task<RiskAcceptance> GetAcceptanceAsync(int id)
    {
        await using var db = DalService.GetContext();

        var acceptance = await db.RiskAcceptances
            .AsNoTracking()
            .Include(a => a.AuthorizingManager)
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (acceptance == null)
            throw new DataNotFoundException("risk_acceptances", id.ToString(),
                new Exception("Risk acceptance not found"));

        return acceptance;
    }

    public async Task<List<RiskAcceptance>> GetAcceptancesAsync(int? expiringWithinDays = null)
    {
        await using var db = DalService.GetContext();

        var query = db.RiskAcceptances
            .AsNoTracking()
            .Include(a => a.AuthorizingManager)
            .Include(a => a.Findings)
            .AsQueryable();

        if (expiringWithinDays != null)
        {
            // Only Active acceptances can be "expiring": an already-expired or revoked one is not a
            // deadline anybody can still act on.
            var cutoff = DateTime.UtcNow.AddDays(expiringWithinDays.Value);
            query = query.Where(a => a.Status == RiskAcceptanceStatus.Active && a.ExpiresAt <= cutoff);
        }

        return await query.OrderBy(a => a.ExpiresAt).ToListAsync();
    }

    public async Task<RiskAcceptance> UpdateAcceptanceAsync(RiskAcceptance acceptance, int? userId)
    {
        ValidateAcceptance(acceptance);

        await using var db = DalService.GetContext();

        var existing = await db.RiskAcceptances.FirstOrDefaultAsync(a => a.Id == acceptance.Id);
        if (existing == null)
            throw new DataNotFoundException("risk_acceptances", acceptance.Id.ToString(),
                new Exception("Risk acceptance not found"));

        if (existing.Status != RiskAcceptanceStatus.Active)
            throw new InvalidStateTransitionException(existing.Status.ToString(), existing.Status.ToString(),
                $"A {existing.Status} risk acceptance cannot be edited. Create a new acceptance instead.");

        existing.Name = acceptance.Name;
        existing.BusinessJustification = acceptance.BusinessJustification;
        existing.AuthorizingManagerId = acceptance.AuthorizingManagerId;
        existing.CompensatingControls = acceptance.CompensatingControls;
        existing.ResidualScoreSnapshot = acceptance.ResidualScoreSnapshot;
        existing.EntityId = acceptance.EntityId;
        existing.UpdatedAt = DateTime.UtcNow;

        // Extending the expiry re-arms the pre-expiry warnings: the old "already warned" marker
        // refers to a deadline that no longer applies, and leaving it set would silently skip the
        // warnings for the new one.
        if (existing.ExpiresAt != acceptance.ExpiresAt)
        {
            existing.ExpiresAt = acceptance.ExpiresAt;
            existing.LastWarningDaysBefore = null;
        }

        await db.SaveChangesAsync();

        return existing;
    }

    public async Task<RiskAcceptance> AddFindingsToAcceptanceAsync(int acceptanceId,
        IReadOnlyList<int> findingIds, int? userId)
    {
        await using var db = DalService.GetContext();

        var acceptance = await db.RiskAcceptances
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(a => a.Id == acceptanceId);

        if (acceptance == null)
            throw new DataNotFoundException("risk_acceptances", acceptanceId.ToString(),
                new Exception("Risk acceptance not found"));

        if (acceptance.Status != RiskAcceptanceStatus.Active)
            throw new InvalidStateTransitionException(acceptance.Status.ToString(),
                RiskAcceptanceStatus.Active.ToString(),
                $"Findings cannot be added to a {acceptance.Status} risk acceptance.");

        await AttachFindingsAsync(db, acceptance, findingIds, userId, DateTime.UtcNow);

        return acceptance;
    }

    public async Task<RiskAcceptance> RevokeAcceptanceAsync(int acceptanceId, string reason, int? userId)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidParameterException(nameof(reason),
                "Revoking a risk acceptance requires a stated reason.");

        await using var db = DalService.GetContext();

        var acceptance = await db.RiskAcceptances
            .Include(a => a.Findings)
            .FirstOrDefaultAsync(a => a.Id == acceptanceId);

        if (acceptance == null)
            throw new DataNotFoundException("risk_acceptances", acceptanceId.ToString(),
                new Exception("Risk acceptance not found"));

        if (acceptance.Status != RiskAcceptanceStatus.Active)
            throw new InvalidStateTransitionException(acceptance.Status.ToString(),
                RiskAcceptanceStatus.Revoked.ToString(),
                $"Only an active risk acceptance can be revoked; this one is {acceptance.Status}.");

        var now = DateTime.UtcNow;
        acceptance.Status = RiskAcceptanceStatus.Revoked;
        acceptance.RevokedAt = now;
        acceptance.RevokedById = userId;
        acceptance.RevocationReason = reason;

        await ReactivateCoveredFindingsAsync(db, acceptance, userId, FindingStatusChangeSource.Manual,
            $"Risk acceptance '{acceptance.Name}' revoked: {reason}", now);

        await db.SaveChangesAsync();

        Logger.Information("Risk acceptance {Id} revoked by user {User}: {Reason}", acceptanceId, userId, reason);

        return acceptance;
    }

    public async Task<AcceptanceExpiryResult> ProcessExpiredAcceptancesAsync(DateTime nowUtc,
        int[]? warningThresholdDays = null)
    {
        var thresholds = (warningThresholdDays ?? DefaultWarningThresholds)
            .Where(t => t > 0)
            .OrderByDescending(t => t)
            .ToArray();

        var result = new AcceptanceExpiryResult();

        await using var db = DalService.GetContext();

        var active = await db.RiskAcceptances
            .Include(a => a.Findings)
            .Where(a => a.Status == RiskAcceptanceStatus.Active)
            .ToListAsync();

        foreach (var acceptance in active)
        {
            if (acceptance.ExpiresAt <= nowUtc)
            {
                acceptance.Status = RiskAcceptanceStatus.Expired;
                acceptance.UpdatedAt = nowUtc;

                var reactivated = await ReactivateCoveredFindingsAsync(db, acceptance, userId: null,
                    FindingStatusChangeSource.Job,
                    $"Risk acceptance '{acceptance.Name}' expired on {acceptance.ExpiresAt:yyyy-MM-dd}.", nowUtc);

                result.Expired.Add(acceptance);
                result.ReactivatedFindings[acceptance.Id] = reactivated;
                continue;
            }

            // Pre-expiry warnings. The stored marker is the smallest threshold already warned at, so
            // a re-run on the same day finds nothing new and a pass that skipped a day still warns
            // at the tighter threshold rather than staying silent.
            var daysLeft = (int)Math.Ceiling((acceptance.ExpiresAt - nowUtc).TotalDays);
            var crossed = thresholds.Where(t => daysLeft <= t).ToList();
            if (crossed.Count == 0) continue;

            var tightest = crossed.Min();
            if (acceptance.LastWarningDaysBefore != null && acceptance.LastWarningDaysBefore <= tightest) continue;

            acceptance.LastWarningDaysBefore = tightest;
            result.Warnings.Add((acceptance, tightest));
        }

        await db.SaveChangesAsync();

        if (result.Expired.Count > 0 || result.Warnings.Count > 0)
            Logger.Information(
                "Risk-acceptance expiry pass: {Expired} expired, {Reactivated} findings reactivated, {Warned} warnings",
                result.Expired.Count, result.ReactivatedFindings.Sum(r => r.Value.Count), result.Warnings.Count);

        return result;
    }

    // --- internals -------------------------------------------------------------------------

    /// <summary>
    /// Mutates the finding and queues its history row. Does not save — the caller decides the
    /// transaction boundary, which is what lets a bulk acceptance write one transaction rather than
    /// one per finding.
    /// </summary>
    private static void ApplyTransition(DAL.Context.NRDbContext db, Vulnerability finding, FindingStatus to,
        int? userId, FindingStatusChangeSource source, string? justification, int? duplicateOfId,
        int? riskAcceptanceId, DateTime at)
    {
        var from = finding.LifecycleStatus;

        finding.LifecycleStatus = to;

        if (to == FindingStatus.Duplicate) finding.DuplicateOfId = duplicateOfId;
        // Leaving the duplicate link set on a finding that is no longer a duplicate would have the
        // detail view keep showing a canonical finding it is not a duplicate of.
        else if (from == FindingStatus.Duplicate) finding.DuplicateOfId = null;

        db.FindingStatusHistories.Add(new FindingStatusHistory
        {
            VulnerabilityId = finding.Id,
            FromStatus = from,
            ToStatus = to,
            UserId = userId,
            Source = source,
            ChangedAt = at,
            Justification = justification,
            RiskAcceptanceId = riskAcceptanceId,
            DuplicateOfId = to == FindingStatus.Duplicate ? duplicateOfId : null
        });
    }

    private async Task AttachFindingsAsync(DAL.Context.NRDbContext db, RiskAcceptance acceptance,
        IReadOnlyList<int> findingIds, int? userId, DateTime now)
    {
        var distinct = findingIds.Distinct().ToList();

        var findings = await db.Vulnerabilities.Where(v => distinct.Contains(v.Id)).ToListAsync();

        var missing = distinct.Except(findings.Select(f => f.Id)).ToList();
        if (missing.Count > 0)
            throw new DataNotFoundException("vulnerabilities", string.Join(",", missing),
                new Exception("One or more findings to accept were not found"));

        var alreadyLinked = await db.RiskAcceptanceFindings
            .Where(l => l.RiskAcceptanceId == acceptance.Id)
            .Select(l => l.VulnerabilityId)
            .ToListAsync();

        var justification = $"Covered by risk acceptance '{acceptance.Name}' " +
                            $"(expires {acceptance.ExpiresAt:yyyy-MM-dd}).";

        foreach (var finding in findings)
        {
            if (!alreadyLinked.Contains(finding.Id))
                db.RiskAcceptanceFindings.Add(new RiskAcceptanceFinding
                {
                    RiskAcceptanceId = acceptance.Id,
                    VulnerabilityId = finding.Id,
                    CreatedAt = now
                });

            // A finding already accepted (by this or another acceptance) needs no transition, and
            // asking the state machine for RiskAccepted -> RiskAccepted would throw.
            if (finding.LifecycleStatus == FindingStatus.RiskAccepted) continue;

            // Validated per finding so an illegal one is reported as itself rather than as an
            // opaque failure of the whole batch.
            FindingStatusMachine.Validate(finding.LifecycleStatus, FindingStatus.RiskAccepted,
                justification, duplicateOfId: null, finding.Id);

            ApplyTransition(db, finding, FindingStatus.RiskAccepted, userId, FindingStatusChangeSource.Manual,
                justification, duplicateOfId: null, acceptance.Id, now);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Moves everything an acceptance covered back to Active. Returns the finding ids actually
    /// reactivated, which is what the notification needs — a finding somebody had already re-triaged
    /// is left where it is rather than being dragged back to Active.
    /// </summary>
    private static async Task<List<int>> ReactivateCoveredFindingsAsync(DAL.Context.NRDbContext db,
        RiskAcceptance acceptance, int? userId, FindingStatusChangeSource source, string justification,
        DateTime at)
    {
        var coveredIds = await db.RiskAcceptanceFindings
            .Where(l => l.RiskAcceptanceId == acceptance.Id)
            .Select(l => l.VulnerabilityId)
            .ToListAsync();

        if (coveredIds.Count == 0) return [];

        var findings = await db.Vulnerabilities
            .Where(v => coveredIds.Contains(v.Id) && v.LifecycleStatus == FindingStatus.RiskAccepted)
            .ToListAsync();

        foreach (var finding in findings)
            ApplyTransition(db, finding, FindingStatus.Active, userId, source, justification,
                duplicateOfId: null, acceptance.Id, at);

        return findings.Select(f => f.Id).ToList();
    }

    private static void ValidateAcceptance(RiskAcceptance acceptance)
    {
        if (string.IsNullOrWhiteSpace(acceptance.Name))
            throw new InvalidParameterException(nameof(acceptance.Name),
                "A risk acceptance requires a name.");

        if (string.IsNullOrWhiteSpace(acceptance.BusinessJustification))
            throw new InvalidParameterException(nameof(acceptance.BusinessJustification),
                "A risk acceptance requires a business justification.");

        if (acceptance.AuthorizingManagerId <= 0)
            throw new InvalidParameterException(nameof(acceptance.AuthorizingManagerId),
                "A risk acceptance requires an authorizing manager.");

        if (acceptance.ExpiresAt == default)
            throw new InvalidParameterException(nameof(acceptance.ExpiresAt),
                "A risk acceptance requires an expiry date.");

        // Rejected rather than silently accepted-and-immediately-expired: an acceptance created in
        // the past is a data-entry error, and letting it through means the expiry job reactivates
        // everything on its next run with no explanation anybody can act on.
        if (acceptance.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidParameterException(nameof(acceptance.ExpiresAt),
                "A risk acceptance must expire in the future.");
    }
}

using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Serilog;
using ServerServices.Importers;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Findings;

/// <summary>
/// SLA policy resolution, due-date arithmetic, and the notification digest (Track 3 milestone 3.4).
///
/// Two deliberate absences. There is no stored <c>days_overdue</c>: it is derived on read, so it
/// cannot drift and no nightly job is needed to keep it honest. And policy rows are never edited —
/// a change supersedes the old row — so last quarter's compliance figure stays reproducible.
/// </summary>
public class SlaService(ILogger logger, IDalService dalService) : ServiceBase(logger, dalService), ISlaService
{
    /// <summary>
    /// Days before the deadline at which a warning goes out. The spec's T-7/T-3/T-1; the breach
    /// itself is threshold 0 and always evaluated.
    /// </summary>
    public static readonly int[] DefaultApproachingThresholds = [7, 3, 1];

    public async Task<SlaConfiguration?> ResolveAsync(NormalizedSeverity severity, int? entityId, DateTime atUtc)
    {
        await using var db = DalService.GetContext();
        return await ResolveAsync(db, severity, entityId, atUtc);
    }

    private static async Task<SlaConfiguration?> ResolveAsync(DAL.Context.NRDbContext db,
        NormalizedSeverity severity, int? entityId, DateTime atUtc)
    {
        var severityValue = (int)severity;

        var candidates = await db.SlaConfigurations
            .AsNoTracking()
            .Where(c => c.Severity == severityValue
                        && c.EffectiveFrom <= atUtc
                        && (c.EffectiveTo == null || c.EffectiveTo > atUtc)
                        && (c.EntityId == null || c.EntityId == entityId))
            .ToListAsync();

        // An entity override wins over the global default; among equals the most recently effective
        // row wins, which is what makes two rows with the same effective date a harmless mistake
        // rather than a coin toss.
        return candidates
            .OrderByDescending(c => c.EntityId != null)
            .ThenByDescending(c => c.EffectiveFrom)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();
    }

    public async Task<DateTime?> ComputeDueDateAsync(NormalizedSeverity severity, int? entityId,
        DateTime firstSeenUtc)
    {
        // Resolved as of first-seen, not as of now: the finding's deadline is the one the policy
        // promised when it appeared, which is the whole reason the policy table is effective-dated.
        var policy = await ResolveAsync(severity, entityId, firstSeenUtc);
        return policy == null ? null : firstSeenUtc.AddDays(policy.MaxRemediationDays);
    }

    public async Task<List<SlaConfiguration>> GetConfigurationsAsync(bool includeSuperseded = false)
    {
        await using var db = DalService.GetContext();

        var query = db.SlaConfigurations.AsNoTracking().AsQueryable();
        if (!includeSuperseded) query = query.Where(c => c.EffectiveTo == null);

        return await query
            .OrderByDescending(c => c.Severity)
            .ThenBy(c => c.EntityId)
            .ThenByDescending(c => c.EffectiveFrom)
            .ToListAsync();
    }

    public async Task<SlaConfiguration> SetConfigurationAsync(SlaConfiguration configuration, int? userId)
    {
        if (configuration.MaxRemediationDays <= 0)
            throw new InvalidParameterException(nameof(configuration.MaxRemediationDays),
                "The remediation allowance must be at least one day.");

        if (configuration.MaxTriageDays <= 0)
            throw new InvalidParameterException(nameof(configuration.MaxTriageDays),
                "The triage allowance must be at least one day.");

        // A triage window longer than the remediation window describes a policy that cannot be met:
        // the finding would breach its remediation deadline while still inside its triage window.
        if (configuration.MaxTriageDays > configuration.MaxRemediationDays)
            throw new InvalidParameterException(nameof(configuration.MaxTriageDays),
                "The triage allowance cannot exceed the remediation allowance.");

        if (!Enum.IsDefined(typeof(NormalizedSeverity), configuration.Severity))
            throw new InvalidParameterException(nameof(configuration.Severity),
                $"Unknown severity {configuration.Severity}.");

        await using var db = DalService.GetContext();

        var now = DateTime.UtcNow;
        var effectiveFrom = configuration.EffectiveFrom == default ? now : configuration.EffectiveFrom;

        var current = await db.SlaConfigurations
            .Where(c => c.Severity == configuration.Severity
                        && c.EntityId == configuration.EntityId
                        && c.EffectiveTo == null)
            .ToListAsync();

        foreach (var superseded in current) superseded.EffectiveTo = effectiveFrom;

        var inserted = new SlaConfiguration
        {
            Severity = configuration.Severity,
            MaxTriageDays = configuration.MaxTriageDays,
            MaxRemediationDays = configuration.MaxRemediationDays,
            EntityId = configuration.EntityId,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            CreatedAt = now,
            CreatedById = userId
        };

        db.SlaConfigurations.Add(inserted);
        await db.SaveChangesAsync();

        Logger.Information(
            "SLA policy for severity {Severity} entity {Entity} set to triage {Triage}d remediation {Remediation}d effective {From} by user {User}",
            configuration.Severity, configuration.EntityId, configuration.MaxTriageDays,
            configuration.MaxRemediationDays, effectiveFrom, userId);

        return inserted;
    }

    public async Task<DateTime?> RecomputeDueDateAsync(int findingId, int? userId)
    {
        await using var db = DalService.GetContext();

        var finding = await db.Vulnerabilities.FirstOrDefaultAsync(v => v.Id == findingId);
        if (finding == null)
            throw new DataNotFoundException("vulnerabilities", findingId.ToString(),
                new Exception("Finding not found"));

        var severity = SeverityOf(finding);
        var policy = await ResolveAsync(db, severity, finding.EntityId, finding.FirstDetection);

        var previous = finding.SlaDueDate;
        finding.SlaDueDate = policy == null ? null : finding.FirstDetection.AddDays(policy.MaxRemediationDays);

        if (previous == finding.SlaDueDate) return finding.SlaDueDate;

        // The recompute is recorded on the finding's own timeline rather than only in the log: a
        // deadline that moved is exactly the kind of thing an auditor asks about, and "the severity
        // changed" is the answer.
        db.FindingStatusHistories.Add(new FindingStatusHistory
        {
            VulnerabilityId = finding.Id,
            FromStatus = finding.LifecycleStatus,
            ToStatus = finding.LifecycleStatus,
            UserId = userId,
            Source = FindingStatusChangeSource.Job,
            ChangedAt = DateTime.UtcNow,
            Justification = $"SLA due date recomputed for severity {severity}: " +
                            $"{Format(previous)} to {Format(finding.SlaDueDate)}."
        });

        await db.SaveChangesAsync();

        return finding.SlaDueDate;
    }

    public async Task<List<SlaDigest>> BuildNotificationDigestsAsync(DateTime nowUtc,
        int[]? approachingThresholdDays = null)
    {
        var thresholds = (approachingThresholdDays ?? DefaultApproachingThresholds)
            .Where(t => t > 0)
            .OrderBy(t => t)
            .ToArray();

        await using var db = DalService.GetContext();

        // Only open findings: a suppressed one has its clock paused (see FindingStatus.AccruesSla),
        // and notifying about a deadline nobody is allowed to work towards is pure noise.
        var horizon = nowUtc.AddDays(thresholds.Length == 0 ? 0 : thresholds.Max());

        var candidates = await db.Vulnerabilities
            .AsNoTracking()
            .Include(v => v.Host)
            .Include(v => v.Analyst)
            .Where(v => v.SlaDueDate != null
                        && v.SlaDueDate <= horizon
                        && (v.LifecycleStatus == FindingStatus.Active || v.LifecycleStatus == FindingStatus.Verified))
            .ToListAsync();

        if (candidates.Count == 0) return [];

        var candidateIds = candidates.Select(v => v.Id).ToList();

        // Already-notified pairs are excluded by (finding, threshold, due date). Including the due
        // date is what re-arms a warning when a severity change legitimately moves the deadline.
        var alreadyNotified = await db.SlaNotifications
            .AsNoTracking()
            .Where(n => candidateIds.Contains(n.VulnerabilityId))
            .Select(n => new { n.VulnerabilityId, n.ThresholdDays, n.DueDate })
            .ToListAsync();

        var notified = alreadyNotified
            .Select(n => (n.VulnerabilityId, n.ThresholdDays, n.DueDate))
            .ToHashSet();

        var digests = new Dictionary<int, SlaDigest>();
        SlaDigest? unowned = null;

        foreach (var finding in candidates)
        {
            var dueDate = finding.SlaDueDate!.Value;
            var threshold = ThresholdFor(dueDate, nowUtc, thresholds);
            if (threshold == null) continue;

            if (notified.Contains((finding.Id, threshold.Value, dueDate))) continue;

            var item = new SlaDigestItem
            {
                FindingId = finding.Id,
                Title = finding.Title,
                Severity = finding.Severity,
                DueDate = dueDate,
                ThresholdDays = threshold.Value,
                DaysOverdue = finding.DaysOverdue(nowUtc) ?? 0,
                AssetName = finding.Host?.HostName ?? finding.Host?.Ip
            };

            // The analyst is the finding's owner. Findings with none go into one fallback digest
            // rather than being dropped: an unowned breached critical is the one you most need to
            // hear about.
            if (finding.AnalystId == null)
            {
                unowned ??= new SlaDigest { RecipientUserId = null };
                unowned.Items.Add(item);
                continue;
            }

            if (!digests.TryGetValue(finding.AnalystId.Value, out var digest))
            {
                digest = new SlaDigest
                {
                    RecipientUserId = finding.AnalystId.Value,
                    RecipientEmail = finding.Analyst?.Email,
                    RecipientName = finding.Analyst?.Name
                };
                digests[finding.AnalystId.Value] = digest;
            }

            digest.Items.Add(item);
        }

        var result = digests.Values.ToList();
        if (unowned != null) result.Add(unowned);

        // Breached first, then nearest deadline: the order someone reads the mail in.
        foreach (var digest in result)
            digest.Items = digest.Items
                .OrderBy(i => i.ThresholdDays == 0 ? 0 : 1)
                .ThenBy(i => i.DueDate)
                .ToList();

        return result;
    }

    public async Task RecordNotificationsAsync(IEnumerable<SlaDigest> delivered, DateTime nowUtc)
    {
        await using var db = DalService.GetContext();

        foreach (var digest in delivered)
        foreach (var item in digest.Items)
            db.SlaNotifications.Add(new SlaNotification
            {
                VulnerabilityId = item.FindingId,
                ThresholdDays = item.ThresholdDays,
                DueDate = item.DueDate,
                NotifiedAt = nowUtc,
                RecipientUserId = digest.RecipientUserId
            });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // The unique index is the real guard. Losing the race means another pass already
            // recorded the same notification, which is the outcome we wanted anyway — so this is
            // logged rather than propagated to a caller who has already sent the mail.
            Logger.Warning("Some SLA notifications were already recorded by a concurrent pass: {Message}",
                ex.Message);
        }
    }

    public async Task<List<SlaComplianceBucket>> GetComplianceBySeverityAsync(DateTime nowUtc)
    {
        await using var db = DalService.GetContext();

        var open = await db.Vulnerabilities
            .AsNoTracking()
            .Where(v => v.SlaDueDate != null
                        && (v.LifecycleStatus == FindingStatus.Active || v.LifecycleStatus == FindingStatus.Verified))
            .Select(v => new { v.Severity, v.Cvss3BaseScore, v.CvssBaseScore, v.SlaDueDate })
            .ToListAsync();

        var buckets = Enum.GetValues<NormalizedSeverity>()
            .Where(s => s != NormalizedSeverity.None)
            .ToDictionary(s => s, s => new SlaComplianceBucket { Severity = s });

        foreach (var finding in open)
        {
            var severity = ParseSeverity(finding.Severity, finding.Cvss3BaseScore ?? finding.CvssBaseScore);
            if (severity == NormalizedSeverity.None) continue;

            var bucket = buckets[severity];
            bucket.Total++;
            if (finding.SlaDueDate!.Value.Date < nowUtc.Date) bucket.Breached++;
            else bucket.WithinSla++;
        }

        return buckets.Values.OrderByDescending(b => b.Severity).ToList();
    }

    /// <summary>
    /// Which alert a due date warrants right now: 0 for a passed deadline, otherwise the tightest
    /// crossed warning threshold, or null when the deadline is still further out than any of them.
    /// </summary>
    internal static int? ThresholdFor(DateTime dueDate, DateTime nowUtc, IReadOnlyList<int> thresholds)
    {
        if (dueDate.Date < nowUtc.Date) return 0;

        var daysLeft = (dueDate.Date - nowUtc.Date).Days;

        // Ascending, so the tightest crossed threshold wins: a finding due tomorrow reports T-1
        // rather than T-7, which is the more urgent and more useful message.
        foreach (var threshold in thresholds.OrderBy(t => t))
            if (daysLeft <= threshold)
                return threshold;

        return null;
    }

    /// <summary>
    /// The finding's severity as a normalized band. <c>vulnerabilities.severity</c> is free text
    /// carrying whatever the importing scanner wrote, so both the numeric Nessus scale and the CVSS
    /// words have to be understood, with the CVSS score as the last resort.
    /// </summary>
    internal static NormalizedSeverity ParseSeverity(string? raw, double? score)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (int.TryParse(raw.Trim(), out var numeric) &&
                Enum.IsDefined(typeof(NormalizedSeverity), numeric))
                return (NormalizedSeverity)numeric;

            if (Enum.TryParse<NormalizedSeverity>(raw.Trim(), ignoreCase: true, out var parsed))
                return parsed;

            if (SeverityMapper.CvssWords.TryGetValue(raw.Trim(), out var word))
                return word;
        }

        return SeverityMapper.FromCvssScore(score);
    }

    private static NormalizedSeverity SeverityOf(Vulnerability finding) =>
        ParseSeverity(finding.Severity, finding.Cvss3BaseScore ?? finding.CvssBaseScore);

    private static string Format(DateTime? value) => value?.ToString("yyyy-MM-dd") ?? "none";
}

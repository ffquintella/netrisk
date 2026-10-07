using System.Collections.Concurrent;
using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DAL.Auditing;

/// <summary>
/// Writes one <c>audit_logs</c> row per changed field on the risk-governance aggregate
/// (Track 8 milestone 8.4.1) and on hosts and their services (S38, T234).
///
/// This exists alongside — not instead of — the JSON <c>audit</c> table the context already writes.
/// That table answers forensic questions with a blob per save; it cannot answer "who lowered this
/// risk's impact from 4 to 2, and when" without parsing every row. This one can, with an index.
///
/// Two deliberate limits. The scope is an <see cref="AuditedTypes">allowlist</see>, because a global
/// trail over a vulnerability import would write millions of rows nobody reads. And the write is
/// best-effort: an auditing failure logs and lets the business save through, exactly as the existing
/// audit path does. An audit trail that can block a risk from being saved is a new outage source.
///
/// Hosts are in because "what changed on this host, and who or what changed it" is the question the
/// Hosts view's History tab answers, and the scanner, CMDB and posture imports that rewrite
/// criticality, owner, environment and risk score all save through here — so an import is
/// attributed without any import having to remember to log. Vulnerabilities stay out for the reason
/// above. Two host columns are ignored because every import pass stamps them whether or not anything
/// else changed: <see cref="Host.LastVerificationDate"/> and <see cref="Host.RiskScoreUpdatedAt"/>.
/// Recording them would bury the edits a person cares about under one row per host per scan; the
/// risk score itself is still recorded when it moves.
/// </summary>
public class GovernanceAuditInterceptor : SaveChangesInterceptor
{
    /// <summary>
    /// One stateless instance for the process. A fresh instance per context would multiply EF's
    /// internal service-provider cache entries, which is a documented way to leak memory.
    /// </summary>
    public static readonly GovernanceAuditInterceptor Instance = new();

    /// <summary>
    /// The governance aggregate, by CLR type name. Everything an auditor samples when testing
    /// ISO 27001 6.1.3 / SOC 2 CC3.x: the risk, its scores, its treatment, its approvals, the
    /// exceptions granted against it, the appetite those exceptions were measured against, and the
    /// business review decisions.
    /// </summary>
    public static readonly HashSet<string> AuditedTypes = new(StringComparer.Ordinal)
    {
        nameof(Risk),
        nameof(RiskScoring),
        nameof(Mitigation),
        nameof(MitigationTask),
        nameof(MgmtReview),
        nameof(RiskAcceptance),
        nameof(RiskAppetite),
        nameof(RiskReviewCampaignItem),
        nameof(EntityRiskReviewer),
        nameof(Host),
        nameof(HostsService),

        // Stage 9.1 (S41 §4.3): who linked a risk to which objective, process, service, data or
        // asset, and every promotion or demotion between Declared and Legacy. Recorded here; the
        // per-risk trail does not display it yet (S41 §3, negative scope).
        nameof(RiskChainLink),

        // Stage 9.3 (S43 §4.4): the continuity objectives that set process criticality and the flag 4
        // basis, the dependencies the cascade reads, and the restoration-test evidence — including its
        // voiding.
        nameof(BusinessImpactAnalysis),
        nameof(BiaDependency),
        nameof(RestorationTest),

        // Stage 9.4 (S45 §4.6): the ATT&CK techniques of a risk scenario, like its chain links. The
        // finding-side associations, the KEV catalogue and the EPSS readings are not audited: Vulnerability
        // is not either, and a 1 500-row catalogue would bury the trail.
        nameof(RiskAttackTechnique),

        // Stage 9.5 (S46 §4.10): every declaration, withdrawal and derived change of a mandatory flag —
        // including a derived flag reverting when its basis is lost, which must never be silent — and every
        // Phase 4 decision, the automatic Gate A ones included.
        nameof(RiskFlag),
        nameof(RiskDecision),

        // Stage 9.6 (S47 §4.10): the treatment option and monetary cost Gate C computes against, the
        // dependencies Gate D schedules by, and the target level — who set each, and every change.
        nameof(MitigationEconomics),
        nameof(MitigationDependency),
        nameof(RiskTarget)
    };

    /// <summary>
    /// The <c>settings</c> rows audited, by key (S43 §4.4, D18). <see cref="Setting"/> is deliberately
    /// <b>not</b> in <see cref="AuditedTypes"/>: that table also holds the backup password, and auditing
    /// the type would copy it into <c>audit_logs</c>. Only the keys listed here are recorded — the
    /// product parameters that change a governance result. T283 extends this list rather than adding a
    /// second mechanism.
    /// </summary>
    public static readonly HashSet<string> AuditedSettingNames = new(StringComparer.Ordinal)
    {
        ContinuitySettingKeys.RestorationTestValidityDays,
        ContinuitySettingKeys.UnverifiedThreatWeight
    };

    /// <summary>
    /// Fields never worth a row: the primary key (already the row's subject) and the churn columns
    /// every save touches. A trail whose signal is buried under `last_update` changes is not read.
    /// Matched by property name across every audited type; no two audited types share one of these
    /// names with a different meaning.
    /// </summary>
    private static readonly HashSet<string> IgnoredFields = new(StringComparer.Ordinal)
    {
        nameof(Risk.LastUpdate),
        nameof(RiskScoring.ResidualUpdatedAt),
        nameof(RiskScoring.QuantComputedAt),
        nameof(Host.LastVerificationDate),
        nameof(Host.RiskScoreUpdatedAt),
        "UpdatedAt"
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Collect(DbContext? context)
    {
        if (context is null) return;

        try
        {
            var userId = context is AuditableContext auditable && auditable.UserId > 0
                ? auditable.UserId
                : (int?)null;
            var actor = context is AuditableContext ac ? ac.AuditActor : AuditableContext.SystemActor;

            var correlationId = Guid.NewGuid().ToString("N")[..32];
            var occurredAt = DateTime.UtcNow;

            var rows = new List<AuditLog>();

            foreach (var entry in context.ChangeTracker.Entries().ToList())
            {
                if (entry.Entity is AuditLog) continue;
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;

                if (entry.Entity is Setting setting)
                {
                    if (AuditedSettingNames.Contains(setting.Name))
                        rows.AddRange(SettingRowsFor(entry, setting, userId, actor, correlationId, occurredAt));
                    continue;
                }

                if (!AuditedTypes.Contains(entry.Entity.GetType().Name)) continue;

                rows.AddRange(RowsFor(entry, userId, actor, correlationId, occurredAt));
            }

            // Added after the loop: adding to a DbSet mutates the change tracker, and mutating it
            // while enumerating is how this kind of interceptor usually breaks.
            foreach (var row in rows) context.Add(row);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error writing the governance audit trail");
        }
    }

    private static IEnumerable<AuditLog> RowsFor(EntityEntry entry, int? userId, string actor,
        string correlationId, DateTime occurredAt)
    {
        var type = entry.Entity.GetType().Name;
        var key = KeyOf(entry);

        switch (entry.State)
        {
            case EntityState.Added:
                // One summary row rather than one per property: on a create every field "changed",
                // and thirty rows saying so make the trail harder to read, not more complete. The
                // fields themselves are the record that was created.
                yield return New(type, key, string.Empty, null, Describe(entry), AuditLogAction.Create,
                    userId, actor, correlationId, occurredAt);
                break;

            case EntityState.Deleted:
                yield return New(type, key, string.Empty, Describe(entry), null, AuditLogAction.Delete,
                    userId, actor, correlationId, occurredAt);
                break;

            case EntityState.Modified:
                foreach (var property in entry.Properties)
                {
                    if (!property.IsModified) continue;
                    if (property.Metadata.IsPrimaryKey()) continue;
                    if (IgnoredFields.Contains(property.Metadata.Name)) continue;

                    var oldValue = Stringify(property.OriginalValue);
                    var newValue = Stringify(property.CurrentValue);
                    if (oldValue == newValue) continue;

                    yield return New(type, key, property.Metadata.Name, oldValue, newValue,
                        AuditLogAction.Update, userId, actor, correlationId, occurredAt);
                }

                break;
        }
    }

    /// <summary>
    /// A setting's key is its name, not an integer, so the row names the parameter in
    /// <see cref="AuditLog.Field"/> and leaves <see cref="AuditLog.EntityId"/> at 0 (S43 §4.4).
    /// </summary>
    private static IEnumerable<AuditLog> SettingRowsFor(EntityEntry entry, Setting setting, int? userId,
        string actor, string correlationId, DateTime occurredAt)
    {
        var value = entry.Property(nameof(Setting.Value));

        switch (entry.State)
        {
            case EntityState.Added:
                yield return New(nameof(Setting), 0, setting.Name, null, setting.Value, AuditLogAction.Create,
                    userId, actor, correlationId, occurredAt);
                break;

            case EntityState.Deleted:
                yield return New(nameof(Setting), 0, setting.Name, value.OriginalValue as string, null,
                    AuditLogAction.Delete, userId, actor, correlationId, occurredAt);
                break;

            case EntityState.Modified:
                var oldValue = value.OriginalValue as string;
                if (string.Equals(oldValue, setting.Value, StringComparison.Ordinal)) break;

                yield return New(nameof(Setting), 0, setting.Name, oldValue, setting.Value, AuditLogAction.Update,
                    userId, actor, correlationId, occurredAt);
                break;
        }
    }

    private static AuditLog New(string type, int key, string field, string? oldValue, string? newValue,
        AuditLogAction action, int? userId, string actor, string correlationId, DateTime occurredAt) => new()
    {
        EntityType = type,
        EntityId = key,
        Field = Truncate(field, 128) ?? string.Empty,
        OldValue = oldValue,
        NewValue = newValue,
        Action = action,
        UserId = userId,
        Actor = Truncate(actor, 64) ?? AuditableContext.SystemActor,
        OccurredAt = occurredAt,
        CorrelationId = correlationId
    };

    /// <summary>
    /// The single integer key of the audited row, or 0 when it is not yet assigned (an insert whose
    /// identity the database allocates). 0 is honest: the correlation id and the create row's value
    /// dump identify it, and back-filling would mean saving twice.
    /// </summary>
    private static int KeyOf(EntityEntry entry)
    {
        var keyProperty = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
        if (keyProperty?.CurrentValue is int id) return id;
        return 0;
    }

    /// <summary>A compact <c>field=value</c> dump for a create or delete, capped so one wide row
    /// cannot exceed a TEXT column.</summary>
    private static string Describe(EntityEntry entry)
    {
        var parts = entry.Properties
            .Where(p => !IgnoredFields.Contains(p.Metadata.Name))
            .Select(p => $"{p.Metadata.Name}={Stringify(
                entry.State == EntityState.Deleted ? p.OriginalValue : p.CurrentValue)}")
            .ToList();

        var joined = string.Join("; ", parts);
        return Truncate(joined, 60000)!;
    }

    private static string? Stringify(object? value) => value switch
    {
        null => null,
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        byte[] bytes => $"<{bytes.Length} bytes>",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Truncate(value.ToString(), 60000)
    };

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

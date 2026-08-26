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
/// (Track 8 milestone 8.4.1).
///
/// This exists alongside — not instead of — the JSON <c>audit</c> table the context already writes.
/// That table answers forensic questions with a blob per save; it cannot answer "who lowered this
/// risk's impact from 4 to 2, and when" without parsing every row. This one can, with an index.
///
/// Two deliberate limits. The scope is an <see cref="AuditedTypes">allowlist</see>, because a global
/// trail over a vulnerability import would write millions of rows nobody reads. And the write is
/// best-effort: an auditing failure logs and lets the business save through, exactly as the existing
/// audit path does. An audit trail that can block a risk from being saved is a new outage source.
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
        nameof(EntityRiskReviewer)
    };

    /// <summary>
    /// Fields never worth a row: the primary key (already the row's subject) and the churn columns
    /// every save touches. A trail whose signal is buried under `last_update` changes is not read.
    /// </summary>
    private static readonly HashSet<string> IgnoredFields = new(StringComparer.Ordinal)
    {
        nameof(Risk.LastUpdate),
        nameof(RiskScoring.ResidualUpdatedAt),
        nameof(RiskScoring.QuantComputedAt),
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
                if (!AuditedTypes.Contains(entry.Entity.GetType().Name)) continue;
                if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                    continue;

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

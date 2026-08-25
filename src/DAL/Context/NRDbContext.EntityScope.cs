using System.Linq;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace DAL.Context;

/// <summary>
/// Entity scoping for the multi-tenant model (Track 2 milestone 2.3.1/2.3.2).
///
/// The 2.3 spec's cardinal rule is that no code path may allow a cross-entity read or write, and
/// its first suggested mechanism is a global query predicate. That is what this does: the filters
/// are declared once on the model, so a service that forgets to scope its query still cannot see
/// another entity's rows. The previous arrangement — an <c>ApplyEntityScope</c> extension each
/// service was expected to remember — was applied in exactly one query, and the controller never
/// passed a principal to it, so nothing was actually filtered.
/// </summary>
public partial class NRDbContext
{
    /// <summary>
    /// Whose data this context may see. Set by the DAL service from the calling principal before
    /// the context is handed out; defaults to unrestricted so non-HTTP callers (jobs, console,
    /// migrations) behave as they always did.
    /// </summary>
    private EntityScope _entityScope = EntityScope.Unrestricted;

    public EntityScope EntityScope
    {
        get => _entityScope;
        set
        {
            _entityScope = value;
            // Mirrored onto plain fields below, which is what the filters actually read.
            ScopeIsUnrestricted = value.IsUnrestricted;
            ScopeEntityIds = value.EntityIds.ToArray();
        }
    }

    /// <summary>
    /// The scope flattened to primitives. The query filters read these rather than
    /// <see cref="EntityScope"/> itself: referencing the complex type inside a filter expression
    /// makes EF try to find a relational type mapping for it while building the model, which it
    /// cannot do, and the model build then fails with a null-reference deep inside the type
    /// mapping source. A bool and an int array are both things EF can parameterise happily.
    /// </summary>
    public bool ScopeIsUnrestricted { get; private set; } = true;

    public int[] ScopeEntityIds { get; private set; } = [];

    /// <summary>
    /// Declares the filter on every entity that carries an <c>entity_id</c>.
    ///
    /// Because these are query filters, they also govern <c>Find</c>/<c>FirstOrDefault</c>, so an
    /// out-of-scope row is simply not found — an update or delete aimed at another entity's record
    /// turns into a clean not-found rather than a silent cross-tenant write.
    /// </summary>
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        // Track 2 milestone 2.4.3 — persisted IRP task dependencies and the override record.
        ConfigureIrpDependencies(modelBuilder);

        // Track 3 (ASPM) — finding lifecycle, dedup, SLA and CI token schema.
        ConfigureAspm(modelBuilder);

        // Track 4 — notification channels, issue-tracker links, enterprise auth, posture providers.
        ConfigureIntegrations(modelBuilder);


        // The predicate is written inline rather than factored into a helper method: EF must be
        // able to translate the whole expression to SQL, and a method call is not translatable.
        modelBuilder.Entity<Risk>().HasQueryFilter(e =>
            ScopeIsUnrestricted || (e.EntityId != null && ScopeEntityIds.Contains(e.EntityId.Value)));

        modelBuilder.Entity<Vulnerability>().HasQueryFilter(e =>
            ScopeIsUnrestricted || (e.EntityId != null && ScopeEntityIds.Contains(e.EntityId.Value)));

        modelBuilder.Entity<Host>().HasQueryFilter(e =>
            ScopeIsUnrestricted || (e.EntityId != null && ScopeEntityIds.Contains(e.EntityId.Value)));

        modelBuilder.Entity<Incident>().HasQueryFilter(e =>
            ScopeIsUnrestricted || (e.EntityId != null && ScopeEntityIds.Contains(e.EntityId.Value)));

        modelBuilder.Entity<Assessment>().HasQueryFilter(e =>
            ScopeIsUnrestricted || (e.EntityId != null && ScopeEntityIds.Contains(e.EntityId.Value)));

        // Records that carry no entity_id of their own but belong to one that does. Without these
        // a scoped caller could read another entity's mitigations, management reviews, assessment
        // answers and so on — the parent row would be invisible while its children were not. EF
        // also warns about exactly this shape (a filtered principal on the required end of an
        // unfiltered dependent), and the filters below are what silence it correctly.
        //
        // Each reuses the already-filtered DbSet of its parent, so "my parent is visible to me" is
        // expressed once and stays true if the parent's own rule ever changes.
        modelBuilder.Entity<MgmtReview>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<Mitigation>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<HostsService>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Hosts.Any(h => h.Id == e.HostId));

        modelBuilder.Entity<AssessmentRun>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Assessments.Any(a => a.Id == e.AssessmentId));

        modelBuilder.Entity<AssessmentQuestion>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Assessments.Any(a => a.Id == e.AssessmentId));

        modelBuilder.Entity<AssessmentAnswer>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Assessments.Any(a => a.Id == e.AssessmentId));

        modelBuilder.Entity<FixRequest>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Vulnerabilities.Any(v => v.Id == e.VulnerabilityId));

        // A further step removed: these hang off a run, which hangs off the assessment that
        // carries the entity_id.
        modelBuilder.Entity<AssessmentRunAnswer>().HasQueryFilter(e =>
            ScopeIsUnrestricted || AssessmentRuns.Any(r => r.Id == e.AssessmentRunId));

        modelBuilder.Entity<AssessmentRunsAnswer>().HasQueryFilter(e =>
            ScopeIsUnrestricted || AssessmentRuns.Any(r => r.Id == e.RunId));

        // Track 3 (ASPM). Risk acceptances and scan imports carry their own entity_id; the rest
        // inherit visibility from the finding or acceptance they hang off. Without these a scoped
        // caller could read another entity's suppression justifications and scan history — the
        // audit trail is exactly the material that must not leak across tenants.
        modelBuilder.Entity<RiskAcceptance>().HasQueryFilter(e =>
            ScopeIsUnrestricted || (e.EntityId != null && ScopeEntityIds.Contains(e.EntityId.Value)));

        modelBuilder.Entity<ScanImport>().HasQueryFilter(e =>
            ScopeIsUnrestricted || (e.EntityId != null && ScopeEntityIds.Contains(e.EntityId.Value)));

        modelBuilder.Entity<FindingStatusHistory>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Vulnerabilities.Any(v => v.Id == e.VulnerabilityId));

        modelBuilder.Entity<SlaNotification>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Vulnerabilities.Any(v => v.Id == e.VulnerabilityId));

        modelBuilder.Entity<RiskAcceptanceFinding>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Vulnerabilities.Any(v => v.Id == e.VulnerabilityId));

        // An SLA policy row is either the global default (entity_id null, visible to everyone
        // because every finding is measured against it) or an entity override.
        modelBuilder.Entity<SlaConfiguration>().HasQueryFilter(e =>
            ScopeIsUnrestricted || e.EntityId == null || ScopeEntityIds.Contains(e.EntityId.Value));
    }
}

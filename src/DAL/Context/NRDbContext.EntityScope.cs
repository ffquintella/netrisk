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

        // Track 4 milestone 4.6 — the Jira Service Management and Assets facet. After
        // ConfigureIntegrations, because it widens finding_issue_links, which that configures.
        ConfigureJira(modelBuilder);

        // Track 8 — risk acceptance on risks, residual and quantitative scoring, appetite,
        // counter-signature, the field-level audit trail, mitigation tasks and pending-risk triage.
        ConfigureGovernance(modelBuilder);
        ApplyGovernanceQueryFilters(modelBuilder);

        // Track 8 milestone 8.6 — the business review portal.
        ConfigureReviewPortal(modelBuilder);
        ApplyReviewPortalQueryFilters(modelBuilder);

        // External secret vaults — the connections a stored SecretReference resolves through.
        ConfigureSecretVaults(modelBuilder);

        // The schema the deferred Track 7 findings needed (NR-2026-017 / -028 / -008b).
        ConfigureDeferredSecuritySchema(modelBuilder);
        ApplyDeferredSecurityQueryFilters(modelBuilder);

        // Track 9 Stage 9.1 — the risk linkage chain (S41). Its scope filter is below, with the
        // other derived ones.
        ConfigureRiskChain(modelBuilder);

        // Track 9 Stage 9.2 — structured scenario, evidence confidence, standalone hypotheses and
        // near misses (S42). Its one new filter, on pending_risks, is below with the others.
        ConfigureRiskScenario(modelBuilder);

        // Track 9 Stage 9.3 — business impact analysis, continuity dependencies and restoration tests
        // (S43). No scope filter: processes and services carry no scope column (S43 §11, D12).
        ConfigureContinuity(modelBuilder);

        // GitHub #79 — whether a vulnerability is in a server or in an application. A column on
        // vulnerabilities, so the vulnerability scope filter below already covers it.
        ConfigureVulnerabilityClassification(modelBuilder);

        // GitHub #80 — a comment and evidence files on each answer of an assessment run (S44). The
        // files carry their own entity_id and the answers inherit the run's scope, so no new filter.
        ConfigureAssessmentEvidence(modelBuilder);

        // Track 9 Stage 9.4 — exploitation signals (S45). The EPSS columns are on vulnerabilities, so
        // the vulnerability filter covers them; the catalogue tables are public per-CVE data; the two
        // ATT&CK association tables get their filters below, through their parent.
        ConfigureExploitationSignals(modelBuilder);

        // Track 9 Stage 9.5 — the eleven mandatory flags and Gate A (S46). Both tables hang off the
        // risk and get their filters below, through it.
        ConfigureRiskFlags(modelBuilder);

        // Track 9 Stage 9.6 — treatment economics (S47). The economics and the dependencies hang off the
        // mitigation, the target off the risk; their filters are below.
        ConfigureTreatmentEconomics(modelBuilder);

        // Track 9 Stage 9.7 — tail statistics and portfolio (S48). Components and statistics hang off the risk,
        // a correlation off both its risks, the tail limits off the appetite; their filters are below.
        ConfigureTailRisk(modelBuilder);

        // Track 9 Stage 9.8 — KRIs and reassessment triggers (S49). The KRI carries its own entity; readings hang off
        // it, links and triggers off the risk, an event off its triggers or its KRI; their filters are below.
        ConfigureMonitoring(modelBuilder);

        // Track 9 Stage 9.9 — archival, backtesting and the risk committee (S50). Archives and committee decisions hang
        // off the risk, backtests off the incident, a committee carries its own entity; their filters are below.
        ConfigureDecisionCycle(modelBuilder);

        // Track 9 Stage 9.10 — the third-party register (S51). The third party carries its own entity; everything it
        // declares hangs off it, answers off their assessment, components off their SBOM; their filters are below.
        ConfigureThirdParties(modelBuilder);

        // Track 9 Stage 9.11 — the LGPD data catalogue (S52). The catalogue, the RIPDs and the requirements describe the
        // entity map, which has no scope, and are the organization's (S52 D10); only the risk links carry the risk's scope,
        // and their filter is below.
        ConfigureDataCatalogue(modelBuilder);

        // Track 9 Stage 9.12 — AI governance (S53). The model carries its own entity, like a third party; its data links,
        // readings and overrides follow it, a risk link follows the risk and the model; their filters are below.
        ConfigureAiGovernance(modelBuilder);

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

        // Stage 9.2 (S42 §4.2): the hypothesis queue. It was the one register with no scope at all —
        // harmless while only legacy-migrated rows lived there, not once POST /Risks/Pending let any
        // submitter write free text into it. Same rule as a risk, so triage reads, promotes and
        // dismisses only what the caller could see as a risk.
        modelBuilder.Entity<PendingRisk>().HasQueryFilter(e =>
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

        // Stage 9.1 (S41 §4.3). Two parents, both scoped: the risk, and — for a host link — the host.
        // A scoped caller sees neither another entity's risk links nor the link from one of their own
        // risks to a host they cannot see; the chain must not be a side door to either. Every chain
        // query relies on this, and none of them calls IgnoreQueryFilters.
        modelBuilder.Entity<RiskChainLink>().HasQueryFilter(e =>
            ScopeIsUnrestricted
            || (Risks.Any(r => r.Id == e.RiskId) && (e.HostId == null || Hosts.Any(h => h.Id == e.HostId))));

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

        // Stage 9.4 (S45 §4.5): the ATT&CK techniques of a finding and of a risk scenario are visible
        // exactly when their parent is. Every technique query relies on this; none calls
        // IgnoreQueryFilters.
        modelBuilder.Entity<VulnerabilityAttackTechnique>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Vulnerabilities.Any(v => v.Id == e.VulnerabilityId));

        modelBuilder.Entity<RiskAttackTechnique>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        // Stage 9.5 (S46 §4.2–4.3): a risk's flags and decisions are visible exactly when the risk is.
        // The derivation reads through an unscoped context on purpose (S46 D9); every read a caller
        // makes relies on these.
        modelBuilder.Entity<RiskFlag>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<RiskDecision>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        // Stage 9.6 (S47 §4.10): a mitigation's economics and dependencies are visible exactly when the
        // dependent mitigation is (which is when its risk is); a risk's target exactly when the risk is. A
        // dependency may name a prerequisite the caller cannot see — the service never lets that caller add
        // one, and never deletes one it cannot see.
        modelBuilder.Entity<MitigationEconomics>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Mitigations.Any(m => m.Id == e.MitigationId));

        modelBuilder.Entity<MitigationDependency>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Mitigations.Any(m => m.Id == e.MitigationId));

        modelBuilder.Entity<RiskTarget>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        // Stage 9.7 (S48 §4.9): a risk's loss components and tail statistics are visible exactly when the risk is,
        // a statistic's components exactly when the statistic is. A correlation needs BOTH risks visible — one
        // visible end would otherwise reveal that a hidden risk exists and how it moves. The tail limits follow
        // the appetite they belong to. The validity check of a new correlation reads unscoped on purpose (S48 §4.5).
        modelBuilder.Entity<RiskLossComponent>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<RiskTailStatistics>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<RiskTailComponent>().HasQueryFilter(e =>
            ScopeIsUnrestricted || RiskTailStatistics.Any(s => s.Id == e.TailStatisticsId));

        modelBuilder.Entity<RiskCorrelation>().HasQueryFilter(e =>
            ScopeIsUnrestricted
            || (Risks.Any(r => r.Id == e.RiskAId) && Risks.Any(r => r.Id == e.RiskBId)));

        modelBuilder.Entity<RiskAppetiteTailLimit>().HasQueryFilter(e =>
            ScopeIsUnrestricted || RiskAppetites.Any(a => a.Id == e.AppetiteId));

        // Stage 9.8 (S49 §4.11). A KRI with no entity is the organization's and every reader of the register sees it
        // (S49 R4) — unlike a risk with no entity, which is unassigned. Writing one is still refused to a scoped caller by
        // the write guard, because Kri is IEntityScoped and a null entity is outside every scope. Readings follow their
        // KRI; links and triggers follow their risk. An event is visible through a trigger the caller can see, or through
        // its KRI. Gate B reads a risk's KRIs unscoped on purpose, after checking the risk is visible (S49 D6).
        modelBuilder.Entity<Kri>().HasQueryFilter(e =>
            ScopeIsUnrestricted || e.EntityId == null || ScopeEntityIds.Contains(e.EntityId.Value));

        modelBuilder.Entity<KriReading>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Kris.Any(k => k.Id == e.KriId));

        modelBuilder.Entity<KriRisk>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<RiskReassessmentTrigger>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<ReassessmentEvent>().HasQueryFilter(e =>
            ScopeIsUnrestricted
            || RiskReassessmentTriggers.Any(t => t.EventId == e.Id)
            || (e.KriId != null && Kris.Any(k => k.Id == e.KriId)));

        // Stage 9.9 (S50 §4.8). An archive, its conditions and reviews follow the risk. A backtest follows its
        // incident; its links follow the backtest *and* the risk, so a reader sees only the risks they may see — the
        // outcome itself is computed unscoped once the incident is known visible, so it never depends on who asks
        // (S50 D7). A committee with no entity is the organization's and every reader sees it, like a KRI; members
        // follow the committee, decisions follow the risk, votes follow the decision.
        modelBuilder.Entity<RiskArchive>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<RiskArchiveCondition>().HasQueryFilter(e =>
            ScopeIsUnrestricted || RiskArchives.Any(a => a.Id == e.ArchiveId));

        modelBuilder.Entity<RiskArchiveReview>().HasQueryFilter(e =>
            ScopeIsUnrestricted || RiskArchives.Any(a => a.Id == e.ArchiveId));

        modelBuilder.Entity<IncidentBacktest>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Incidents.Any(i => i.Id == e.IncidentId));

        modelBuilder.Entity<IncidentBacktestRisk>().HasQueryFilter(e =>
            ScopeIsUnrestricted
            || (IncidentBacktests.Any(b => b.Id == e.BacktestId) && Risks.Any(r => r.Id == e.RiskId)));

        modelBuilder.Entity<RiskCommittee>().HasQueryFilter(e =>
            ScopeIsUnrestricted || e.EntityId == null || ScopeEntityIds.Contains(e.EntityId.Value));

        modelBuilder.Entity<RiskCommitteeMember>().HasQueryFilter(e =>
            ScopeIsUnrestricted || RiskCommittees.Any(c => c.Id == e.CommitteeId));

        modelBuilder.Entity<RiskCommitteeDecision>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        modelBuilder.Entity<RiskCommitteeVote>().HasQueryFilter(e =>
            ScopeIsUnrestricted || RiskCommitteeDecisions.Any(d => d.Id == e.DecisionId));

        // Stage 9.10 (S51 §4.10). A third party with no entity is the organization's and every reader sees it, like a KRI or
        // a committee; writing one is refused to a scoped caller by the write guard, because ThirdParty is IEntityScoped.
        // Everything it declares follows it; answers follow their assessment, components their SBOM. The concentration is
        // computed unscoped once the reader's third parties are known (S51 D8), so it never depends on who asks.
        modelBuilder.Entity<ThirdParty>().HasQueryFilter(e =>
            ScopeIsUnrestricted || e.EntityId == null || ScopeEntityIds.Contains(e.EntityId.Value));

        modelBuilder.Entity<ThirdPartyLink>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ThirdParties.Any(t => t.Id == e.ThirdPartyId));

        modelBuilder.Entity<ThirdPartySubprocessor>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ThirdParties.Any(t => t.Id == e.ThirdPartyId));

        modelBuilder.Entity<ThirdPartyDataLocation>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ThirdParties.Any(t => t.Id == e.ThirdPartyId));

        modelBuilder.Entity<ThirdPartyAssessment>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ThirdParties.Any(t => t.Id == e.ThirdPartyId));

        modelBuilder.Entity<ThirdPartyAssessmentAnswer>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ThirdPartyAssessments.Any(a => a.Id == e.AssessmentId));

        modelBuilder.Entity<ThirdPartySbom>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ThirdParties.Any(t => t.Id == e.ThirdPartyId));

        modelBuilder.Entity<ThirdPartySbomComponent>().HasQueryFilter(e =>
            ScopeIsUnrestricted || ThirdPartySboms.Any(s => s.Id == e.SbomId));

        // Stage 9.11 (S52 §4.10). A risk's legal requirements are visible exactly when the risk is. The catalogue, the RIPDs
        // and the requirements themselves are the organization's — every write to them needs global scope, checked by the
        // service — and the in-use count of a requirement is taken unscoped, so a link the caller cannot see still counts.
        modelBuilder.Entity<RiskLegalRequirement>().HasQueryFilter(e =>
            ScopeIsUnrestricted || Risks.Any(r => r.Id == e.RiskId));

        // Stage 9.12 (S53 §4.8). A model with no entity is the organization's and every reader sees it, like a third party;
        // writing one is refused to a scoped caller by the write guard, because AiModel is IEntityScoped, and the service
        // checks the model's own entity before any write on what hangs off it. Data links, readings and overrides follow the
        // model. A risk link needs BOTH the risk and the model visible — one visible end must not reveal the other. Flag 11
        // is derived through an unscoped context on purpose (S46 D9), so it never depends on who asks.
        modelBuilder.Entity<AiModel>().HasQueryFilter(e =>
            ScopeIsUnrestricted || e.EntityId == null || ScopeEntityIds.Contains(e.EntityId.Value));

        modelBuilder.Entity<AiModelDataLink>().HasQueryFilter(e =>
            ScopeIsUnrestricted || AiModels.Any(m => m.Id == e.ModelId));

        modelBuilder.Entity<AiModelMetricReading>().HasQueryFilter(e =>
            ScopeIsUnrestricted || AiModels.Any(m => m.Id == e.ModelId));

        modelBuilder.Entity<AiModelOverride>().HasQueryFilter(e =>
            ScopeIsUnrestricted || AiModels.Any(m => m.Id == e.ModelId));

        modelBuilder.Entity<AiModelRisk>().HasQueryFilter(e =>
            ScopeIsUnrestricted || (Risks.Any(r => r.Id == e.RiskId) && AiModels.Any(m => m.Id == e.ModelId)));

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

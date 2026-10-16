using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using Model.Status;
using ServerServices.Attribution;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using ServerServices.RiskIndex.Signals;

namespace ServerServices.RiskIndex;

/// <summary>State carried from earlier snapshots that today's rows depend on (S39 §6.3 GOV.G latch, §9.2 frozen closures).</summary>
public sealed record RiskIndexCollectionOptions
{
    /// <summary>Incident id → the closure date the first snapshot that saw it closed froze (D-15 interim).</summary>
    public IReadOnlyDictionary<int, DateTime> IncidentClosureDates { get; init; } = new Dictionary<int, DateTime>();

    /// <summary>Risk id → the first day of GOV.G's current run of lit days.</summary>
    public IReadOnlyDictionary<int, DateTime> DivergenceSince { get; init; } = new Dictionary<int, DateTime>();
}

/// <summary>Every object row of one day, and the facts the job carries forward or publishes beside them.</summary>
public sealed record RiskIndexObjectSet
{
    public IReadOnlyList<RiskIndexObjectRow> Rows { get; init; } = [];

    /// <summary>The closure date each closed incident in the window was read with — persist to freeze it (S39 §9.2).</summary>
    public IReadOnlyDictionary<int, DateTime> IncidentClosureDates { get; init; } = new Dictionary<int, DateTime>();

    /// <summary>Some incident was created in <c>janela_intake_incidentes</c>: without intake AME is unavailable, not zero.</summary>
    public bool IncidentIntakeActive { get; init; }

    /// <summary>Whether some source of each category is enabled by the profile (AME: and intake active).</summary>
    public IReadOnlyDictionary<RiskIndexCategory, bool> SourceEnabled { get; init; } = new Dictionary<RiskIndexCategory, bool>();

    /// <summary>Findings whose legacy status is closing while the lifecycle holds them open (D-04 quality metric).</summary>
    public int LegacyLifecycleDisagreements { get; init; }

    public EntityMap Map { get; init; } = EntityMap.Build([], []);
}

/// <summary>
/// Builds the object rows of every category from the database (S39 §2.1, §3, §5, §6, §8; T244, T245): scenarios in
/// REG, GOV and CTL.P; hosts, SecurityScorecard domains and host-less applications in EXP and GOV; target entities in
/// AME and CTL.A. Reads through the given context, so under its entity scope; a job runs it unrestricted (T237).
/// </summary>
public static partial class RiskIndexObjectCollector
{
    internal sealed class Run
    {
        public required RiskIndexParameters P;
        public required RiskIndexMode Mode;
        public required DateTime Now;
        public required EntityMap Map;
        public required HashSet<string> Enabled;
        public required RiskIndexCollectionOptions Options;
        public readonly List<RiskIndexObjectRow> Rows = [];

        public bool On(string source) => Enabled.Contains(source);
        public double W(string source) => P.FreshnessDays.TryGetValue(source, out var w) ? w : 30;
        public double M(int crit) => IcrMath.Modulator(crit, P.MA);
        public double U(string factor) => GovernanceSignals.Ceiling(P, factor);
    }

    public static async Task<RiskIndexObjectSet> CollectAsync(NRDbContext db, RiskIndexParameters p, RiskIndexMode mode,
        DateTime now, RiskIndexCollectionOptions? options = null, CancellationToken ct = default)
    {
        var map = await ReadEntityMapAsync(db, ct);
        var run = new Run
        {
            P = p, Mode = mode, Now = now, Map = map, Enabled = p.EnabledSources.ToHashSet(StringComparer.Ordinal),
            Options = options ?? new RiskIndexCollectionOptions()
        };

        var facts = await RegisterFactsReader.ReadAsync(db, p, now, ct);
        var attribution = await ScenarioAttribution.EntitiesByRiskAsync(db, facts.Keys.ToList(), ct);

        var exposure = await CollectExposureAsync(db, run, ct);
        CollectScenarios(run, facts, attribution, exposure.FreshHostExposure);
        var events = await CollectEventsAsync(db, run, ct);
        await CollectAssessmentsAsync(db, run, ct);

        var enabled = new Dictionary<RiskIndexCategory, bool>
        {
            [RiskIndexCategory.Reg] = run.On(RiskIndexSources.RegResidual),
            [RiskIndexCategory.Exp] = new[]
            {
                RiskIndexSources.V1Device, RiskIndexSources.V1Cve, RiskIndexSources.SscTotal, RiskIndexSources.TenableNessus,
                RiskIndexSources.TenableVm, RiskIndexSources.TenableAes, RiskIndexSources.Importers
            }.Any(run.On),
            [RiskIndexCategory.Ame] = (run.On(RiskIndexSources.Incidents) || run.On(RiskIndexSources.NearMisses)) &&
                                      events.IntakeActive,
            [RiskIndexCategory.Ctl] = run.On(RiskIndexSources.Assessments) || run.On(RiskIndexSources.IncidentResponsePlans),
            [RiskIndexCategory.Gov] = run.On(RiskIndexSources.RegResidual) || exposure.AnySlaSource,
            [RiskIndexCategory.Ter] = false
        };

        return new RiskIndexObjectSet
        {
            Rows = run.Rows,
            IncidentClosureDates = events.ClosureDates,
            IncidentIntakeActive = events.IntakeActive,
            SourceEnabled = enabled,
            LegacyLifecycleDisagreements = exposure.Disagreements,
            Map = map
        };
    }

    /// <summary>The entity map and the EAV properties the index reads (links and risk context).</summary>
    public static async Task<EntityMap> ReadEntityMapAsync(NRDbContext db, CancellationToken ct = default)
    {
        var entities = await db.Entities.AsNoTracking()
            .Select(e => new EntityMapRow(e.Id, e.DefinitionName, e.Parent)).ToListAsync(ct);

        string[] keys =
        [
            RiskIndexEntityTypes.ProcessUnitsProperty, RiskIndexEntityTypes.ProcessApplicationsProperty,
            Model.Entities.RiskContextProperties.Criticality, Model.Entities.RiskContextProperties.InternetFacing,
            Model.Entities.RiskContextProperties.SecurityClassification, "environment"
        ];
        var properties = await db.EntitiesProperties.AsNoTracking().Where(p => keys.Contains(p.Type))
            .Select(p => new EntityPropertyRow(p.Entity, p.Type, p.Value)).ToListAsync(ct);

        return EntityMap.Build(entities, properties);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Criticality of an entity-backed object (S39 §5.1–§5.2): declared EAV criticality, else the default; plus the
    // classification step for an application whose level the profile marks sensitive.
    // ------------------------------------------------------------------------------------------------------------
    internal static (int Criticality, string Source) EntityCriticality(Run run, int? entityId)
    {
        if (entityId is not { } id) return (run.P.DefaultCriticality, "default");

        var declared = run.On(RiskIndexSources.EntityCriticality) ? run.Map.Criticality(id) : null;
        var crit = declared ?? run.P.DefaultCriticality;
        var source = declared is null ? "default" : "entity";

        if (run.On(RiskIndexSources.EntityClassification) &&
            run.Map.DefinitionOf(id) == RiskIndexEntityTypes.Application &&
            run.Map.SecurityClassification(id) is { } level && run.P.SensitiveLevels.Contains(level))
            crit = Math.Min(5, crit + Math.Max(0, run.P.ClassificationStep));

        return (Math.Clamp(crit, 1, 5), source);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Scenarios: REG (σ_r with the review credit), GOV (σ_ref × MRA of the governance factors) and CTL.P (response
    // readiness on every open scenario). Weight w_r = m(impact)·m_rank; the rank moves only the weight (S39 §6.5).
    // ------------------------------------------------------------------------------------------------------------
    private static void CollectScenarios(Run run, IReadOnlyDictionary<int, ScenarioFacts> facts,
        IReadOnlyDictionary<int, IReadOnlyList<int>> attribution, IReadOnlyDictionary<int, double> freshHostExposure)
    {
        var p = run.P;
        foreach (var (id, f) in facts.OrderBy(kv => kv.Key))
        {
            var key = new IcrObjectKey(RiskIndexObjectKind.Scenario, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var credit = ReviewCredit.Compute(f, p, run.Now);

            var impact = run.On(RiskIndexSources.RegImpact) ? f.ImpactLevel : null;
            var rank = run.On(RiskIndexSources.RegRank) ? f.Rank : null;
            var weight = run.M(impact ?? 3) * IcrAnchors.RankMultiplier(p, rank?.Rank, rank?.RankedItems ?? 0);

            // GOV.G: the linked hosts' fresh-only exposure against the declared severity, latched until a qualified review.
            var exposure = f.LinkedHostIds.Select(h => freshHostExposure.GetValueOrDefault(h)).DefaultIfEmpty(0).Max();
            var litToday = exposure - credit.SigmaRef > p.DivergenceDelta;
            DateTime? since = run.Options.DivergenceSince.TryGetValue(id, out var previous) ? previous : null;
            if (since is { } first && credit.ReferenceDate > first) since = null; // a qualified review released the latch
            var lit = litToday || since is not null;
            var divergenceSince = lit ? since ?? run.Now.Date : (DateTime?)null;

            var governance = GovernanceSignals.Compute(f, credit, p, run.Now, lit);
            var attributed = attribution.GetValueOrDefault(id) ?? [];
            var inputs = new RiskIndexScenarioInputs
            {
                ImpactLevel = f.ImpactLevel, Rank = rank?.Rank, RankedItems = rank?.RankedItems, Residual = f.Residual,
                Inherent = f.Inherent, CadenceDays = credit.CadenceDays, DelayDays = credit.DelayDays,
                Credit = credit.Credit, EffectiveResidual = credit.EffectiveResidual, SigmaRef = credit.SigmaRef,
                ReferenceDate = credit.ReferenceDate, Due = credit.Due, TreatmentAssessed = credit.TreatmentAssessed,
                ResidualAboveInherent = credit.ResidualAboveInherent, AppetiteConfigured = governance.AppetiteConfigured,
                AboveCeiling = governance.AboveCeiling, AcceptedAboveCeiling = governance.AcceptedAboveCeiling,
                AcceptanceExpiringSoon = governance.AcceptanceExpiringSoon, DivergenceSince = divergenceSince
            };

            RiskIndexObjectRow Row(RiskIndexCategory category, double r, IReadOnlyList<RiskIndexFactorReading> factors,
                IReadOnlyList<RiskIndexSignalReading> signals, RiskIndexObjectState? state = null, double coverage = 1) => new()
            {
                Key = key, Category = category, Population = RiskIndexPopulation.Scenarios, R = IcrMath.Clamp01(r),
                Weight = weight, Criticality = impact ?? 3, CriticalitySource = impact is null ? "default" : "impact",
                State = state ?? (r > 0 ? RiskIndexObjectState.Assessed : RiskIndexObjectState.AssessedClean),
                ScopeEntityId = f.EntityId, Attribution = attributed, Factors = factors, Signals = signals,
                ScenarioInputs = inputs, SourceMask = RiskIndexSourceBits.Bit(RiskIndexSources.RegResidual),
                CoverageEffect = coverage, RMax = 1
            };

            if (run.On(RiskIndexSources.RegResidual))
            {
                run.Rows.Add(Row(RiskIndexCategory.Reg, credit.SigmaR,
                    [new RiskIndexFactorReading { Factor = "REG.R", Type = RiskIndexFactorType.S, X = credit.SigmaR, Source = RiskIndexSources.RegResidual }],
                    [], credit.TreatmentAssessed ? null : RiskIndexObjectState.NotAssessed));

                run.Rows.Add(Row(RiskIndexCategory.Gov, governance.R,
                    governance.Factors.Select(kv => new RiskIndexFactorReading
                    {
                        Factor = kv.Key, Type = RiskIndexFactorType.S, X = kv.Value, Ceiling = run.U(kv.Key)
                    }).ToList(),
                    governance.Signals.Select(g => new RiskIndexSignalReading(g.Factor, g.Id, g.Value, [])).ToList(),
                    coverage: GovernanceSignals.CoverageEffect(p, governance.AppetiteConfigured)));
            }

            if (run.On(RiskIndexSources.IncidentResponsePlans))
            {
                var irp = IcrAnchors.Irp(p, f.ApprovedResponsePlan, f.ResponsePlanLastTest, run.Now);
                var ramp = IcrAnchors.IrpRamp(p, credit.SigmaRef);
                var u = run.U(RiskIndexFactors.CtlResponse);
                run.Rows.Add(Row(RiskIndexCategory.Ctl, credit.SigmaRef * u * irp * ramp,
                    [new RiskIndexFactorReading { Factor = RiskIndexFactors.CtlResponse, Type = RiskIndexFactorType.S, X = irp * ramp, Ceiling = u, ReadAt = f.ResponsePlanLastTest, Source = RiskIndexSources.IncidentResponsePlans }],
                    irp * ramp > 0 ? [new RiskIndexSignalReading(RiskIndexFactors.CtlResponse, "IRP", irp, ramp < 1 ? ["ramp"] : [])] : []) with
                {
                    SourceMask = RiskIndexSourceBits.Bit(RiskIndexSources.IncidentResponsePlans)
                });
            }
        }
    }

    /// <summary>Whether a finding's legacy status closes it (the D-04 interim clause of S39 §3.3).</summary>
    internal static bool LegacyClosed(int legacyStatus) => LegacyFindingStatus.ClosingTarget(legacyStatus) is not null;
}

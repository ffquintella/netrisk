using System.Globalization;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Monitoring;
using Model.RiskIndex;
using Model.TailRisk;
using Model.ThirdParties;
using ServerServices.Governance;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using Tools.Monitoring;
using Tools.ThirdParties;

namespace ServerServices.RiskIndex;

/// <summary>
/// The concentration M48 computes for one supplier (T203): the active critical processes depending on it, counted once
/// each, and those it lists. Supplied by the day calculator from <c>IThirdPartiesService</c>, so M48's rule is consumed.
/// </summary>
public sealed record RiskIndexSupplierConcentration(int CriticalProcesses, IReadOnlyList<int> ProcessIds);

/// <summary>
/// The full mode of the collector (S39 §15.1; M59, T284–T288). Every read here happens only when the profile is in full
/// mode, so an interim snapshot is unchanged. It reads Track 9's stored results and pure rules; no Track 9 service reads
/// the index (<c>IcrIsNotADecisionInputTest</c>).
/// </summary>
public static partial class RiskIndexObjectCollector
{
    /// <summary>What Track 9 says of one open scenario, stored in its <see cref="RiskIndexScenarioInputs"/>.</summary>
    internal sealed record ScenarioTrack9(IReadOnlyList<int> GateAConditions, int? EvidenceConfidence, double? TargetScore,
        double? TargetExpectedLoss, string GateBTail, string GateBIndicators);

    /// <summary>The criticality source of an object that inherits from the chain nodes it serves (S39 §5.2).</summary>
    public const string InheritedCriticalitySource = "inherited";

    /// <summary>Each host's applications through <c>host_application_links</c> (T291), in full mode under CMDB-REL (T284).</summary>
    private static async Task LoadHostApplicationsAsync(NRDbContext db, Run run, CancellationToken ct)
    {
        if (run.Mode != RiskIndexMode.Full || !run.On(RiskIndexSources.CmdbRelations)) return;

        var window = run.W(RiskIndexSources.CmdbRelations);
        var links = await db.HostApplicationLinks.AsNoTracking()
            .Select(l => new { l.HostId, l.EntityId, l.Source, l.LastSeenAt, l.CreatedAt })
            .ToListAsync(ct);

        // A CMDB relation counts inside the source's window from its last confirmation; a manual one does not age (T291).
        foreach (var g in links.Where(l => l.Source == AssetContextSources.Manual ||
                                           IcrMath.Days(l.LastSeenAt ?? l.CreatedAt, run.Now) <= window)
                     .GroupBy(l => l.HostId))
            run.HostApplications[g.Key] = g.Select(l => l.EntityId).Where(run.Map.Contains).Distinct().Order().ToList();
    }

    /// <summary>
    /// The entities a full-mode object is attributed to beside its own (S39 §10.2; T284): its applications and the data
    /// they serve. Processes and services above are reached by the full-mode closure, which follows the chain down.
    /// </summary>
    internal static IReadOnlyList<int> ChainAttribution(Run run, int? own, IReadOnlyCollection<int> applications)
    {
        var entities = new SortedSet<int>();
        if (own is { } e) entities.Add(e);
        if (run.Mode != RiskIndexMode.Full) return entities.ToList();

        foreach (var application in applications.Where(run.Map.Contains))
        {
            entities.Add(application);
            foreach (var data in run.Map.Served(application).Data) entities.Add(data);
        }

        return entities.ToList();
    }

    /// <summary>
    /// <c>crit_ef = min(5, max(crit_própria, crit_herdada) + Δ_dados)</c> in full mode (S39 §5.2; T284, T285): the highest
    /// criticality of the applications served and the services and processes above them (a process at its M41 BIA
    /// criticality), plus the classification step when a served application or data is sensitive. Interim: unchanged.
    /// </summary>
    internal static (int Criticality, string Source) WithChain(Run run, int crit, string source,
        IReadOnlyCollection<int> applications)
    {
        if (run.Mode != RiskIndexMode.Full || applications.Count == 0) return (crit, source);

        var inherited = 0;
        var sensitive = false;
        foreach (var application in applications.Where(run.Map.Contains))
        {
            var served = run.Map.Served(application);
            foreach (var id in served.Services.Concat(served.Processes).Prepend(application))
                if (ChainCriticality(run, id) is { } c && c > inherited) inherited = c;
            sensitive |= SensitiveLevel(run, application) ||
                         served.Data.Any(d => run.Map.IsSensitiveData(d) || SensitiveLevel(run, d));
        }

        var result = crit;
        var origin = source;
        if (inherited > result)
        {
            result = inherited;
            origin = InheritedCriticalitySource;
        }

        if (sensitive) result += Math.Max(0, run.P.ClassificationStep);
        return (Math.Clamp(result, 1, 5), origin);
    }

    /// <summary>A chain node's criticality: a process's BIA one (M41, consumed as delivered), else the declared one under ENT-CRIT.</summary>
    private static int? ChainCriticality(Run run, int id)
    {
        var (value, source) = run.Map.CriticalityWithSource(id);
        if (value is null) return null;
        return source == EntityMap.CriticalitySourceBia || run.On(RiskIndexSources.EntityCriticality) ? value : null;
    }

    private static bool SensitiveLevel(Run run, int id) =>
        run.On(RiskIndexSources.EntityClassification) && run.Map.SecurityClassification(id) is { } level &&
        run.P.SensitiveLevels.Contains(level);

    /// <summary>
    /// The M39 chain links of each scenario (T146): its process, service, data and application links attribute it to those
    /// nodes in full mode (T284). Host links are assets, not nodes, and stay out.
    /// </summary>
    private static async Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> ChainLinksAsync(NRDbContext db, Run run,
        IReadOnlyCollection<int> riskIds, CancellationToken ct)
    {
        if (run.Mode != RiskIndexMode.Full || riskIds.Count == 0) return new Dictionary<int, IReadOnlyList<int>>();

        var links = await db.RiskChainLinks.AsNoTracking()
            .Where(l => l.EntityId != null)
            .Select(l => new { l.RiskId, EntityId = l.EntityId!.Value })
            .ToListAsync(ct);
        var wanted = riskIds.ToHashSet();
        return links.Where(l => wanted.Contains(l.RiskId) && run.Map.Contains(l.EntityId))
            .GroupBy(l => l.RiskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(l => l.EntityId).Distinct().Order().ToList());
    }

    /// <summary>
    /// What Track 9 holds on each open scenario (T286, T287): M43's Gate A conditions from the stored flags (read, never
    /// re-derived, S39 §7), M40's confidence, M44's target, and Gate B as the decision path evaluates it (M45 tail, M46 KRIs).
    /// </summary>
    private static async Task<IReadOnlyDictionary<int, ScenarioTrack9>> ReadScenarioTrack9Async(NRDbContext db, Run run,
        IReadOnlyDictionary<int, Signals.ScenarioFacts> facts, CancellationToken ct)
    {
        if (run.Mode != RiskIndexMode.Full || facts.Count == 0) return new Dictionary<int, ScenarioTrack9>();

        var ids = facts.Keys.ToHashSet();

        var flags = (await db.RiskFlags.AsNoTracking().Where(f => f.Declared || f.Derived)
                .Select(f => new { f.RiskId, f.Flag }).ToListAsync(ct))
            .Where(f => ids.Contains(f.RiskId))
            .GroupBy(f => f.RiskId)
            .ToDictionary(g => g.Key, g => Tools.RiskFlags.GateA.ConditionsAmong(g.Select(f => f.Flag)));

        var confidence = (await db.Risks.AsNoTracking().Where(r => r.EvidenceConfidence != null)
                .Select(r => new { r.Id, r.EvidenceConfidence }).ToListAsync(ct))
            .Where(r => ids.Contains(r.Id))
            .ToDictionary(r => r.Id, r => (int?)r.EvidenceConfidence);

        var targets = (await db.RiskTargets.AsNoTracking()
                .Select(t => new { t.RiskId, t.TargetScore, t.TargetExpectedLoss }).ToListAsync(ct))
            .Where(t => ids.Contains(t.RiskId))
            .GroupBy(t => t.RiskId)
            .ToDictionary(g => g.Key, g => g.First());

        // Gate B on the tail (S48 §4.7.2): the governing run against the scenario tolerances of the appetite that governs
        // the risk (its entity's row, else the global one) — RiskWorkflowService's rule.
        var tails = (await db.RiskTailStatistics.AsNoTracking().ToListAsync(ct))
            .Where(t => ids.Contains(t.RiskId))
            .GroupBy(t => t.RiskId)
            .ToDictionary(g => g.Key, g => TailStatisticsMapping.Governing(g));
        var appetites = await db.RiskAppetites.AsNoTracking().Select(a => new { a.Id, a.EntityId }).ToListAsync(ct);
        var limits = await db.RiskAppetiteTailLimits.AsNoTracking().ToDictionaryAsync(l => l.AppetiteId, ct);
        var global = appetites.FirstOrDefault(a => a.EntityId == null);

        // Gate B by indicator (S49 §4.8): the active KRIs linked to the risk and their valid readings, MonitoringService's rule.
        var kriLinks = (await db.KriRisks.AsNoTracking().Select(l => new { l.RiskId, l.KriId }).ToListAsync(ct))
            .Where(l => ids.Contains(l.RiskId)).ToList();
        var kriIds = kriLinks.Select(l => l.KriId).Distinct().ToList();
        var kris = kriIds.Count == 0
            ? new Dictionary<int, Kri>()
            : await db.Kris.AsNoTracking().Where(k => kriIds.Contains(k.Id) && k.RetiredAt == null).ToDictionaryAsync(k => k.Id, ct);
        var readings = kris.Count == 0
            ? new Dictionary<int, List<KriObservation>>()
            : await MonitoringService.ValidReadingsAsync(db, kris.Keys.ToList());
        var gates = kris.Values.ToDictionary(k => k.Id, k => KriAppetite.Gate(k.Id, k.Name, k.Category, k.Unit, k.Direction,
            k.ToleranceThreshold, KriEvaluator.Evaluate(MonitoringService.Thresholds(k), readings.GetValueOrDefault(k.Id) ?? [], run.Now)));
        var kriByRisk = kriLinks.GroupBy(l => l.RiskId).ToDictionary(g => g.Key, g => g.Select(l => l.KriId).ToList());

        var result = new Dictionary<int, ScenarioTrack9>();
        foreach (var (id, f) in facts)
        {
            var appetite = (f.EntityId is { } entity ? appetites.FirstOrDefault(a => a.EntityId == entity) : null) ?? global;
            var governing = tails.GetValueOrDefault(id);
            var tail = Tools.TailRisk.TailAppetite.EvaluateScenario(appetite?.Id, appetite?.EntityId,
                appetite is null ? null : TailStatisticsMapping.ScenarioLimits(limits.GetValueOrDefault(appetite.Id)),
                governing?.Run, governing is null ? null : TailStatisticsMapping.ToStatistics(governing));

            var indicators = KriAppetite.Evaluate((kriByRisk.GetValueOrDefault(id) ?? [])
                .Where(gates.ContainsKey).Select(k => gates[k]));

            targets.TryGetValue(id, out var target);
            result[id] = new ScenarioTrack9(flags.GetValueOrDefault(id)?.Select(c => (int)c).ToList() ?? [],
                confidence.GetValueOrDefault(id), (double?)target?.TargetScore, (double?)target?.TargetExpectedLoss,
                TailState(tail.State), IndicatorState(indicators.State));
        }

        return result;
    }

    internal static string TailState(TailAppetiteState state) => state switch
    {
        TailAppetiteState.ExceedsTolerance => RiskIndexGateBStates.Exceeds,
        TailAppetiteState.WithinTolerance => RiskIndexGateBStates.Within,
        TailAppetiteState.NotAssessable => RiskIndexGateBStates.NotAssessable,
        _ => RiskIndexGateBStates.NotConfigured
    };

    internal static string IndicatorState(IndicatorAppetiteState state) => state switch
    {
        IndicatorAppetiteState.ExceedsTolerance => RiskIndexGateBStates.Exceeds,
        IndicatorAppetiteState.WithinTolerance => RiskIndexGateBStates.Within,
        IndicatorAppetiteState.NotAssessable => RiskIndexGateBStates.NotAssessable,
        _ => RiskIndexGateBStates.NotConfigured
    };

    // ------------------------------------------------------------------------------------------------------------------
    // TER — the third-party register (M48; S39 §2.1, §2.3, §15.1; T288). The factor anchors are recorded on T288.
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// One TER object per active or exiting supplier (full mode, TER-REG), attributed to its unit and the entities it is
    /// linked to. TER.D diligence: 1 − the latest current HECVAT score (1 with a critical failure; none current = no reading).
    /// TER.C concentration: 1 − 2^(−n) for n dependent active critical processes counted once (T203). MRA under <c>u.TER</c>.
    /// </summary>
    private static async Task CollectSuppliersAsync(NRDbContext db, Run run, CancellationToken ct)
    {
        if (run.Mode != RiskIndexMode.Full || !run.On(RiskIndexSources.ThirdPartyRegister)) return;

        var suppliers = await db.ThirdParties.AsNoTracking()
            .Where(t => t.Status == ThirdPartyStatus.Active || t.Status == ThirdPartyStatus.Exiting)
            .Select(t => new { t.Id, t.EntityId })
            .ToListAsync(ct);
        if (suppliers.Count == 0) return;

        var ids = suppliers.Select(s => s.Id).ToHashSet();
        var links = (await db.ThirdPartyLinks.AsNoTracking().Select(l => new { l.ThirdPartyId, l.EntityId }).ToListAsync(ct))
            .Where(l => ids.Contains(l.ThirdPartyId) && run.Map.Contains(l.EntityId))
            .GroupBy(l => l.ThirdPartyId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.EntityId).Distinct().Order().ToList());

        var assessments = (await db.ThirdPartyAssessments.AsNoTracking()
                .Where(a => a.VoidedAt == null)
                .Select(a => new
                {
                    a.Id, a.ThirdPartyId, a.ExpectedQuestionCount, a.ValidUntil, a.RespondedAt, a.CreatedAt,
                    Answers = a.Answers.Select(x => new { x.QuestionId, x.Answer, x.PreferredAnswer, x.Weight, x.Critical }).ToList()
                })
                .ToListAsync(ct))
            .Where(a => ids.Contains(a.ThirdPartyId))
            .GroupBy(a => a.ThirdPartyId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.RespondedAt ?? a.CreatedAt).ThenByDescending(a => a.Id).First());

        var u = run.U(RiskIndexFactors.Ter);
        foreach (var s in suppliers.OrderBy(s => s.Id))
        {
            var linked = links.GetValueOrDefault(s.Id) ?? [];
            var concentration = run.Options.SupplierConcentration.GetValueOrDefault(s.Id)
                                ?? new RiskIndexSupplierConcentration(0, []);

            var crit = linked.Concat(concentration.ProcessIds).Select(id => ChainCriticality(run, id)).Where(c => c is not null)
                .Select(c => c!.Value).DefaultIfEmpty(0).Max();
            var critSource = crit > 0 ? InheritedCriticalitySource : "default";
            if (crit <= 0) crit = run.P.DefaultCriticality;
            crit = Math.Clamp(crit, 1, 5);
            var m = run.M(crit);

            var readings = new List<Reading>();

            // TER.C is state, read every day from M48's dependency count.
            var n = Math.Max(0, concentration.CriticalProcesses);
            var xc = 1 - Math.Pow(2, -n);
            readings.Add(new Reading(RiskIndexFactors.TerConcentration, RiskIndexFactorType.V, xc, 1, u, null,
                RiskIndexSources.ThirdPartyRegister,
                xc > 0 ? [new RiskIndexSignalReading(RiskIndexFactors.TerConcentration,
                    "critical_processes:" + n.ToString(CultureInfo.InvariantCulture), xc, [])] : []));

            if (assessments.TryGetValue(s.Id, out var a))
            {
                var score = HecvatScoring.Score(a.ExpectedQuestionCount,
                    a.Answers.Select(x => new HecvatAnswerFacts(x.QuestionId, x.Answer, x.PreferredAnswer, x.Weight, x.Critical)).ToList(),
                    a.ValidUntil, voided: false, run.Now);
                if (score.State is HecvatState.Conforming or HecvatState.NonConforming && score.Score is { } fraction)
                {
                    var failed = score.CriticalFailures.Count > 0;
                    var xd = failed ? 1 : IcrMath.Clamp01(1 - (double)fraction);
                    readings.Add(new Reading(RiskIndexFactors.TerDiligence, RiskIndexFactorType.V, xd, 1, u,
                        a.RespondedAt ?? a.CreatedAt, RiskIndexSources.ThirdPartyRegister,
                        xd > 0 ? [new RiskIndexSignalReading(RiskIndexFactors.TerDiligence,
                            "hecvat:" + a.Id.ToString(CultureInfo.InvariantCulture), xd, failed ? ["critical_failure"] : [])] : []));
                }
            }

            var key = new IcrObjectKey(RiskIndexObjectKind.Supplier, s.Id.ToString(CultureInfo.InvariantCulture));
            run.Rows.Add(SupplierRow(run, key, readings, m, u) with
            {
                Criticality = crit, CriticalitySource = critSource, ScopeEntityId = s.EntityId,
                Attribution = (s.EntityId is { } own ? linked.Prepend(own) : linked).Distinct().Order().ToList()
            });
        }
    }

    /// <summary>
    /// A supplier's TER row: the MRA of <c>u·m·x</c> over its factors, assessed when a HECVAT was read; with concentration
    /// alone it is presumed at <c>max(MRA, u·π·m)</c> — no diligence reading is not a clean supplier (S39 §8.1).
    /// </summary>
    private static RiskIndexObjectRow SupplierRow(Run run, IcrObjectKey key, List<Reading> readings, double m, double u)
    {
        var p = run.P;
        var values = readings.Select(r => r.Ceiling * m * r.X).ToList();
        var mra = Math.Min(u, IcrMath.Mra(values, p.Rho, p.KSignals));
        var diligence = readings.Any(r => r.Factor == RiskIndexFactors.TerDiligence);

        double r;
        RiskIndexObjectState state;
        if (diligence)
        {
            r = mra;
            state = r > 0 ? RiskIndexObjectState.Assessed : RiskIndexObjectState.AssessedClean;
        }
        else
        {
            r = Math.Max(mra, u * p.PiPrior * m);
            state = RiskIndexObjectState.Presumed;
        }

        return new RiskIndexObjectRow
        {
            Key = key,
            Category = RiskIndexCategory.Ter,
            Population = RiskIndexPopulation.Suppliers,
            R = IcrMath.Clamp01(r),
            Weight = m,
            State = state,
            Factors = readings.Select(x => new RiskIndexFactorReading
            {
                Factor = x.Factor, Type = x.Type, X = x.X, Validity = x.Validity, Ceiling = x.Ceiling, ReadAt = x.ReadAt,
                Source = x.Source
            }).ToList(),
            Signals = readings.SelectMany(x => x.Signals).ToList(),
            SourceMask = RiskIndexSourceBits.Bit(RiskIndexSources.ThirdPartyRegister),
            Freshness = diligence ? 1 : 0,
            RMax = Math.Max(u * m, r)
        };
    }
}

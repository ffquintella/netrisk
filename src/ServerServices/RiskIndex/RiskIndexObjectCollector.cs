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

    /// <summary>Supplier id → M48's concentration (full mode with TER-REG; T288); a supplier missing here has none.</summary>
    public IReadOnlyDictionary<int, RiskIndexSupplierConcentration> SupplierConcentration { get; init; } =
        new Dictionary<int, RiskIndexSupplierConcentration>();
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

        /// <summary>
        /// Each host's current Tenable ACR (1–10) and AES (0–1000) with their read time (M56, T267), loaded only when the
        /// snapshot's profile enables TEN-ACR or TEN-AES: collecting a reading never moves the index by itself (S40 §6.8).
        /// </summary>
        public Dictionary<int, (double Value, DateTime ReadAt)> TenableAcr = new(), TenableAes = new();

        /// <summary>Host id → the applications it runs (<c>host_application_links</c>), full mode under CMDB-REL (T284).</summary>
        public Dictionary<int, IReadOnlyList<int>> HostApplications = new();

        public bool Full => Mode == RiskIndexMode.Full;

        public bool On(string source) => Enabled.Contains(source);
        public double W(string source) => P.FreshnessDays.TryGetValue(source, out var w) ? w : 30;
        public double M(int crit) => IcrMath.Modulator(crit, P.MA);
        public double U(string factor) => GovernanceSignals.Ceiling(P, factor);
    }

    public static async Task<RiskIndexObjectSet> CollectAsync(NRDbContext db, RiskIndexParameters p, RiskIndexMode mode,
        DateTime now, RiskIndexCollectionOptions? options = null, CancellationToken ct = default)
    {
        var map = await ReadEntityMapAsync(db, ct, mode);
        var run = new Run
        {
            P = p, Mode = mode, Now = now, Map = map, Enabled = p.EnabledSources.ToHashSet(StringComparer.Ordinal),
            Options = options ?? new RiskIndexCollectionOptions()
        };

        var facts = await RegisterFactsReader.ReadAsync(db, p, now, ct);
        var attribution = await ScenarioAttribution.EntitiesByRiskAsync(db, facts.Keys.ToList(), ct);

        // Full mode (M59): the M39 chain links of each scenario join its attribution (T284), and Track 9's verdicts on it
        // are carried beside its value (T286, T287).
        var chain = await ChainLinksAsync(db, run, facts.Keys.ToList(), ct);
        if (chain.Count > 0)
            attribution = facts.Keys.ToDictionary(id => id, id => (IReadOnlyList<int>)(attribution.GetValueOrDefault(id) ?? [])
                .Concat(chain.GetValueOrDefault(id) ?? []).Distinct().Order().ToList());
        var track9 = await ReadScenarioTrack9Async(db, run, facts, ct);

        await LoadTenableReadingsAsync(db, run, ct);
        await LoadHostApplicationsAsync(db, run, ct);
        var exposure = await CollectExposureAsync(db, run, ct);
        CollectScenarios(run, facts, attribution, exposure.FreshHostExposure, track9);
        var events = await CollectEventsAsync(db, run, ct);
        await CollectAssessmentsAsync(db, run, ct);
        await CollectSecurityConfigurationAsync(db, run, ct);
        await CollectSuppliersAsync(db, run, ct);

        var enabled = new Dictionary<RiskIndexCategory, bool>
        {
            [RiskIndexCategory.Reg] = run.On(RiskIndexSources.RegResidual),
            [RiskIndexCategory.Exp] = new[]
            {
                RiskIndexSources.V1Device, RiskIndexSources.V1Cve, RiskIndexSources.SscTotal, RiskIndexSources.TenableNessus,
                RiskIndexSources.TenableVm, RiskIndexSources.TenableAes, RiskIndexSources.Importers
            }.Concat(VendorAssetSources).Any(run.On),
            [RiskIndexCategory.Ame] = (run.On(RiskIndexSources.Incidents) || run.On(RiskIndexSources.NearMisses)) &&
                                      events.IntakeActive,
            [RiskIndexCategory.Ctl] = run.On(RiskIndexSources.Assessments) || run.On(RiskIndexSources.IncidentResponsePlans) ||
                                      run.On(RiskIndexSources.V1SecurityConfiguration),
            [RiskIndexCategory.Gov] = run.On(RiskIndexSources.RegResidual) || exposure.AnySlaSource,
            // TER reads the M48 register in full mode only (T288).
            [RiskIndexCategory.Ter] = run.Full && run.On(RiskIndexSources.ThirdPartyRegister)
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

    /// <summary>
    /// The entity map and the EAV properties the index reads (links and risk context). In full mode (M59) also the M39 links
    /// of IT services, the MTPD of M41's BIAs and the data records the M49 catalogue marks sensitive.
    /// </summary>
    public static async Task<EntityMap> ReadEntityMapAsync(NRDbContext db, CancellationToken ct = default,
        RiskIndexMode mode = RiskIndexMode.Interim)
    {
        var entities = await db.Entities.AsNoTracking()
            .Select(e => new EntityMapRow(e.Id, e.DefinitionName, e.Parent)).ToListAsync(ct);

        var full = mode == RiskIndexMode.Full;
        List<string> keys =
        [
            RiskIndexEntityTypes.ProcessUnitsProperty, RiskIndexEntityTypes.ProcessApplicationsProperty,
            Model.Entities.RiskContextProperties.Criticality, Model.Entities.RiskContextProperties.InternetFacing,
            Model.Entities.RiskContextProperties.SecurityClassification, "environment"
        ];
        if (full) keys.AddRange([RiskIndexEntityTypes.ServiceProcessesProperty, RiskIndexEntityTypes.ServiceDataProperty]);
        var properties = await db.EntitiesProperties.AsNoTracking().Where(p => keys.Contains(p.Type))
            .Select(p => new EntityPropertyRow(p.Entity, p.Type, p.Value)).ToListAsync(ct);

        if (!full) return EntityMap.Build(entities, properties);

        // M41 (T285): the MTPD of each BIA; ProcessCriticality derives the criticality from it, as the BIA screens do.
        var mtpd = await db.BusinessImpactAnalyses.AsNoTracking().Where(b => b.MtpdMinutes != null)
            .Select(b => new { b.EntityId, Mtpd = b.MtpdMinutes!.Value }).ToDictionaryAsync(b => b.EntityId, b => b.Mtpd, ct);

        // M49 (T285): sensitive by the catalogue — sensitive personal data, a large volume of personal data, or strategic
        // research, the flag 5 basis of S52 §4.8 read the way RiskFlagsService reads it.
        var sensitive = (await db.DataCatalogueEntries.AsNoTracking()
                .Select(e => new { e.EntityId, e.PersonalData, e.LargeVolume, e.StrategicResearch }).ToListAsync(ct))
            .Where(e => e.PersonalData == DAL.Enums.PersonalDataCategory.SensitivePersonal || e.StrategicResearch ||
                        (e.LargeVolume && e.PersonalData is DAL.Enums.PersonalDataCategory.Personal
                            or DAL.Enums.PersonalDataCategory.SensitivePersonal))
            .Select(e => e.EntityId).ToHashSet();

        return EntityMap.Build(entities, properties, new EntityChainFacts(mtpd, sensitive), fullMode: true);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Criticality of an entity-backed object (S39 §5.1–§5.2): declared EAV criticality, else the default; plus the
    // classification step for an application whose level the profile marks sensitive.
    // ------------------------------------------------------------------------------------------------------------
    internal static (int Criticality, string Source) EntityCriticality(Run run, int? entityId)
    {
        if (entityId is not { } id) return (run.P.DefaultCriticality, "default");

        // A process's M41 BIA criticality is consumed as delivered in full mode (T285); the declared one under ENT-CRIT.
        var (value, origin) = run.Map.CriticalityWithSource(id);
        var declared = origin == EntityMap.CriticalitySourceBia || run.On(RiskIndexSources.EntityCriticality) ? value : null;
        var crit = declared ?? run.P.DefaultCriticality;
        var source = declared is null ? "default" : origin!;

        // Full mode: an application inherits from the chain nodes it serves and takes Δ_dados from them (T284, T285).
        if (run.Full && run.Map.DefinitionOf(id) == RiskIndexEntityTypes.Application)
            return WithChain(run, Math.Clamp(crit, 1, 5), source, [id]);

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
        IReadOnlyDictionary<int, IReadOnlyList<int>> attribution, IReadOnlyDictionary<int, double> freshHostExposure,
        IReadOnlyDictionary<int, ScenarioTrack9> track9)
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
            var t9 = track9.GetValueOrDefault(id);
            var inputs = new RiskIndexScenarioInputs
            {
                GateAConditions = run.Full ? t9?.GateAConditions ?? [] : null,
                EvidenceConfidence = t9?.EvidenceConfidence, TargetScore = t9?.TargetScore,
                TargetExpectedLoss = t9?.TargetExpectedLoss, GateBTail = t9?.GateBTail, GateBIndicators = t9?.GateBIndicators,
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

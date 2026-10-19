using System.Globalization;
using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Assessments;
using Model.RiskIndex;
using Model.Status;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex;

public static partial class RiskIndexObjectCollector
{
    private sealed record EventsResult(IReadOnlyDictionary<int, DateTime> ClosureDates, bool IntakeActive);

    private sealed record IncidentSignal(int Id, string Factor, double Value, double Decay);

    /// <summary>
    /// AME (S39 §2.1, §3.1, §9.2): one object per target entity — every entity of a target type, assessed (R = 0 when
    /// quiet) while the intake is active — valued <c>MRA(u_f·m(crit)·MRA(s·d))</c>. A closed incident decays from the
    /// closure date the first snapshot froze, or <c>LastUpdate</c> read once (D-15 interim).
    /// </summary>
    private static async Task<EventsResult> CollectEventsAsync(NRDbContext db, Run run, CancellationToken ct)
    {
        var p = run.P;
        var incidents = await db.Incidents.AsNoTracking()
            .Select(i => new { i.Id, i.Status, i.Category, i.Kind, i.ImpactedEntityId, i.EntityId, i.CreationDate, i.LastUpdate })
            .ToListAsync(ct);

        var intake = incidents.Any(i => IcrMath.Days(i.CreationDate, run.Now) <= p.IncidentIntakeWindowDays);
        var closures = new Dictionary<int, DateTime>();
        var byTarget = new Dictionary<string, List<IncidentSignal>>(StringComparer.Ordinal);

        foreach (var i in incidents)
        {
            var nearMiss = i.Kind == IncidentKind.NearMiss;
            var source = nearMiss ? RiskIndexSources.NearMisses : RiskIndexSources.Incidents;
            if (!run.On(source)) continue;

            var open = ClosedStatuses.IsOpen(i.Status);
            DateTime? closedAt = open ? null
                : run.Options.IncidentClosureDates.TryGetValue(i.Id, out var frozen) ? frozen : i.LastUpdate;
            if (IcrAnchors.IncidentDecay(p, open, closedAt, run.Now) is not { } d) continue;
            if (closedAt is { } c) closures[i.Id] = c;

            var target = i.ImpactedEntityId ?? i.EntityId;
            var key = target is { } t && run.Map.Contains(t) ? t.ToString(CultureInfo.InvariantCulture) : "";
            if (!byTarget.TryGetValue(key, out var list)) byTarget[key] = list = [];
            list.Add(new IncidentSignal(i.Id, nearMiss ? "AME.Q" : RiskIndexFactors.AmeIncidents,
                IcrAnchors.Incident(p, i.Category, null), d));
        }

        var targets = run.Map.Ids.Where(id => run.Map.DefinitionOf(id) is { } d && RiskIndexEntityTypes.TargetTypes.Contains(d))
            .Select(id => id.ToString(CultureInfo.InvariantCulture))
            .Concat(byTarget.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();

        foreach (var target in targets)
        {
            int? entity = int.TryParse(target, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
            var (crit, critSource) = EntityCriticality(run, entity);
            var m = run.M(crit);
            var signals = byTarget.GetValueOrDefault(target) ?? [];

            double Value(Func<IncidentSignal, double> decay)
            {
                var factors = signals.GroupBy(s => s.Factor).Select(g =>
                    Ceiling(run, g.Key) * m * IcrMath.Mra(g.Select(s => s.Value * decay(s)), p.Rho, p.KSignals));
                return IcrMath.Mra(factors, p.Rho, p.KSignals);
            }

            var r = Value(s => s.Decay);
            var key = entity is { } e
                ? new IcrObjectKey(RiskIndexObjectKind.TargetEntity, target)
                : new IcrObjectKey(RiskIndexObjectKind.UnassignedGroup, "incidents");

            run.Rows.Add(new RiskIndexObjectRow
            {
                Key = key, Category = RiskIndexCategory.Ame, Population = RiskIndexPopulation.Targets, R = IcrMath.Clamp01(r),
                Weight = m, Criticality = crit, CriticalitySource = critSource,
                State = r > 0 ? RiskIndexObjectState.Assessed : RiskIndexObjectState.AssessedClean,
                ScopeEntityId = entity, Attribution = entity is { } a ? [a] : [],
                Factors = signals.GroupBy(s => s.Factor).Select(g => new RiskIndexFactorReading
                {
                    Factor = g.Key, Type = RiskIndexFactorType.V, X = IcrMath.Mra(g.Select(s => s.Value * s.Decay), p.Rho, p.KSignals),
                    Ceiling = Ceiling(run, g.Key),
                    Source = g.Key == RiskIndexFactors.AmeIncidents ? RiskIndexSources.Incidents : RiskIndexSources.NearMisses
                }).ToList(),
                Signals = signals.OrderByDescending(s => s.Value * s.Decay).Take(50)
                    .Select(s => new RiskIndexSignalReading(s.Factor, $"incident:{s.Id}", s.Value * s.Decay,
                        s.Decay < 1 ? ["decayed"] : ["open"])).ToList(),
                SourceMask = signals.Count == 0 ? 0 : RiskIndexSourceBits.Mask(signals.Select(s =>
                    (string?)(s.Factor == RiskIndexFactors.AmeIncidents ? RiskIndexSources.Incidents : RiskIndexSources.NearMisses))),
                RMax = Ceiling(run, RiskIndexFactors.AmeIncidents) * m,
                ClearedR = IcrMath.Clamp01(Value(s => s.Decay / 2))
            });
        }

        return new EventsResult(closures, intake);
    }

    private static double Ceiling(Run run, string factor) =>
        factor == "AME.Q" ? IcrMath.Clamp01(run.P.NearMissFactor) : run.U(factor);

    /// <summary>
    /// CTL.A (S39 §3.1, §8.3; D-16 experimental): the latest Submitted run per (assessment, target), scored
    /// <c>Σ chosen RiskScore / Σ question maximum</c> over the answered questions; the inventory is the entities of the
    /// assessable types and the hosts with a run, presumed when never assessed.
    /// </summary>
    private static async Task CollectAssessmentsAsync(NRDbContext db, Run run, CancellationToken ct)
    {
        if (!run.On(RiskIndexSources.Assessments)) return;
        var p = run.P;

        var runs = await db.AssessmentRuns.AsNoTracking()
            .Select(r => new { r.Id, r.AssessmentId, r.EntityId, r.HostId, r.RunDate, r.Status })
            .ToListAsync(ct);
        var submitted = runs.Where(r => r.Status == (int)AssessmentStatus.Submitted).ToList();
        var latest = submitted.GroupBy(r => (r.AssessmentId, Target: r.HostId is { } h ? $"h{h}" : $"e{r.EntityId}"))
            .Select(g => g.OrderByDescending(r => r.RunDate).ThenByDescending(r => r.Id).First()).ToList();

        var runIds = latest.Select(r => r.Id).ToList();
        var chosen = await db.AssessmentRunsAnswers.AsNoTracking().Where(a => runIds.Contains(a.RunId))
            .Select(a => new { a.RunId, a.QuestionId, a.AnswerId }).ToListAsync(ct);
        var questionIds = chosen.Select(c => c.QuestionId).Distinct().ToList();
        var options = await db.AssessmentAnswers.AsNoTracking().Where(a => questionIds.Contains(a.QuestionId))
            .Select(a => new { a.Id, a.QuestionId, a.RiskScore }).ToListAsync(ct);
        var scoreOf = options.ToDictionary(o => o.Id, o => (double)o.RiskScore);
        var maxOf = options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.Max(o => (double)o.RiskScore));

        double? Ratio(int runId)
        {
            var answers = chosen.Where(c => c.RunId == runId).GroupBy(c => c.QuestionId).ToList();
            var max = answers.Sum(g => Math.Max(0, maxOf.GetValueOrDefault(g.Key)));
            if (max <= 0) return null;
            var got = answers.Sum(g => g.Max(c => Math.Max(0, scoreOf.GetValueOrDefault(c.AnswerId))));
            return IcrMath.Clamp01(got / max);
        }

        // tipos_avaliaveis: the profile's list, or by default the types with a Submitted run on an entity.
        var assessable = p.AssessableTypes.Count > 0
            ? p.AssessableTypes.ToHashSet(StringComparer.Ordinal)
            : submitted.Where(r => r.HostId is null).Select(r => run.Map.DefinitionOf(r.EntityId)).OfType<string>()
                .ToHashSet(StringComparer.Ordinal);

        var hostIds = runs.Where(r => r.HostId is not null).Select(r => r.HostId!.Value).Distinct().ToList();
        var hosts = await db.Hosts.AsNoTracking().Where(h => hostIds.Contains(h.Id) && h.Status != RetiredHost)
            .Select(h => new { h.Id, h.Criticality, h.ExternalProvider, h.Environment, h.EntityId, h.Os }).ToListAsync(ct);

        var targets = new List<(IcrObjectKey Key, int? Entity, int Crit, string CritSource, IEnumerable<int> Runs,
            RiskIndexAssetClass? AssetClass)>();
        foreach (var id in run.Map.Ids.Where(id => run.Map.DefinitionOf(id) is { } d && assessable.Contains(d)).OrderBy(i => i))
        {
            var (crit, source) = EntityCriticality(run, id);
            targets.Add((new IcrObjectKey(RiskIndexObjectKind.TargetEntity, id.ToString(CultureInfo.InvariantCulture)), id,
                crit, source, latest.Where(r => r.HostId is null && r.EntityId == id).Select(r => r.Id), null));
        }

        foreach (var h in hosts.OrderBy(h => h.Id))
        {
            var (crit, source) = HostCriticality(run, h.Criticality, h.ExternalProvider, h.Environment);
            targets.Add((new IcrObjectKey(RiskIndexObjectKind.Host, h.Id.ToString(CultureInfo.InvariantCulture)), h.EntityId,
                crit, source, latest.Where(r => r.HostId == h.Id).Select(r => r.Id), AssetClassOf(h.Os, null)));
        }

        var u = run.U(RiskIndexFactors.CtlAssessments);
        var window = run.W(RiskIndexSources.Assessments);
        var runById = latest.ToDictionary(r => r.Id);

        foreach (var (key, entity, crit, critSource, targetRuns, assetClass) in targets)
        {
            var m = run.M(crit);
            var scored = targetRuns.Select(id => (Run: runById[id], Ratio: Ratio(id))).Where(t => t.Ratio is not null).ToList();
            var readAt = scored.Select(t => t.Run.RunDate).Max();
            var validity = readAt is { } at ? IcrMath.Validity(IcrMath.Days(at, run.Now), window) : 0;
            var x = IcrMath.Mra(scored.Select(t => t.Ratio!.Value), p.Rho, p.KSignals);
            var f = m * x;

            double r;
            RiskIndexObjectState state;
            if (scored.Count == 0 || validity <= 0)
            {
                r = u * Math.Max(scored.Count == 0 ? 0 : f, p.PiPrior * m);
                state = RiskIndexObjectState.Presumed;
            }
            else
            {
                r = u * IcrMath.EffectiveFactor(f, validity, p.PiPrior, m);
                state = validity >= 1
                    ? r > 0 ? RiskIndexObjectState.Assessed : RiskIndexObjectState.AssessedClean
                    : RiskIndexObjectState.Stale;
            }

            run.Rows.Add(new RiskIndexObjectRow
            {
                Key = key, Category = RiskIndexCategory.Ctl, Population = RiskIndexPopulation.Assessments,
                R = IcrMath.Clamp01(r), Weight = m, Criticality = crit, CriticalitySource = critSource, State = state,
                ScopeEntityId = entity, Attribution = entity is { } e ? [e] : [],
                Factors = scored.Count == 0 ? [] :
                [
                    new RiskIndexFactorReading
                    {
                        Factor = RiskIndexFactors.CtlAssessments, Type = RiskIndexFactorType.V, X = x, Validity = validity,
                        Ceiling = u, ReadAt = readAt, Source = RiskIndexSources.Assessments
                    }
                ],
                Signals = scored.Select(t => new RiskIndexSignalReading(RiskIndexFactors.CtlAssessments,
                    $"assessment-run:{t.Run.Id}", t.Ratio!.Value, ["experimental"])).ToList(),
                SourceMask = scored.Count == 0 ? 0 : RiskIndexSourceBits.Bit(RiskIndexSources.Assessments),
                Freshness = validity,
                RMax = u * m,
                AssetClass = assetClass
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The worked examples of S39 as engine inputs: the canonical case (§4.7) and Unit X (§11), on 2026-10-05 and on
/// 2026-10-06 (Example C). Every value is derived through the engine's own anchors and primitives from the data the
/// spec's tables give, so the golden tests exercise normalization as well as aggregation.
/// </summary>
internal static class IcrFixtures
{
    public static readonly RiskIndexParameters Balanced = new();

    public static double M(int crit) => IcrMath.Modulator(crit, Balanced.MA);

    public static IcrObjectKey Host(string id) => new(RiskIndexObjectKind.Host, id);
    public static IcrObjectKey Scenario(string id) => new(RiskIndexObjectKind.Scenario, id);
    public static IcrObjectKey Target(string id) => new(RiskIndexObjectKind.TargetEntity, id);
    public static IcrObjectKey App(string id) => new(RiskIndexObjectKind.Application, id);
    public static IcrObjectKey Domain(string id) => new(RiskIndexObjectKind.ExternalDomain, id);

    public static IcrCategoryObject Obj(IcrObjectKey key, RiskIndexPopulation population, double r, double w,
        IcrMembership membership = IcrMembership.Assessed, double freshness = 1, double coverageEffect = 1,
        double rMax = 1, double clearedR = 0) => new()
    {
        Key = key, Population = population, R = r, Weight = w, Membership = membership, Freshness = freshness,
        CoverageEffect = coverageEffect, RMax = rMax, ClearedR = clearedR
    };

    // ---------- S39 §4.7: the critical server and the twenty test machines ----------

    public static List<IcrCategoryObject> Canonical(int vms = 20, double server = 0.90, double vm = 0.10, int copies = 1)
    {
        var list = new List<IcrCategoryObject>();
        for (var c = 0; c < copies; c++)
        {
            list.Add(Obj(Host($"SRV-{c}"), RiskIndexPopulation.Assets, server, M(5)));
            for (var i = 0; i < vms; i++)
                list.Add(Obj(Host($"TST-{c}-{i}"), RiskIndexPopulation.Assets, vm, M(1)));
        }

        return list;
    }

    public static IcrCategoryInput Exp(IEnumerable<IcrCategoryObject> objects) =>
        new() { Category = RiskIndexCategory.Exp, Objects = objects.ToList() };

    // ---------- S39 §11: Unit X ----------

    /// <summary>R of DB-02: a VPR 6.7 CVE and two CVSS 5.3 CVEs with an exploit available, crit 4 (S39 §11.1).</summary>
    public static double Db02()
    {
        var p = Balanced;
        var vpr = IcrAnchors.Finding(p, new FindingReading { Vpr = 6.7, Severity = 3 })!.Value.Value;
        var cvss = IcrAnchors.Finding(p, new FindingReading { Cvss3 = 5.3, Severity = 2, ExploitAvailable = true })!.Value.Value;
        return M(4) * IcrMath.Mra([vpr, cvss, cvss], p.Rho, p.KSignals);
    }

    /// <summary>R of APP-03: SAST findings without CVSS — one High and <paramref name="mediums"/> Medium — internet-facing.</summary>
    public static double App03(bool high = true, int mediums = 3)
    {
        var p = Balanced;
        var signals = new List<double>();
        if (high) signals.Add(IcrAnchors.Finding(p, new FindingReading { Severity = 3, InternetFacing = true })!.Value.Value);
        for (var i = 0; i < mediums; i++)
            signals.Add(IcrAnchors.Finding(p, new FindingReading { Severity = 2, InternetFacing = true })!.Value.Value);
        return M(5) * IcrMath.Mra(signals, p.Rho, p.KSignals);
    }

    public static double Domain72() => IcrAnchors.Ssc(Balanced, 72) * M(4);

    /// <summary>A clean scan aged <paramref name="ageDays"/> under W = 30 on a crit-4 host (BKP-04).</summary>
    public static double Bkp04(double ageDays, out double validity)
    {
        validity = IcrMath.Validity(ageDays, 30);
        return IcrMath.EffectiveFactor(0, validity, Balanced.PiPrior, M(4));
    }

    public static readonly string[] Scenarios = ["R1", "R2", "R3", "R4", "R5", "R6"];

    /// <summary>w_r = m(impact)·m_rank, ranks 1–6 of a campaign of 6, impacts 5, 4, 3, 3, 2, 1 (S39 §11.1).</summary>
    public static double[] ScenarioWeights()
    {
        int[] impacts = [5, 4, 3, 3, 2, 1];
        return impacts.Select((impact, i) => M(impact) * IcrAnchors.RankMultiplier(Balanced, i + 1, 6)).ToArray();
    }

    /// <summary>The Unit X node on 2026-10-05 (<paramref name="day"/> 0) or 2026-10-06 (1), Example C.</summary>
    public static IcrNodeInput UnitX(int day = 0, bool ameAndCtlSources = true)
    {
        var p = Balanced;
        var w = ScenarioWeights();

        // ---- EXP ----
        var exp = new List<IcrCategoryObject> { Obj(Host("SRV-01"), RiskIndexPopulation.Assets, 0.90, M(5)) };
        for (var i = 1; i <= 20; i++) exp.Add(Obj(Host($"TST-{i:00}"), RiskIndexPopulation.Assets, 0.10, M(1)));
        for (var i = 1; i <= 10; i++) exp.Add(Obj(Host($"WS-{i:00}"), RiskIndexPopulation.Assets, 0.25, M(2)));
        exp.Add(Obj(Host("DB-02"), RiskIndexPopulation.Assets, Db02(), M(4)));
        exp.Add(Obj(App("APP-03"), RiskIndexPopulation.Assets, App03(high: day == 0), M(5)));
        exp.Add(Obj(Domain("SSC-1"), RiskIndexPopulation.Assets, Domain72(), M(4)));
        var bkp = Bkp04(45 + day, out var v);
        exp.Add(Obj(Host("BKP-04"), RiskIndexPopulation.Assets, bkp, M(4), freshness: v));
        exp.Add(Obj(Host("WS-ADM"), RiskIndexPopulation.Assets, p.PiPrior * M(3), M(3), IcrMembership.Presumed));
        exp.Add(Obj(Host("LEG-05"), RiskIndexPopulation.Assets, Math.Max(0.30, p.PiPrior * M(5)), M(5),
            IcrMembership.Presumed));

        // ---- AME: incidents decay by the closure date; intake active ----
        var inc1 = IcrAnchors.Incident(p, "phishing", null) * IcrAnchors.IncidentDecay(p, false,
            new DateTime(2026, 9, 15), new DateTime(2026, 10, 5).AddDays(day))!.Value;
        var inc2 = IcrAnchors.Incident(p, "unauthorized_access", null);
        var ame = new List<IcrCategoryObject>
        {
            Obj(Target("UNIT-X"), RiskIndexPopulation.Targets, M(3) * inc1, M(3), clearedR: M(3) * inc1 / 2),
            Obj(Target("APP-03"), RiskIndexPopulation.Targets, M(5) * inc2, M(5), clearedR: M(5) * inc2 / 2),
            Obj(Target("P-MAT"), RiskIndexPopulation.Targets, 0, M(5)),
            Obj(Target("P-PES"), RiskIndexPopulation.Targets, 0, M(3)),
            Obj(Target("LAB-L1"), RiskIndexPopulation.Targets, 0, M(2))
        };

        // ---- REG: r_eff by the review credit (S39 §6.1) ----
        double Reff(double res, double inh, double delay, int cadence) =>
            res + (1 - Math.Max(0, 1 - Math.Max(0, delay) / cadence)) * Math.Max(0, inh - res);
        double[] sigmaR =
        [
            (day == 0 ? Reff(8.4, 8.8, 15, 90) : 8.4) / 10,
            Reff(4.0, 8.0, 60 + day, 90) / 10,
            0.60,
            0.30,
            Reff(2.0, 3.2, 20 + day, 240) / 10,
            0.12
        ];
        var reg = Scenarios.Select((s, i) => Obj(Scenario(s), RiskIndexPopulation.Scenarios, sigmaR[i], w[i],
            coverageEffect: s == "R3" ? 0 : 1)).ToList();

        // ---- GOV: σ_ref × MRA(u_f·x_f) on scenarios; m(crit)·u_T·SLA on assets ----
        var u = p.FactorCeilings;
        double Gov(double sigmaRef, params double[] factors) => sigmaRef * IcrMath.Mra(factors, p.Rho, p.KSignals);
        double[] gov =
        [
            day == 0
                ? Gov(0.84, 1.0 * u[RiskIndexFactors.GovAppetite], 0.7 * u[RiskIndexFactors.GovCampaign],
                    12.0 / 30 * u[RiskIndexFactors.GovTreatment])
                : Gov(0.84, 0.5 * u[RiskIndexFactors.GovAppetite], 13.0 / 30 * u[RiskIndexFactors.GovTreatment]),
            Gov(0.40, 1.0 * u[RiskIndexFactors.GovCampaign]),
            Gov(0.60, 1.0 * u[RiskIndexFactors.GovAppetite], Math.Min(1, (67.0 + day) / 120) * u[RiskIndexFactors.GovReview]),
            Gov(0.30, 0.8 * u[RiskIndexFactors.GovDivergence]),
            0,
            0
        ];
        var govObjects = Scenarios.Select((s, i) => Obj(Scenario(s), RiskIndexPopulation.Scenarios, gov[i], w[i])).ToList();
        double Sla(int crit, int sev, double overdue, double period) =>
            M(crit) * u[RiskIndexFactors.GovTreatment] * IcrAnchors.SeverityAnchor(p, sev) * Math.Min(1, overdue / period);
        govObjects.Add(Obj(Host("SRV-01"), RiskIndexPopulation.Assets, Sla(5, 4, 6 + day, 15), M(5)));
        govObjects.Add(Obj(Host("DB-02"), RiskIndexPopulation.Assets, Sla(4, 3, 12 + day, 30), M(4)));
        govObjects.Add(Obj(App("APP-03"), RiskIndexPopulation.Assets, 0, M(5)));
        govObjects.Add(Obj(Host("BKP-04"), RiskIndexPopulation.Assets, 0, M(4)));
        govObjects.Add(Obj(Host("WS-ADM"), RiskIndexPopulation.Assets, 0, M(3)));
        for (var i = 1; i <= 20; i++) govObjects.Add(Obj(Host($"TST-{i:00}"), RiskIndexPopulation.Assets, 0, M(1)));
        for (var i = 1; i <= 10; i++) govObjects.Add(Obj(Host($"WS-{i:00}"), RiskIndexPopulation.Assets, 0, M(2)));
        govObjects.Add(Obj(Host("LEG-05"), RiskIndexPopulation.Assets, 0, M(5), IcrMembership.InventoryOnly,
            rMax: M(5) * u[RiskIndexFactors.GovTreatment]));

        // ---- CTL: assessment ratios, and IRP on every open scenario ----
        var ctl = new List<IcrCategoryObject>
        {
            Obj(Target("UNIT-X"), RiskIndexPopulation.Assessments, M(3) * u[RiskIndexFactors.CtlAssessments] * 0.35, M(3)),
            Obj(Target("LAB-L1"), RiskIndexPopulation.Assessments, M(2) * u[RiskIndexFactors.CtlAssessments] * 0.10, M(2))
        };
        double[] sigmaRef = [0.84, 0.40, 0.60, 0.30, 0.20, 0.12];
        ctl.AddRange(Scenarios.Select((s, i) => Obj(Scenario(s), RiskIndexPopulation.Scenarios,
            sigmaRef[i] * u[RiskIndexFactors.CtlResponse] * (i == 0 ? 0.5 : 1.0) * IcrAnchors.IrpRamp(p, sigmaRef[i]),
            w[i])));

        return new IcrNodeInput
        {
            Categories =
            [
                new IcrCategoryInput { Category = RiskIndexCategory.Reg, Objects = reg },
                Exp(exp),
                new IcrCategoryInput { Category = RiskIndexCategory.Ame, Objects = ame, SourceEnabled = ameAndCtlSources },
                new IcrCategoryInput { Category = RiskIndexCategory.Ctl, Objects = ctl, SourceEnabled = ameAndCtlSources },
                new IcrCategoryInput { Category = RiskIndexCategory.Gov, Objects = govObjects }
            ],
            CriticalProcessWeight = M(5),
            CriticalProcessWeightCovered = M(5)
        };
    }
}

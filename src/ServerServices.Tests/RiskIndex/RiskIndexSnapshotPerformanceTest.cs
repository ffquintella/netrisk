using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// S40 §12.2 <c>RiskIndexSnapshotPerformanceTest</c>: a synthetic day of 50 000 objects over ~530 entity nodes, with a
/// previous day to attribute against — the compute half of the 05:00 job (composition, sensitivity, attribution, class
/// days, rows) inside the 10-minute budget. Explicit (xUnit v3): run with <c>-explicit only</c>, never by default.
/// </summary>
[TestSubject(typeof(RiskIndexSnapshotService))]
public class RiskIndexSnapshotPerformanceTest
{
    private static readonly TimeSpan Budget = TimeSpan.FromMinutes(10);
    private static readonly DateTime Now = new(2026, 10, 5, 5, 0, 0, DateTimeKind.Utc);

    [Fact(Explicit = true)]
    public void TestFiftyThousandObjectsFitTheBudget()
    {
        var (map, today, yesterday) = SyntheticDay(objects: 50_000, units: 20, subunitsPerUnit: 5, processesPerSubunit: 3,
            applications: 120);
        var p = new RiskIndexParameters();
        var set = new RiskIndexObjectSet
        {
            Rows = today, Map = map, IncidentIntakeActive = true,
            SourceEnabled = IcrNodeCalculator.Categories.ToDictionary(c => c, c => c != RiskIndexCategory.Ter)
        };
        var previous = new RiskIndexPreviousDay { Objects = yesterday };

        var clock = Stopwatch.StartNew();
        var day = RiskIndexDayCalculator.Compose(set, new RiskIndexDayRequest { Parameters = p, SnapshotInstant = Now, Previous = previous });
        var compose = clock.Elapsed;

        var profile = new RiskIndexProfile { Id = 1, Version = 1, Name = "v1", Parameters = "{}" };
        var rows = RiskIndexSnapshotService.BuildRows(day, profile, p, DateOnly.FromDateTime(Now), new string('A', 64), null, []);
        clock.Stop();

        var maxSteps = day.Nodes.Where(n => n.Change is not null).Max(n => n.Change!.Steps);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{today.Count} object rows, {day.Nodes.Count} nodes: compose (incl. attribution) {compose.TotalSeconds:0.0} s, " +
            $"rows {(clock.Elapsed - compose).TotalSeconds:0.0} s; {rows.Snapshots.Count} node rows, {rows.Attributions.Count} " +
            $"attributions, {rows.NodeMembers.Count} members, {rows.ClassDays.Count} class days; max attribution steps {maxSteps}"));

        Assert.True(day.Nodes.Count >= 500, $"{day.Nodes.Count} nodes");
        Assert.All(day.Nodes.Where(n => n.Node.ScopeKind == RiskIndexScopeKind.Organization), n => Assert.NotNull(n.Change));
        Assert.True(clock.Elapsed < Budget, $"The snapshot's compute half took {clock.Elapsed}, over the {Budget} budget");
    }

    /// <summary>
    /// An organization of units, subunits, processes (each linked to a few applications) and a day of hosts, scenarios and
    /// application findings; yesterday is today with a tenth of the values moved and a hundredth of the objects absent.
    /// </summary>
    private static (EntityMap Map, List<RiskIndexObjectRow> Today, List<RiskIndexObjectRow> Yesterday) SyntheticDay(
        int objects, int units, int subunitsPerUnit, int processesPerSubunit, int applications)
    {
        var random = new Random(247); // test data, not a secret
        var entities = new List<EntityMapRow> { new(1, RiskIndexEntityTypes.Organization, null) };
        var properties = new List<EntityPropertyRow>();
        var subunits = new List<int>();
        var processes = new List<int>();
        var apps = Enumerable.Range(90_000, applications).ToList();
        var next = 10;

        foreach (var app in apps) entities.Add(new EntityMapRow(app, RiskIndexEntityTypes.Application, null));
        for (var u = 0; u < units; u++)
        {
            var unit = next++;
            entities.Add(new EntityMapRow(unit, RiskIndexEntityTypes.OrganizationUnit, 1));
            for (var s = 0; s < subunitsPerUnit; s++)
            {
                var sub = next++;
                subunits.Add(sub);
                entities.Add(new EntityMapRow(sub, RiskIndexEntityTypes.SubOrganizationUnit, unit));
                for (var p = 0; p < processesPerSubunit; p++)
                {
                    var process = next++;
                    processes.Add(process);
                    entities.Add(new EntityMapRow(process, RiskIndexEntityTypes.BusinessProcess, sub));
                    properties.Add(new EntityPropertyRow(process, "criticality", random.Next(1, 6).ToString(CultureInfo.InvariantCulture)));
                    properties.Add(new EntityPropertyRow(process, RiskIndexEntityTypes.ProcessApplicationsProperty,
                        string.Join(",", Enumerable.Range(0, 2).Select(_ => apps[random.Next(apps.Count)]))));
                }
            }
        }

        var map = EntityMap.Build(entities, properties);
        var rows = new List<RiskIndexObjectRow>();
        var hosts = objects * 84 / 100;
        var scenarios = objects * 12 / 100;
        var findings = objects - hosts - scenarios;

        RiskIndexObjectRow Row(RiskIndexObjectKind kind, int id, RiskIndexCategory category, RiskIndexPopulation population,
            int crit, int scope, int attributed, RiskIndexAssetClass? assetClass) => new()
        {
            Key = new IcrObjectKey(kind, id.ToString(CultureInfo.InvariantCulture)), Category = category, Population = population,
            R = Math.Round(random.NextDouble() * random.NextDouble(), 4), Weight = IcrMath.Modulator(crit, 0.4), Criticality = crit,
            State = RiskIndexObjectState.Assessed, ScopeEntityId = scope, Attribution = [attributed], AssetClass = assetClass,
            SourceMask = assetClass is null ? 0 : RiskIndexSourceBits.Bit(RiskIndexSources.V1Device), Freshness = 1
        };

        for (var h = 0; h < hosts; h++)
        {
            var sub = subunits[h % subunits.Count];
            var crit = 1 + h % 5;
            var cls = h % 3 == 0 ? RiskIndexAssetClass.Workstation : RiskIndexAssetClass.Server;
            rows.Add(Row(RiskIndexObjectKind.Host, h, RiskIndexCategory.Exp, RiskIndexPopulation.Assets, crit, sub, sub, cls));
            if (h % 3 == 0)
                rows.Add(Row(RiskIndexObjectKind.Host, h, RiskIndexCategory.Gov, RiskIndexPopulation.Assets, crit, sub, sub, cls));
        }

        for (var s = 0; s < scenarios; s++)
        {
            var process = processes[s % processes.Count];
            var scope = subunits[s % subunits.Count];
            rows.Add(Row(RiskIndexObjectKind.Scenario, s, RiskIndexCategory.Reg, RiskIndexPopulation.Scenarios, 3, scope, process, null));
            rows.Add(Row(RiskIndexObjectKind.Scenario, s, RiskIndexCategory.Gov, RiskIndexPopulation.Scenarios, 3, scope, process, null));
        }

        for (var f = 0; f < findings; f++)
        {
            var app = apps[f % apps.Count];
            rows.Add(Row(RiskIndexObjectKind.Application, 500_000 + f, RiskIndexCategory.Exp, RiskIndexPopulation.Assets, 4, app, app,
                RiskIndexAssetClass.Application));
        }

        var yesterday = rows.Where((_, i) => i % 100 != 0)
            .Select((r, i) => i % 10 == 0 ? r with { R = Math.Clamp(r.R + 0.05, 0, 1) } : r)
            .ToList();
        return (map, rows, yesterday);
    }
}

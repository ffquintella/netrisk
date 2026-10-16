using System.Globalization;
using System.Net;
using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.RiskIndex;
using ServerServices.Findings;
using ServerServices.Integrations.SecurityScorecard;
using ServerServices.Integrations.TrendMicro;
using ServerServices.RiskIndex.Engine;
using ServerServices.RiskIndex.Signals;

namespace ServerServices.RiskIndex;

public static partial class RiskIndexObjectCollector
{
    internal sealed record ExposureResult(IReadOnlyDictionary<int, double> FreshHostExposure, int Disagreements, bool AnySlaSource);

    private const string VisionOneImporter = "trendmicro-visionone";
    private const string ScorecardImporter = "securityscorecard";
    private const string AssessmentImporter = "assessment";
    private const string NessusImporter = "nessus";
    private const string JiraAssetsProvider = "JiraAssets";
    private const short RetiredHost = 27;

    private sealed record FindingRow(int Id, int? HostId, int? EntityId, int Status, FindingStatus Lifecycle,
        string? ImportSource, string? Severity, float? Cvss3, float? Cvss2, float? Vpr, bool? ExploitAvailable,
        bool? ExploitedByScanner, double? Epss, string? Cves, string? RuleId, string? Cwes, string? Location,
        string Title, DateTime FirstDetection, DateTime LastDetection, DateTime? SlaDueDate, int? LastImportId);

    /// <summary>One factor reading of an exposure object before aggregation.</summary>
    private sealed record Reading(string Factor, RiskIndexFactorType Type, double X, double Validity, double Ceiling,
        DateTime? ReadAt, string Source, IReadOnlyList<RiskIndexSignalReading> Signals);

    /// <summary>The source of a finding by its importer (S39 §3.2), or null for the ones that never score.</summary>
    private static string? FindingSource(string? importSource) => importSource?.ToLowerInvariant() switch
    {
        null => RiskIndexSources.Importers,
        ScorecardImporter or AssessmentImporter => null,
        VisionOneImporter => RiskIndexSources.V1Cve,
        NessusImporter => RiskIndexSources.TenableNessus,
        _ => RiskIndexSources.Importers
    };

    private static async Task<ExposureResult> CollectExposureAsync(NRDbContext db, Run run, CancellationToken ct)
    {
        var findings = await db.Vulnerabilities.AsNoTracking()
            .Select(v => new FindingRow(v.Id, v.HostId, v.EntityId, v.Status, v.LifecycleStatus, v.ImportSource, v.Severity,
                v.Cvss3BaseScore, v.CvssBaseScore, v.VprScore, v.ExploitAvaliable, v.ExploitedByScanner, v.EpssScore, v.Cves,
                v.RuleId, v.Cwes, v.Location, v.Title, v.FirstDetection, v.LastDetection, v.SlaDueDate, v.LastImportId))
            .ToListAsync(ct);

        // D-04 interim clause (S39 §3.3): a legacy-closed finding the lifecycle holds open stays closed unless it was
        // detected again after its last recorded action.
        var suspects = findings.Where(f => LegacyClosed(f.Status) &&
                                           f.Lifecycle is FindingStatus.Active or FindingStatus.Verified or FindingStatus.RiskAccepted)
            .Select(f => f.Id).ToList();
        var lastAction = suspects.Count == 0
            ? new Dictionary<int, DateTime?>()
            : await db.Vulnerabilities.AsNoTracking().Where(v => suspects.Contains(v.Id))
                .Select(v => new { v.Id, Last = v.Actions.Max(a => (DateTime?)a.DateTime) })
                .ToDictionaryAsync(v => v.Id, v => v.Last, ct);
        bool ClosedByLegacy(FindingRow f) => lastAction.TryGetValue(f.Id, out var last) && last is { } t && f.LastDetection <= t;

        var kev = run.Mode == RiskIndexMode.Full
            ? (await db.KevEntries.AsNoTracking().Where(k => k.DelistedAt == null).Select(k => k.CveId).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();

        bool Exposed(FindingRow f) => f.Lifecycle is FindingStatus.Active or FindingStatus.Verified or FindingStatus.RiskAccepted &&
                                      FindingSource(f.ImportSource) is { } s && run.On(s) && !ClosedByLegacy(f);
        bool OpenForSla(FindingRow f) => f.Lifecycle.IsOpen() && FindingSource(f.ImportSource) is { } s && run.On(s) &&
                                         !ClosedByLegacy(f);

        var fresh = new Dictionary<int, double>();
        await CollectHostsAsync(db, run, findings, Exposed, OpenForSla, kev, fresh, ct);
        await CollectDomainsAsync(db, run, ct);
        await CollectApplicationsAsync(db, run, findings, Exposed, OpenForSla, kev, ct);

        var anySla = new[] { RiskIndexSources.V1Cve, RiskIndexSources.TenableNessus, RiskIndexSources.Importers }.Any(run.On);
        return new ExposureResult(fresh, suspects.Count, anySla);
    }

    private static async Task CollectHostsAsync(NRDbContext db, Run run, List<FindingRow> findings,
        Func<FindingRow, bool> exposed, Func<FindingRow, bool> openForSla, HashSet<string> kev,
        Dictionary<int, double> freshExposure, CancellationToken ct)
    {
        var p = run.P;
        var hosts = await db.Hosts.AsNoTracking()
            .Where(h => h.Status != RetiredHost && (h.ExternalProvider == null || h.ExternalProvider != SecurityScorecardService.ProviderName))
            .Select(h => new
            {
                h.Id, h.Os, h.Source, h.Criticality, h.ExternalProvider, h.RiskScore, h.RiskScoreUpdatedAt, h.Environment,
                h.EntityId, h.LastVerificationDate, h.Ip, h.RegistrationDate
            })
            .ToListAsync(ct);

        var cmdb = (await db.JiraAssetObjects.AsNoTracking().Where(j => j.TargetHostId != null)
                .Select(j => new { HostId = j.TargetHostId!.Value, j.ObjectTypeName }).ToListAsync(ct))
            .GroupBy(j => j.HostId).ToDictionary(g => g.Key, g => g.First().ObjectTypeName);

        var byHost = findings.Where(f => f.HostId is not null).GroupBy(f => f.HostId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var deviceOn = run.On(RiskIndexSources.V1Device);
        var scanSources = new[] { RiskIndexSources.TenableNessus, RiskIndexSources.Importers }.Where(run.On).ToList();
        var anySource = deviceOn || scanSources.Count > 0 || run.On(RiskIndexSources.V1Cve) || run.On(RiskIndexSources.TenableAes);

        foreach (var h in hosts.OrderBy(h => h.Id))
        {
            var own = byHost.GetValueOrDefault(h.Id) ?? [];
            var cmdbLinked = cmdb.ContainsKey(h.Id);
            var (crit, critSource) = HostCriticality(run, h.Criticality, h.ExternalProvider, h.Environment);
            var m = run.M(crit);
            var environment = NormalizeEnvironment(p, h.Environment);
            var internet = p.PublicIpHeuristic && IsPublic(h.Ip, p.InternalPublicRanges);

            var readings = new List<Reading>();
            var hasDevice = deviceOn && h.RiskScore is not null;
            if (hasDevice)
            {
                var age = h.RiskScoreUpdatedAt is { } at ? IcrMath.Days(at, run.Now) : double.PositiveInfinity;
                readings.Add(new Reading(RiskIndexFactors.ExpDevice, RiskIndexFactorType.C, IcrMath.Clamp01(h.RiskScore!.Value / 100.0),
                    IcrMath.Validity(age, run.W(RiskIndexSources.V1Device)), run.U(RiskIndexFactors.ExpDevice),
                    h.RiskScoreUpdatedAt, RiskIndexSources.V1Device, []));
            }

            // EXP.V from scanners and importers: read at the latest detection of their findings (any status), or the
            // verification date of a host no provider or CMDB stamps (S39 §8.2, D-18 interim rule).
            if (scanSources.Count > 0)
            {
                var scanFindings = own.Where(f => FindingSource(f.ImportSource) is { } s && scanSources.Contains(s)).ToList();
                DateTime? readAt = scanFindings.Count > 0 ? scanFindings.Max(f => f.LastDetection) : null;
                if (h.ExternalProvider is null && !cmdbLinked && h.LastVerificationDate is { } verified &&
                    (readAt is null || verified > readAt))
                    readAt = verified;

                if (readAt is { } date)
                {
                    var window = scanSources.Min(run.W);
                    readings.Add(FindingReadingOf(run, scanFindings.Where(exposed), m, internet, kev, date, window,
                        scanFindings.Any(f => FindingSource(f.ImportSource) == RiskIndexSources.TenableNessus)
                            ? RiskIndexSources.TenableNessus
                            : RiskIndexSources.Importers, visionOne: false));
                }
            }

            // Vision One CVEs only while the host has no device score (S39 §3.4 rule 1).
            if (run.On(RiskIndexSources.V1Cve) && !hasDevice)
            {
                var v1 = own.Where(f => FindingSource(f.ImportSource) == RiskIndexSources.V1Cve).ToList();
                if (v1.Count > 0)
                    readings.Add(FindingReadingOf(run, v1.Where(exposed), m, internet, kev, v1.Max(f => f.LastDetection),
                        run.W(RiskIndexSources.V1Cve), RiskIndexSources.V1Cve, visionOne: true));
            }

            // Hosts with no lifecycle source leave the inventory after janela_inventario_sinal without a signal.
            var lastSign = readings.Select(r => r.ReadAt).Append((DateTime?)h.RegistrationDate).Max();
            if (h.ExternalProvider is null && !cmdbLinked && lastSign is { } sign &&
                IcrMath.Days(sign, run.Now) > p.InventorySignalWindowDays)
                continue;

            var key = new IcrObjectKey(RiskIndexObjectKind.Host, h.Id.ToString(CultureInfo.InvariantCulture));
            var attribution = h.EntityId is { } e ? new[] { e } : [];
            var assetClass = AssetClassOf(h.Os, cmdb.GetValueOrDefault(h.Id));
            var findingSources = scanSources.Count > 0 || run.On(RiskIndexSources.V1Cve);
            var rMax = Math.Max(deviceOn ? run.U(RiskIndexFactors.ExpDevice) : 0,
                findingSources ? run.U(RiskIndexFactors.ExpVulnerabilities) * m : 0);
            var uBar = Math.Max(deviceOn ? run.U(RiskIndexFactors.ExpDevice) : 0,
                findingSources ? run.U(RiskIndexFactors.ExpVulnerabilities) : 0);

            if (anySource)
            {
                var row = ExposureRow(run, key, readings, m, rMax, uBar) with
                {
                    Criticality = crit, CriticalitySource = critSource, ScopeEntityId = h.EntityId, Attribution = attribution,
                    AssetClass = assetClass, Environment = environment
                };
                run.Rows.Add(row);

                freshExposure[h.Id] = readings.Where(r => r.Validity >= 1)
                    .Select(r => r.Ceiling * (r.Type == RiskIndexFactorType.V ? m * r.X : r.X)).DefaultIfEmpty(0).Max();
            }

            AddSla(run, key, own.Where(openForSla).ToList(), crit, critSource, m, h.EntityId, attribution, assetClass, environment);
        }
    }

    /// <summary>
    /// A host's own criticality (S39 §5.1, D-10 interim): the stored value, labelled by the provider that owns the host
    /// (Vision One capped at <c>crit_max_vision_one</c>), else the default — lower in a non-production environment.
    /// </summary>
    private static (int Criticality, string Source) HostCriticality(Run run, int? stored, string? provider, string? environment)
    {
        if (stored is { } value)
        {
            var c = Math.Clamp(value, 1, 5);
            return provider switch
            {
                TrendMicroService.ProviderName => (Math.Min(c, run.P.VisionOneCriticalityCeiling), "visionone"),
                JiraAssetsProvider => (c, "cmdb"),
                _ => (c, "unknown")
            };
        }

        var nonProduction = run.On(RiskIndexSources.CmdbEnvironment) && NormalizeEnvironment(run.P, environment) is { } env &&
                            env is not ("prod" or "none");
        return (nonProduction ? run.P.DefaultNonProductionCriticality : run.P.DefaultCriticality, "default");
    }

    /// <summary>The environment by the profile vocabulary (S40 §7.1): the matching term, <c>prod</c>, or <c>none</c>.</summary>
    internal static string NormalizeEnvironment(RiskIndexParameters p, string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment)) return "none";
        var text = environment.Trim().ToLowerInvariant();
        return p.NonProductionEnvironments.FirstOrDefault(t => text.Contains(t.ToLowerInvariant())) ?? "prod";
    }

    /// <summary>The interim asset class by rules on the OS and the CMDB object type (S39 §5.4).</summary>
    internal static RiskIndexAssetClass AssetClassOf(string? os, string? cmdbType)
    {
        var text = $"{os} {cmdbType}".ToLowerInvariant();
        string[] network = ["cisco", "juniper", "junos", "fortinet", "fortios", "routeros", "pan-os", "palo alto", "nx-os", "switch", "router", "firewall"];
        string[] server = ["server", "linux", "ubuntu", "centos", "debian", "red hat", "rhel", "suse", "freebsd", "unix", "aix", "solaris", "esxi"];
        string[] workstation = ["windows", "macos", "mac os", "os x", "workstation", "desktop", "laptop", "notebook"];

        if (network.Any(text.Contains)) return RiskIndexAssetClass.Network;
        if (server.Any(text.Contains)) return RiskIndexAssetClass.Server;
        if (workstation.Any(text.Contains)) return RiskIndexAssetClass.Workstation;
        return RiskIndexAssetClass.Unknown;
    }

    /// <summary>Whether an IPv4 is public: outside RFC 1918, RFC 6598, loopback and link-local, and outside the ranges the
    /// organization uses internally (<c>faixas_publicas_internas</c>, S39 §5.3).</summary>
    internal static bool IsPublic(string? ip, IReadOnlyList<string> internalRanges)
    {
        if (!IPAddress.TryParse(ip?.Trim(), out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        string[] privateRanges = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16"];
        return !privateRanges.Concat(internalRanges).Any(range => IPNetwork.TryParse(range, out var network) && network.Contains(address));
    }

    /// <summary>
    /// One EXP.V reading from a set of findings (S39 §3.1, §3.4): each finding's anchor signal, deduplicated by CVE (else
    /// by tool and rule), combined by MRA over the K largest. A Vision One finding's flag earns <c>m_x</c>, not the floor.
    /// </summary>
    private static Reading FindingReadingOf(Run run, IEnumerable<FindingRow> findings, double m, bool internet,
        HashSet<string> kev, DateTime readAt, double window, string source, bool visionOne)
    {
        var p = run.P;
        var signals = new Dictionary<string, (double Value, int Id, FindingSignal Signal)>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in findings)
        {
            var cves = (f.Cves ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim().ToUpperInvariant()).OrderBy(c => c).ToList();
            var reading = new FindingReading
            {
                Vpr = f.Vpr, Cvss3 = f.Cvss3, Cvss2 = f.Cvss2,
                Severity = (int)SlaService.ParseSeverity(f.Severity, f.Cvss3 ?? f.Cvss2),
                ExploitAvailable = f.ExploitAvailable == true,
                Epss = f.Epss,
                ExploitationKnown = !visionOne && (f.ExploitedByScanner == true || cves.Any(kev.Contains)),
                InternetFacing = internet
            };
            if (IcrAnchors.Finding(p, reading) is not { } signal || signal.Value <= 0) continue;

            var key = cves.Count > 0 ? string.Join(",", cves) : $"{f.ImportSource}|{f.RuleId ?? f.Cwes ?? f.Title}|{f.Location}";
            if (!signals.TryGetValue(key, out var existing) || signal.Value > existing.Value)
                signals[key] = (signal.Value, f.Id, signal);
        }

        var x = IcrMath.Mra(signals.Values.Select(s => s.Value), p.Rho, p.KSignals);
        var stored = signals.Values.OrderByDescending(s => s.Value).Take(50)
            .Select(s => new RiskIndexSignalReading(RiskIndexFactors.ExpVulnerabilities, $"finding:{s.Id}", s.Value, Flags(s.Signal)))
            .ToList();

        return new Reading(RiskIndexFactors.ExpVulnerabilities, RiskIndexFactorType.V, x,
            IcrMath.Validity(IcrMath.Days(readAt, run.Now), window), run.U(RiskIndexFactors.ExpVulnerabilities), readAt, source, stored);
    }

    private static IReadOnlyList<string> Flags(FindingSignal s)
    {
        var flags = new List<string> { s.BaseSource.ToString().ToLowerInvariant() };
        if (s.ExploitMultiplier) flags.Add("m_x");
        if (s.ExploitationFloor) flags.Add("exploited");
        if (s.InternetMultiplier) flags.Add("m_net");
        return flags;
    }

    /// <summary>
    /// The EXP row of an object from its readings (S39 §4.2, §8.1–§8.3): assessed when some reading has <c>v &gt; 0</c>,
    /// valued <c>max_f u_f·F_eff</c>; presumed otherwise, at <c>max_f u_f·max(F, π·m)</c> or <c>ū·π·m</c> without readings.
    /// </summary>
    private static RiskIndexObjectRow ExposureRow(Run run, IcrObjectKey key, List<Reading> readings, double m, double rMax,
        double uBar)
    {
        var p = run.P;
        double F(Reading r) => r.Type == RiskIndexFactorType.V ? m * r.X : r.X;

        double r;
        RiskIndexObjectState state;
        if (readings.Count == 0)
        {
            r = uBar * p.PiPrior * m;
            state = RiskIndexObjectState.Presumed;
        }
        else if (readings.All(x => x.Validity <= 0))
        {
            r = readings.Max(x => x.Ceiling * Math.Max(F(x), p.PiPrior * m));
            state = RiskIndexObjectState.Presumed;
        }
        else
        {
            r = readings.Max(x => x.Ceiling * IcrMath.EffectiveFactor(F(x), x.Validity, p.PiPrior, m));
            state = readings.Any(x => x.Validity >= 1)
                ? r > 0 ? RiskIndexObjectState.Assessed : RiskIndexObjectState.AssessedClean
                : RiskIndexObjectState.Stale;
        }

        return new RiskIndexObjectRow
        {
            Key = key,
            Category = RiskIndexCategory.Exp,
            Population = RiskIndexPopulation.Assets,
            R = IcrMath.Clamp01(r),
            Weight = m,
            State = state,
            Factors = readings.Select(x => new RiskIndexFactorReading
            {
                Factor = x.Factor, Type = x.Type, X = x.X, Validity = x.Validity, Ceiling = x.Ceiling, ReadAt = x.ReadAt,
                Source = x.Source
            }).ToList(),
            Signals = readings.SelectMany(x => x.Signals).ToList(),
            SourceMask = RiskIndexSourceBits.Mask(readings.Select(x => (string?)x.Source)),
            Freshness = readings.Count == 0 ? 0 : readings.Max(x => x.Validity),
            RMax = Math.Max(rMax, r)
        };
    }

    /// <summary>
    /// The GOV row of an asset (S39 §6.3 SLA): <c>m(crit)·u_T·SLA</c>; not assessed while an open finding has no SLA date
    /// (§8.3) — it then counts only in the coverage denominator.
    /// </summary>
    private static void AddSla(Run run, IcrObjectKey key, List<FindingRow> open, int crit, string critSource, double m,
        int? scope, IReadOnlyList<int> attribution, RiskIndexAssetClass assetClass, string? environment)
    {
        if (!new[] { RiskIndexSources.V1Cve, RiskIndexSources.TenableNessus, RiskIndexSources.Importers }.Any(run.On)) return;

        var u = run.U(RiskIndexFactors.GovTreatment);
        var unassessed = open.Any(f => f.SlaDueDate is null);
        var sla = unassessed
            ? 0
            : GovernanceSignals.Sla(run.P, open.Select(f => new GovernanceSignals.SlaFinding(
                (int)SlaService.ParseSeverity(f.Severity, f.Cvss3 ?? f.Cvss2), f.FirstDetection, f.SlaDueDate!.Value)), run.Now);
        var r = m * u * sla;

        run.Rows.Add(new RiskIndexObjectRow
        {
            Key = key, Category = RiskIndexCategory.Gov, Population = RiskIndexPopulation.Assets, R = IcrMath.Clamp01(r),
            Weight = m, Criticality = crit, CriticalitySource = critSource,
            State = unassessed ? RiskIndexObjectState.NotAssessed
                : r > 0 ? RiskIndexObjectState.Assessed : RiskIndexObjectState.AssessedClean,
            ScopeEntityId = scope, Attribution = attribution, AssetClass = assetClass, Environment = environment,
            Factors = [new RiskIndexFactorReading { Factor = RiskIndexFactors.GovTreatment, Type = RiskIndexFactorType.V, X = sla, Ceiling = u }],
            Signals = sla > 0 ? [new RiskIndexSignalReading(RiskIndexFactors.GovTreatment, "SLA", sla, [])] : [],
            SourceMask = RiskIndexSourceBits.Mask(open.Select(f => FindingSource(f.ImportSource))),
            RMax = m * u
        });
    }

    /// <summary>One object per active SecurityScorecard connection: its latest overall rating (S39 §2.1, §3.1 SSC-TOT).</summary>
    private static async Task CollectDomainsAsync(NRDbContext db, Run run, CancellationToken ct)
    {
        if (!run.On(RiskIndexSources.SscTotal)) return;

        var connections = await db.SecurityScorecardConnections.AsNoTracking().Where(c => c.Enabled)
            .Select(c => new { c.Id, c.EntityId }).ToListAsync(ct);
        var ids = connections.Select(c => c.Id).ToList();
        var latest = (await db.SecurityScorecardFactors.AsNoTracking().Where(f => f.IsOverall && ids.Contains(f.ConnectionId))
                .Select(f => new { f.ConnectionId, f.Score, f.CapturedAt, f.Id }).ToListAsync(ct))
            .GroupBy(f => f.ConnectionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.CapturedAt).ThenByDescending(f => f.Id).First());

        var crit = Math.Clamp(run.P.ExternalCriticality, 1, 5);
        var m = run.M(crit);
        foreach (var c in connections.OrderBy(c => c.Id))
        {
            var readings = new List<Reading>();
            if (latest.TryGetValue(c.Id, out var f))
                readings.Add(new Reading(RiskIndexFactors.ExpExternal, RiskIndexFactorType.V, IcrAnchors.Ssc(run.P, f.Score),
                    IcrMath.Validity(IcrMath.Days(f.CapturedAt, run.Now), run.W(RiskIndexSources.SscTotal)),
                    run.U(RiskIndexFactors.ExpExternal), f.CapturedAt, RiskIndexSources.SscTotal,
                    [new RiskIndexSignalReading(RiskIndexFactors.ExpExternal, $"ssc:{f.Score}", IcrAnchors.Ssc(run.P, f.Score), [])]));

            var key = new IcrObjectKey(RiskIndexObjectKind.ExternalDomain, c.Id.ToString(CultureInfo.InvariantCulture));
            run.Rows.Add(ExposureRow(run, key, readings, m, run.U(RiskIndexFactors.ExpExternal) * m, run.U(RiskIndexFactors.ExpExternal)) with
            {
                Criticality = crit, CriticalitySource = "external", ScopeEntityId = c.EntityId,
                Attribution = c.EntityId is { } e ? [e] : [], AssetClass = RiskIndexAssetClass.ExternalDomain
            });
        }
    }

    /// <summary>
    /// Applications with exposed host-less findings (S39 §2.1, §3.4 rule 3): one object per <c>vulnerabilities.entity_id</c>;
    /// without an entity, one "Unassigned" group per importer and last import, so distinct repositories never merge.
    /// </summary>
    private static async Task CollectApplicationsAsync(NRDbContext db, Run run, List<FindingRow> findings,
        Func<FindingRow, bool> exposed, Func<FindingRow, bool> openForSla, HashSet<string> kev, CancellationToken ct)
    {
        var hostless = findings.Where(f => f.HostId is null && exposed(f)).ToList();
        if (hostless.Count == 0) return;

        var entityIds = hostless.Where(f => f.EntityId is not null).Select(f => f.EntityId!.Value).Distinct().ToList();
        var importIds = hostless.Where(f => f.LastImportId is not null).Select(f => f.LastImportId!.Value).Distinct().ToList();
        var imports = await db.ScanImports.AsNoTracking()
            .Where(i => (i.EntityId != null && entityIds.Contains(i.EntityId.Value)) || importIds.Contains(i.Id))
            .Select(i => new { i.Id, i.EntityId, At = i.FinishedAt ?? i.StartedAt }).ToListAsync(ct);
        var lastImportByEntity = imports.Where(i => i.EntityId is not null).GroupBy(i => i.EntityId!.Value)
            .ToDictionary(g => g.Key, g => g.Max(i => i.At));
        var importAt = imports.ToDictionary(i => i.Id, i => i.At);

        foreach (var group in hostless.GroupBy(f => f.EntityId is { } e ? $"e:{e}" : $"u:{f.ImportSource}:{f.LastImportId}")
                     .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            var entity = first.EntityId;
            var (crit, critSource) = EntityCriticality(run, entity);
            var m = run.M(crit);
            var internet = entity is { } id && run.On(RiskIndexSources.EntityInternetFacing) && run.Map.InternetFacing(id) == true;

            DateTime readAt = entity is { } ent && lastImportByEntity.TryGetValue(ent, out var at) ? at
                : first.LastImportId is { } import && importAt.TryGetValue(import, out var ia) ? ia
                : group.Max(f => f.LastDetection);
            var source = group.Any(f => FindingSource(f.ImportSource) == RiskIndexSources.TenableNessus)
                ? RiskIndexSources.TenableNessus
                : RiskIndexSources.Importers;
            var reading = FindingReadingOf(run, group, m, internet, kev, readAt, run.W(source), source, visionOne: false);

            var key = entity is { } e2
                ? new IcrObjectKey(RiskIndexObjectKind.Application, e2.ToString(CultureInfo.InvariantCulture))
                : new IcrObjectKey(RiskIndexObjectKind.UnassignedGroup, $"findings:{first.ImportSource}:{first.LastImportId}");
            var attribution = entity is { } e3 ? new[] { e3 } : [];

            run.Rows.Add(ExposureRow(run, key, [reading], m, run.U(RiskIndexFactors.ExpVulnerabilities) * m,
                run.U(RiskIndexFactors.ExpVulnerabilities)) with
            {
                Criticality = crit, CriticalitySource = critSource, ScopeEntityId = entity, Attribution = attribution,
                AssetClass = RiskIndexAssetClass.Application
            });

            var open = findings.Where(f => f.HostId is null && openForSla(f) &&
                                           (entity is null ? f.EntityId is null && f.ImportSource == first.ImportSource &&
                                                             f.LastImportId == first.LastImportId
                                               : f.EntityId == entity)).ToList();
            AddSla(run, key, open, crit, critSource, m, entity, attribution, RiskIndexAssetClass.Application, null);
        }
    }
}

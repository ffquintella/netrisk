using System.Globalization;
using Model.Risks.Chain;

namespace Tools.Risks;

/// <summary>
/// The Phase 7 critical-process coverage metric (Stage 9.1, S41 §6, T147), as a pure function of the
/// chain graph, the visible entity links and the set of open risks. The service supplies those three
/// through the caller's scoped context; nothing here can see a risk the caller cannot.
///
/// The rules, each held by a test in <c>CriticalProcessCoverageCalculatorTest</c>:
/// <list type="bullet">
/// <item>Only <b>active</b> processes enter at all; an inactive one is neither counted nor reported.</item>
/// <item>A process is critical when its <b>effective</b> criticality is 4 or 5 (Stage 9.3, S43 §4.6):
/// the one its BIA's MTPD maps to when declared, which wins, else the declared <c>criticality</c>.
/// Neither — absent, empty, non-numeric or outside 1–5 with no MTPD — is "without criticality", counted
/// in its own figure, never silently treated as "not critical", and outside the denominator.</item>
/// <item>A critical process is covered when at least one <b>open</b> risk is linked to it directly or
/// to a node below it. A risk linked both ways counts once, as direct.</item>
/// <item>No critical process at all gives a null ratio — "not computable" — never 0 % or 100 %.</item>
/// <item>The ratio is the exact decimal quotient; any rounding is the presentation's job.</item>
/// </list>
/// </summary>
public static class CriticalProcessCoverageCalculator
{
    public static CriticalProcessCoverageDto Compute(
        RiskChainGraph graph,
        IEnumerable<(int RiskId, int EntityId)> entityLinks,
        IReadOnlySet<int> openRiskIds,
        DateTime computedAtUtc,
        bool isScopeRestricted,
        IReadOnlyDictionary<int, int>? biaMtpdByProcess = null)
    {
        var risksByNode = new Dictionary<int, HashSet<int>>();

        foreach (var (riskId, entityId) in entityLinks)
        {
            if (!openRiskIds.Contains(riskId)) continue;

            if (!risksByNode.TryGetValue(entityId, out var risks))
                risksByNode[entityId] = risks = new HashSet<int>();
            risks.Add(riskId);
        }

        var result = new CriticalProcessCoverageDto
        {
            ComputedAt = computedAtUtc,
            Threshold = RiskChainSchema.CriticalThreshold,
            IsScopeRestricted = isScopeRestricted
        };

        foreach (var process in graph.Nodes.Values)
        {
            if (!string.Equals(process.DefinitionName, RiskChainSchema.ProcessDefinition, StringComparison.Ordinal))
                continue;
            if (!IsActive(process)) continue;

            var (criticality, source) = Continuity.ProcessCriticality.Resolve(
                biaMtpdByProcess is not null && biaMtpdByProcess.TryGetValue(process.Id, out var mtpd) ? mtpd : (int?)null,
                process.Value(RiskChainSchema.CriticalityProperty));

            if (criticality is null)
            {
                result.ProcessesWithoutCriticality++;
                continue;
            }

            if (criticality < RiskChainSchema.CriticalThreshold) continue;

            var direct = risksByNode.TryGetValue(process.Id, out var linked) ? linked : new HashSet<int>();

            var inferred = new HashSet<int>();
            foreach (var below in graph.Below(process.Id).Keys)
            {
                if (!risksByNode.TryGetValue(below, out var risks)) continue;
                foreach (var risk in risks)
                    if (!direct.Contains(risk)) inferred.Add(risk);
            }

            result.Rows.Add(new CriticalProcessCoverageRowDto
            {
                ProcessId = process.Id,
                ProcessName = process.Name ?? string.Empty,
                Criticality = criticality.Value,
                CriticalitySource = source ?? Model.Continuity.CriticalitySource.Declared,
                DirectOpenRiskCount = direct.Count,
                InferredOpenRiskCount = inferred.Count,
                Covered = direct.Count + inferred.Count > 0
            });
        }

        result.Rows = result.Rows
            .OrderByDescending(r => r.Criticality)
            .ThenBy(r => r.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ProcessId)
            .ToList();

        result.CriticalProcessCount = result.Rows.Count;
        result.CoveredCount = result.Rows.Count(r => r.Covered);
        result.CoverageRatio = result.CriticalProcessCount == 0
            ? null
            : (decimal)result.CoveredCount / result.CriticalProcessCount;

        return result;
    }

    /// <summary>
    /// The declared criticality, 1–5, or null when it is absent or not a valid one. The entity form
    /// writes an empty integer field as "0", which is why 0 means "not declared" rather than "lowest".
    /// </summary>
    public static int? ParseCriticality(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return null;

        return value is >= RiskChainSchema.MinCriticality and <= RiskChainSchema.MaxCriticality ? value : null;
    }

    /// <summary>
    /// A process is active unless its <c>isActive</c> says false. The property is mandatory with a
    /// default of "True", so an entity saved without it predates the property and takes the default.
    /// </summary>
    public static bool IsActive(RiskChainNode node)
    {
        var raw = node.Value(RiskChainSchema.IsActiveProperty);
        return raw is null || !bool.TryParse(raw, out var active) || active;
    }
}

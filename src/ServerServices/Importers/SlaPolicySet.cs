using Contracts.Importers;
using DAL.Entities;

namespace ServerServices.Importers;

/// <summary>
/// The SLA policy table, read once and resolved in memory for the length of one import.
///
/// The pipeline used to resolve a due date per finding, from two places that each ran their own
/// query. The table holds a handful of rows and does not change during an import, so half a million
/// findings meant half a million queries for the same few answers.
/// </summary>
public sealed class SlaPolicySet
{
    private readonly List<SlaConfiguration> _policies;
    private readonly Dictionary<(int Severity, DateTime At), SlaConfiguration?> _resolved = new();

    public SlaPolicySet(List<SlaConfiguration> policies)
    {
        _policies = policies;
    }

    /// <summary>
    /// The deadline the policy in force at <paramref name="firstSeenUtc"/> sets, or null when no
    /// policy covers that severity.
    ///
    /// Resolved as of first-seen rather than now: the finding's deadline is the one the policy
    /// promised when it appeared, which is why the policy table is effective-dated.
    /// </summary>
    public DateTime? DueDate(NormalizedSeverity severity, int? entityId, DateTime firstSeenUtc)
    {
        var policy = Resolve((int)severity, entityId, firstSeenUtc);

        return policy == null ? null : firstSeenUtc.AddDays(policy.MaxRemediationDays);
    }

    /// <summary>
    /// The winning policy, under the same precedence the database query used: an entity override
    /// beats the global default, and among equals the most recently effective row wins.
    /// </summary>
    private SlaConfiguration? Resolve(int severity, int? entityId, DateTime atUtc)
    {
        // Memoized because an import's findings share very few distinct (severity, first-seen)
        // pairs, and the scan below is otherwise repeated once per finding.
        if (_resolved.TryGetValue((severity, atUtc), out var cached)) return cached;

        var policy = _policies
            .Where(c => c.Severity == severity
                        && c.EffectiveFrom <= atUtc
                        && (c.EffectiveTo == null || c.EffectiveTo > atUtc)
                        && (c.EntityId == null || c.EntityId == entityId))
            .OrderByDescending(c => c.EntityId != null)
            .ThenByDescending(c => c.EffectiveFrom)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();

        _resolved[(severity, atUtc)] = policy;

        return policy;
    }
}

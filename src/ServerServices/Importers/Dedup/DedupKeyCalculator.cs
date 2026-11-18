using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Importers.Dedup;

/// <summary>
/// A resolved dedup chain, applied to as many findings as the caller has.
///
/// This holds the result of the work <see cref="DeduplicationService.ComputeKeyAsync"/> used to redo
/// per finding: parsing the field set, and resolving each configured strategy name against the
/// built-ins and the enabled plugins — which enumerates the plugin directory and loads each one.
/// </summary>
internal sealed class DedupKeyCalculator(
    ILogger logger,
    List<IDeduplicationStrategy> strategies,
    DedupFieldSet fields)
    : IDedupKeyCalculator
{
    public DedupKeyResult ComputeKey(DedupContext context)
    {
        var candidates = new List<DedupCandidate>(strategies.Count);

        foreach (var strategy in strategies)
        {
            string? key;

            try
            {
                key = strategy.ComputeKey(context, fields);
            }
            catch (Exception ex)
            {
                // A plugin strategy that throws must not fail the import. Skipping it degrades dedup
                // for that finding, which is recoverable; aborting the scan is not.
                logger.Warning("Deduplication strategy {Strategy} threw for finding {Title}: {Message}",
                    strategy.Name, context.Finding.Title, ex.Message);
                continue;
            }

            if (string.IsNullOrWhiteSpace(key)) continue;

            candidates.Add(new DedupCandidate(strategy.Name, key, strategy.MatchesLegacyImportHash));
        }

        return new DedupKeyResult(WithRepository(candidates, context));
    }

    /// <summary>
    /// Folds the import's repository into every key of a host-less finding (M58, T282, S39 §3.4, D-21): a SARIF rule on
    /// <c>src/app.js</c>, a Snyk issue id and a Dependabot alert number all recur across repositories, and a key without
    /// the repository merges them into one finding. The repository-less keys follow as adoption-only candidates, so
    /// the first repository to re-import a finding from before repositories were named takes it over instead of
    /// duplicating it. A host finding is keyed by its host; an import that names no repository keeps every key as it was.
    /// </summary>
    internal static List<DedupCandidate> WithRepository(List<DedupCandidate> candidates, DedupContext context)
    {
        var repository = context.Repository?.Trim();
        if (string.IsNullOrEmpty(repository) || context.HostId != null || candidates.Count == 0) return candidates;

        var scoped = candidates.Select(c => new DedupCandidate(c.Strategy,
            DedupHash.Sha256($"repository\u001f{repository}\u001f{c.Key}"), MatchesLegacyImportHash: false));
        var adoptable = candidates.Select(c => c with { MatchesLegacyImportHash = false, OnlyUnattributed = true });

        return scoped.Concat(adoptable).ToList();
    }
}

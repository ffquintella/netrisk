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

        return new DedupKeyResult(candidates);
    }
}

using Contracts.Importers;

namespace ServerServices.Importers.Dedup;

/// <summary>
/// Everything a deduplication strategy is allowed to see: the parsed finding plus the database ids
/// the ingestion pipeline resolved for its asset.
///
/// The host/service ids matter because NetRisk's historical Nessus hash was built from them, so a
/// strategy that has to reproduce that hash cannot work from the report alone.
/// </summary>
public class DedupContext
{
    public required NormalizedFinding Finding { get; init; }

    /// <summary>The <c>hosts</c> row the finding was attached to, if any.</summary>
    public int? HostId { get; init; }

    /// <summary>The <c>hosts_services</c> row, if the finding sits on a service.</summary>
    public int? HostServiceId { get; init; }

    /// <summary>The tenant the import is scoped to. Part of no key — dedup is scoped by the query,
    /// not by the key — but available to a custom strategy that wants it.</summary>
    public int? EntityId { get; init; }
}

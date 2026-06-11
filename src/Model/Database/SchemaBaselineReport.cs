namespace Model.Database;

/// <summary>
/// Output of <c>database baseline</c> (Track 6, Plan Phase 0): a recorded snapshot of the database
/// before any uniformization work — current version, migration/model divergence, and a row-count
/// census of the Phase-6 removal candidates so "has data → archive" is distinguished from
/// "empty → drop".
/// </summary>
public class SchemaBaselineReport
{
    public string Environment { get; set; } = "";

    /// <summary>Current <c>db_version</c> from the <c>settings</c> table (−1 if unavailable).</summary>
    public int CurrentVersion { get; set; } = -1;

    public string ServerVersion { get; set; } = "";

    public bool DatabaseOnline { get; set; }

    /// <summary>EF migrations present in code but not yet recorded as applied in the database.</summary>
    public List<string> PendingMigrations { get; set; } = new();

    /// <summary>True if the EF model differs from the latest migration snapshot (an un-migrated entity change).</summary>
    public bool HasPendingModelChanges { get; set; }

    /// <summary>Row-count census of each Phase-6 removal candidate.</summary>
    public List<RemovalCandidateCensus> RemovalCandidates { get; set; } = new();

    /// <summary>Path the full report was written to, when applicable.</summary>
    public string? OutputPath { get; set; }

    public string Message { get; set; } = "";
}

public class RemovalCandidateCensus
{
    public string Table { get; set; } = "";

    /// <summary>True if the table currently exists in the database.</summary>
    public bool Exists { get; set; }

    /// <summary>Row count (−1 when the table does not exist).</summary>
    public long RowCount { get; set; } = -1;

    /// <summary>Recommended treatment: <c>drop</c> (empty/absent) or <c>archive</c> (has data).</summary>
    public string Recommendation => !Exists ? "absent" : RowCount == 0 ? "drop" : "archive";
}

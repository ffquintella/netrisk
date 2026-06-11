using System;

namespace DAL.Entities;

/// <summary>
/// Audit trail of every <c>database upgrade-schema</c> execution (Track 6 — Database
/// Uniformization). One row is appended per run: pre-flight checks, dry-runs, and real
/// applications. Mapped to the snake_case table <c>schema_upgrade_log</c> per the Track 6
/// target naming convention.
/// </summary>
public class SchemaUpgradeLog
{
    public int Id { get; set; }

    /// <summary>The phase identifier as declared in the manifest (e.g. <c>"1"</c>, <c>"6a"</c>).</summary>
    public string Phase { get; set; } = null!;

    /// <summary>The <c>db_version</c> the database was on before this run.</summary>
    public int StartVersion { get; set; }

    /// <summary>The <c>db_version</c> this phase targets (highest numbered SQL file it introduces).</summary>
    public int TargetVersion { get; set; }

    /// <summary>One of <c>Check</c>, <c>DryRun</c>, <c>Success</c>, <c>Failed</c>.</summary>
    public string Status { get; set; } = null!;

    /// <summary>Selected environment profile (<c>homolog</c> / <c>prod</c>).</summary>
    public string Environment { get; set; } = null!;

    /// <summary>Operator/user that triggered the run.</summary>
    public string Operator { get; set; } = null!;

    /// <summary>Path to the automatic backup taken before a real application (null for check/dry-run).</summary>
    public string? BackupPath { get; set; }

    /// <summary>Hash of the backup file, for provenance (null for check/dry-run).</summary>
    public string? BackupHash { get; set; }

    /// <summary>Free-text notes: validation summary, failure reason, census artifact path, etc.</summary>
    public string? Notes { get; set; }

    /// <summary>UTC timestamp the run was recorded.</summary>
    public DateTime AppliedAt { get; set; }
}

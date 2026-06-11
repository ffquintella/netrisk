namespace Model.Database;

/// <summary>
/// Data-driven description of the Track 6 schema-upgrade phases, deserialized from
/// <c>src/ConsoleClient/DB/SchemaUpgradePhases.yaml</c>. Each phase maps to a target
/// <c>db_version</c> (the highest numbered SQL file the phase introduces) and declares the
/// census queries and post-apply validations the <c>database upgrade-schema</c> tool runs.
/// </summary>
public class SchemaUpgradeManifest
{
    public List<SchemaUpgradePhase> Phases { get; set; } = new();

    /// <summary>Returns the phase with the given identifier, or null if it is not declared.</summary>
    public SchemaUpgradePhase? GetPhase(string phase) =>
        Phases.FirstOrDefault(p => string.Equals(p.Phase, phase, StringComparison.OrdinalIgnoreCase));
}

public class SchemaUpgradePhase
{
    /// <summary>Phase identifier (e.g. <c>"1"</c>, <c>"6a"</c>, <c>"6b"</c>).</summary>
    public string Phase { get; set; } = "";

    /// <summary>Human-readable summary of what the phase does.</summary>
    public string Description { get; set; } = "";

    /// <summary>The <c>db_version</c> the database must be on before this phase may be applied.</summary>
    public int StartVersion { get; set; }

    /// <summary>The <c>db_version</c> this phase brings the database to.</summary>
    public int TargetVersion { get; set; }

    /// <summary>True for phases that drop objects (currently only <c>6b</c>); these require an extra gate.</summary>
    public bool Destructive { get; set; }

    /// <summary>For destructive phases: minimum days the prerequisite phase must have been logged before this may run.</summary>
    public int ObservationDays { get; set; }

    /// <summary>Phase identifier that must already be recorded in <c>schema_upgrade_log</c> before this destructive phase runs.</summary>
    public string? RequiresPhase { get; set; }

    /// <summary>Census queries captured (row counts / orphan reports) before the phase is applied.</summary>
    public List<SchemaUpgradeCensus> Census { get; set; } = new();

    /// <summary>Post-apply validations that must pass for the run to be considered successful.</summary>
    public List<SchemaUpgradeValidation> Validations { get; set; } = new();
}

public class SchemaUpgradeCensus
{
    /// <summary>Label for the census artifact (e.g. <c>orphan-risk-owner</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary>Read-only SQL whose result is captured into the run's census report.</summary>
    public string Sql { get; set; } = "";
}

public class SchemaUpgradeValidation
{
    /// <summary>
    /// Validation kind. Recognized values: <c>RowCountParity</c> (table row count unchanged across the
    /// phase), <c>ForeignKeyExists</c>, <c>IndexExists</c>, <c>ColumnType</c>, <c>Custom</c>.
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>Label for the validation, surfaced in the run report.</summary>
    public string Name { get; set; } = "";

    /// <summary>Primary target (table name) the validation applies to.</summary>
    public string? Table { get; set; }

    /// <summary>Secondary target (column / index / constraint name) where relevant.</summary>
    public string? Target { get; set; }

    /// <summary>For <c>Custom</c> validations: read-only SQL expected to return a single truthy/expected value.</summary>
    public string? Sql { get; set; }

    /// <summary>For <c>Custom</c>/<c>ColumnType</c> validations: the expected value the query/metadata must equal.</summary>
    public string? Expected { get; set; }
}

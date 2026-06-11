namespace Model.Database;

/// <summary>Outcome of a single <c>database upgrade-schema</c> invocation (check / dry-run / apply).</summary>
public class SchemaUpgradeReport
{
    /// <summary>The mode that produced this report.</summary>
    public SchemaUpgradeMode Mode { get; set; }

    /// <summary>Phase identifier the run targeted.</summary>
    public string Phase { get; set; } = "";

    /// <summary>Selected environment profile.</summary>
    public string Environment { get; set; } = "";

    /// <summary><c>db_version</c> the database was on when the run started (−1 if it could not be read).</summary>
    public int CurrentVersion { get; set; } = -1;

    /// <summary><c>db_version</c> the phase targets.</summary>
    public int TargetVersion { get; set; }

    /// <summary>True when every check/validation passed and the run is safe / succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Individual pre-flight checks / post-apply validations and their outcomes.</summary>
    public List<SchemaUpgradeCheckResult> Checks { get; set; } = new();

    /// <summary>The exact SQL that would be / was applied (populated for dry-run).</summary>
    public string? GeneratedSql { get; set; }

    /// <summary>Path the report/SQL was written to, when applicable.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Human-readable summary line.</summary>
    public string Message { get; set; } = "";

    public void Add(string name, bool passed, string detail) =>
        Checks.Add(new SchemaUpgradeCheckResult { Name = name, Passed = passed, Detail = detail });

    /// <summary>True if any recorded check failed.</summary>
    public bool HasFailures => Checks.Exists(c => !c.Passed);
}

public class SchemaUpgradeCheckResult
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string Detail { get; set; } = "";
}

public enum SchemaUpgradeMode
{
    Check,
    DryRun,
    Apply
}

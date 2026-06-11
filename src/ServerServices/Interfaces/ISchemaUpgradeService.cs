using Model.Database;

namespace ServerServices.Interfaces;

/// <summary>
/// Orchestrates the Track 6 database-uniformization phases on top of the existing numbered-SQL
/// upgrade path. Drives the <c>database upgrade-schema</c> ConsoleClient command. See
/// roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md.
/// </summary>
public interface ISchemaUpgradeService
{
    /// <summary>Read-only pre-flight: connectivity, version match, phase SQL presence, destructive gate. Mutates nothing.</summary>
    SchemaUpgradeReport Check(string phaseId, string environment);

    /// <summary>Read-only: emits the exact SQL a phase would apply (optionally to a file) plus an impact summary. Mutates nothing.</summary>
    SchemaUpgradeReport DryRun(string phaseId, string environment, string? outputPath = null);

    /// <summary>Applies a phase: backup → census → apply numbered SQL → validate → log. Hard-to-reverse.</summary>
    SchemaUpgradeReport Apply(string phaseId, string environment, bool yes);
}

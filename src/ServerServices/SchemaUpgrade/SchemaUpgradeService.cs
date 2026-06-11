using System.Reflection;
using Model.Database;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.SchemaUpgrade;

/// <summary>
/// Orchestrates Track 6 schema-upgrade phases over the existing numbered-SQL <c>db_version</c>
/// path. Read-only modes (<see cref="Check"/>, <see cref="DryRun"/>) are fully implemented; the
/// destructive <see cref="Apply"/> path is gated until the Testcontainers integration harness
/// (see the 6.1 spec, Testing Requirements) can verify it end-to-end against a real MySQL.
/// </summary>
public class SchemaUpgradeService : ISchemaUpgradeService
{
    private readonly IDatabaseService _databaseService;
    private readonly IDalService _dalService;
    private readonly ILogger _logger;

    /// <summary>Directory containing the <c>SchemaUpgradePhases.yaml</c> manifest and the <c>Structure</c>/<c>Data</c> SQL folders. Overridable for tests.</summary>
    public string DbDirectory { get; set; }

    /// <summary>Clock seam for the destructive-phase observation-window gate. Overridable for tests.</summary>
    public Func<DateTime> NowUtc { get; set; } = () => DateTime.UtcNow;

    public SchemaUpgradeService(IDatabaseService databaseService, IDalService dalService, ILogger logger)
    {
        _databaseService = databaseService;
        _dalService = dalService;
        _logger = logger;

        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        DbDirectory = Path.Combine(currentDir, "DB");
    }

    private string ManifestPath => Path.Combine(DbDirectory, "SchemaUpgradePhases.yaml");
    private string StructureDir => Path.Combine(DbDirectory, "Structure");
    private string DataDir => Path.Combine(DbDirectory, "Data");

    private SchemaUpgradeManifest LoadManifest() => SchemaUpgradeManifestLoader.LoadFromFile(ManifestPath);

    public SchemaUpgradeReport Check(string phaseId, string environment)
    {
        var report = new SchemaUpgradeReport { Mode = SchemaUpgradeMode.Check, Phase = phaseId, Environment = environment };

        if (!TryResolvePhase(phaseId, report, out var phase))
            return report;

        report.TargetVersion = phase.TargetVersion;

        // Connectivity + current version.
        var status = _databaseService.Status();
        var online = status.Status == "Online";
        report.Add("connectivity", online, online ? "Database online" : $"Database not online ({status.Status})");
        if (!online)
        {
            report.Success = false;
            report.Message = "Pre-flight failed: database is not reachable.";
            return report;
        }

        var currentVersion = ParseVersion(status.Version);
        report.CurrentVersion = currentVersion;
        report.Add("server-version", true, $"MySQL {status.ServerVersion}");

        var atExpectedStart = currentVersion == phase.StartVersion;
        report.Add("start-version", atExpectedStart,
            atExpectedStart
                ? $"At expected start db_version {phase.StartVersion}"
                : $"Database is at db_version {currentVersion}, phase '{phaseId}' expects {phase.StartVersion}");

        // Phase SQL files authored?
        var missing = SchemaUpgradePlanner.MissingStructureFiles(StructureDir, phase);
        report.Add("phase-sql-present", missing.Count == 0,
            missing.Count == 0
                ? $"All structure files present for db_version {phase.StartVersion + 1}..{phase.TargetVersion}"
                : $"Missing structure files: {string.Join(", ", missing)}");

        // Destructive gate.
        if (phase.Destructive)
            EvaluateDestructiveGate(phase, report);

        report.Success = !report.HasFailures;
        report.Message = report.Success
            ? $"Pre-flight passed for phase '{phaseId}' (db_version {phase.StartVersion} → {phase.TargetVersion})."
            : $"Pre-flight failed for phase '{phaseId}'.";
        return report;
    }

    public SchemaUpgradeReport DryRun(string phaseId, string environment, string? outputPath = null)
    {
        var report = new SchemaUpgradeReport { Mode = SchemaUpgradeMode.DryRun, Phase = phaseId, Environment = environment };

        if (!TryResolvePhase(phaseId, report, out var phase))
            return report;

        report.TargetVersion = phase.TargetVersion;

        var missing = SchemaUpgradePlanner.MissingStructureFiles(StructureDir, phase);
        report.Add("phase-sql-present", missing.Count == 0,
            missing.Count == 0
                ? "All structure files present"
                : $"Missing structure files (SQL preview will be incomplete): {string.Join(", ", missing)}");

        report.GeneratedSql = SchemaUpgradePlanner.AssemblePhaseSql(StructureDir, DataDir, phase);

        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            try
            {
                File.WriteAllText(outputPath, report.GeneratedSql);
                report.OutputPath = outputPath;
                report.Add("write-output", true, $"SQL written to {outputPath}");
            }
            catch (Exception ex)
            {
                report.Add("write-output", false, $"Could not write output: {ex.Message}");
            }
        }

        report.Success = !report.HasFailures;
        report.Message = $"Dry-run for phase '{phaseId}' (db_version {phase.StartVersion} → {phase.TargetVersion}). " +
                         "Review the SQL above; no changes were made.";
        return report;
    }

    public SchemaUpgradeReport Apply(string phaseId, string environment, bool yes)
    {
        // Run the full pre-flight first so the operator sees exactly what blocks the apply.
        var report = Check(phaseId, environment);
        report.Mode = SchemaUpgradeMode.Apply;

        if (!report.Success)
        {
            report.Message = $"Apply aborted: pre-flight failed for phase '{phaseId}'.";
            return report;
        }

        // The destructive apply orchestration (backup → census → apply → validate → log) is intentionally
        // gated until the Testcontainers integration harness can verify it against a real MySQL. Shipping
        // unverified, hard-to-reverse schema mutation would violate the milestone's own safety ordering.
        report.Success = false;
        report.Add("apply-enabled", false,
            "Apply is not yet enabled in this build. Pre-flight passed — use --dry-run to review the SQL, " +
            "and apply once the schema-upgrade integration harness (6.1) lands.");
        report.Message = $"Pre-flight passed for phase '{phaseId}', but apply is gated pending the integration harness.";
        _logger.Warning("upgrade-schema apply requested for phase {Phase} but apply is gated in this build", phaseId);
        return report;
    }

    private bool TryResolvePhase(string phaseId, SchemaUpgradeReport report, out SchemaUpgradePhase phase)
    {
        phase = null!;
        SchemaUpgradeManifest manifest;
        try
        {
            manifest = LoadManifest();
        }
        catch (SchemaUpgradeManifestException ex)
        {
            report.Add("manifest", false, ex.Message);
            report.Success = false;
            report.Message = "Schema upgrade manifest could not be loaded.";
            return false;
        }

        var resolved = manifest.GetPhase(phaseId);
        if (resolved is null)
        {
            var known = string.Join(", ", manifest.Phases.ConvertAll(p => p.Phase));
            report.Add("phase", false, $"Unknown phase '{phaseId}'. Known phases: {known}.");
            report.Success = false;
            report.Message = $"Unknown phase '{phaseId}'.";
            return false;
        }

        report.Add("phase", true, $"Resolved phase '{phaseId}': {resolved.Description}");
        phase = resolved;
        return true;
    }

    private void EvaluateDestructiveGate(SchemaUpgradePhase phase, SchemaUpgradeReport report)
    {
        if (string.IsNullOrWhiteSpace(phase.RequiresPhase))
        {
            report.Add("destructive-gate", false, "Destructive phase declares no prerequisite phase.");
            return;
        }

        try
        {
            using var context = _dalService.GetContext(false);
            var prerequisite = context.SchemaUpgradeLogs
                .Where(l => l.Phase == phase.RequiresPhase && l.Status == "Success")
                .OrderByDescending(l => l.AppliedAt)
                .FirstOrDefault();

            if (prerequisite is null)
            {
                report.Add("destructive-gate", false,
                    $"Prerequisite phase '{phase.RequiresPhase}' has no successful entry in schema_upgrade_log.");
                return;
            }

            var elapsedDays = (NowUtc() - prerequisite.AppliedAt).TotalDays;
            var satisfied = elapsedDays >= phase.ObservationDays;
            report.Add("destructive-gate", satisfied,
                satisfied
                    ? $"Observation window met: phase '{phase.RequiresPhase}' applied {elapsedDays:F0}d ago (≥ {phase.ObservationDays}d)."
                    : $"Observation window NOT met: phase '{phase.RequiresPhase}' applied {elapsedDays:F1}d ago (< {phase.ObservationDays}d).");
        }
        catch (Exception ex)
        {
            report.Add("destructive-gate", false, $"Could not evaluate destructive gate: {ex.Message}");
        }
    }

    private static int ParseVersion(string? version) =>
        int.TryParse(version, out var v) ? v : -1;
}

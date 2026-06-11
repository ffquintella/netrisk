using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Model.Database;
using MySqlConnector;
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

    /// <summary>MySQL connection string used for the real apply/validation work. Defaults to <c>Database:ConnectionString</c>. Overridable for tests.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Directory automatic backups + census artifacts are written to before a destructive apply.</summary>
    public string BackupDirectory { get; set; }

    /// <summary>Clock seam for the destructive-phase observation-window gate. Overridable for tests.</summary>
    public Func<DateTime> NowUtc { get; set; } = () => DateTime.UtcNow;

    /// <summary>Identifier recorded as the operator on each <c>schema_upgrade_log</c> row.</summary>
    public string Operator { get; set; } = System.Environment.UserName;

    public SchemaUpgradeService(IDatabaseService databaseService, IDalService dalService,
        IConfiguration configuration, ILogger logger)
    {
        _databaseService = databaseService;
        _dalService = dalService;
        _logger = logger;

        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        DbDirectory = Path.Combine(currentDir, "DB");
        ConnectionString = configuration["Database:ConnectionString"];
        BackupDirectory = configuration["Database:BackupPath"] ?? "/backups";
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

        var manifest = LoadManifest();
        var phase = manifest.GetPhase(phaseId)!;

        if (phase.Destructive && !yes)
        {
            report.Success = false;
            report.Add("confirmation", false, "Destructive phase requires explicit --yes.");
            report.Message = $"Apply aborted: phase '{phaseId}' is destructive and --yes was not supplied.";
            return report;
        }

        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            report.Success = false;
            report.Add("connection-string", false, "No connection string configured.");
            report.Message = "Apply aborted: no connection string.";
            return report;
        }

        // 1) Automatic backup.
        string? backupPath = null, backupHash = null;
        try
        {
            Directory.CreateDirectory(BackupDirectory);
            var before = new HashSet<string>(Directory.GetFiles(BackupDirectory));
            _databaseService.Backup(BackupDirectory);
            backupPath = Directory.GetFiles(BackupDirectory).Except(before)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (backupPath is null)
            {
                report.Success = false;
                report.Add("backup", false, $"Backup produced no file in {BackupDirectory}.");
                report.Message = "Apply aborted: backup step did not produce a file.";
                return report;
            }
            backupHash = ComputeSha256(backupPath);
            report.Add("backup", true, $"Backup created: {backupPath}");
        }
        catch (Exception ex)
        {
            report.Success = false;
            report.Add("backup", false, $"Backup failed: {ex.Message}");
            report.Message = "Apply aborted: backup failed.";
            return report;
        }

        using var connection = new MySqlConnection(ConnectionString);
        connection.Open();

        // 2) Census (captured before the change).
        var censusReport = RunCensus(connection, phase);

        // 3) Apply the phase's numbered SQL (Structure then Data per version).
        try
        {
            foreach (var version in SchemaUpgradePlanner.VersionRange(phase))
            {
                ExecuteSqlFile(connection, Path.Combine(StructureDir, $"{version}.sql"));
                ExecuteSqlFile(connection, Path.Combine(DataDir, $"{version}.sql"));
            }
        }
        catch (Exception ex)
        {
            report.Success = false;
            report.Add("apply", false, $"Apply failed: {ex.Message}");
            WriteLog(phase, "Failed", environment, backupPath, backupHash,
                $"apply error: {ex.Message}\n{censusReport}");
            report.Message = $"Apply FAILED for phase '{phaseId}'. Restore from backup if needed: {backupPath}";
            return report;
        }
        report.Add("apply", true, $"Applied numbered SQL up to db_version {phase.TargetVersion}.");

        // 4) Post-apply validations.
        var allValid = true;
        foreach (var validation in phase.Validations)
        {
            var (passed, detail) = SchemaUpgradeValidator.Run(connection, validation);
            report.Add($"validate:{validation.Name}", passed, detail);
            allValid &= passed;
        }

        // 5) Record the outcome.
        var status = allValid ? "Success" : "Failed";
        WriteLog(phase, status, environment, backupPath, backupHash,
            $"validations: {(allValid ? "all passed" : "FAILED")}\n{censusReport}");

        report.Success = allValid;
        report.Message = allValid
            ? $"Phase '{phaseId}' applied successfully (db_version {phase.StartVersion} → {phase.TargetVersion})."
            : $"Phase '{phaseId}' applied but post-validation FAILED. Review and restore from backup if needed: {backupPath}";
        return report;
    }

    private string RunCensus(MySqlConnection connection, SchemaUpgradePhase phase)
    {
        var sb = new StringBuilder("census:\n");
        foreach (var census in phase.Census)
        {
            try
            {
                using var cmd = new MySqlCommand(census.Sql, connection);
                using var reader = cmd.ExecuteReader();
                var rows = 0;
                while (reader.Read()) rows++;
                reader.Close();
                sb.AppendLine($"  {census.Name}: {rows} row(s)");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  {census.Name}: ERROR {ex.Message}");
            }
        }
        return sb.ToString();
    }

    private static void ExecuteSqlFile(MySqlConnection connection, string path)
    {
        if (!File.Exists(path)) return; // Data files are optional; missing structure is caught in pre-flight.
        var sql = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(sql)) return;
        using var cmd = new MySqlCommand(sql, connection) { CommandTimeout = 0 };
        cmd.ExecuteNonQuery();
    }

    private void WriteLog(SchemaUpgradePhase phase, string status, string environment,
        string? backupPath, string? backupHash, string? notes)
    {
        try
        {
            using var context = _dalService.GetContext(false);
            context.SchemaUpgradeLogs.Add(new DAL.Entities.SchemaUpgradeLog
            {
                Phase = phase.Phase,
                StartVersion = phase.StartVersion,
                TargetVersion = phase.TargetVersion,
                Status = status,
                Environment = environment,
                Operator = Operator,
                BackupPath = backupPath,
                BackupHash = backupHash,
                Notes = notes,
                AppliedAt = NowUtc()
            });
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to write schema_upgrade_log entry for phase {Phase}", phase.Phase);
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
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

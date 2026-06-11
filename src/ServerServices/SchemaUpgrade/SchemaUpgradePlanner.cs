using System.Text;
using Model.Database;

namespace ServerServices.SchemaUpgrade;

/// <summary>
/// Pure (no-DB) helpers for the schema-upgrade tool: which numbered SQL files a phase covers,
/// whether they are present on disk, and assembling their contents into the SQL a dry-run shows.
/// Kept separate from <see cref="SchemaUpgradeService"/> so the file/version logic is unit-testable
/// without a database.
/// </summary>
public static class SchemaUpgradePlanner
{
    /// <summary>The numbered <c>db_version</c> SQL files a phase introduces: <c>startVersion+1 .. targetVersion</c>.</summary>
    public static IReadOnlyList<int> VersionRange(SchemaUpgradePhase phase)
    {
        var versions = new List<int>();
        for (var v = phase.StartVersion + 1; v <= phase.TargetVersion; v++)
            versions.Add(v);
        return versions;
    }

    /// <summary>
    /// Returns the Structure SQL files for a phase that are missing from disk. The tool refuses to
    /// apply (or dry-run) a phase whose numbered files have not been authored yet.
    /// </summary>
    public static IReadOnlyList<string> MissingStructureFiles(string structureDir, SchemaUpgradePhase phase)
    {
        var missing = new List<string>();
        foreach (var v in VersionRange(phase))
        {
            var path = Path.Combine(structureDir, $"{v}.sql");
            if (!File.Exists(path))
                missing.Add(path);
        }
        return missing;
    }

    /// <summary>
    /// Concatenates the Structure and Data SQL for every version in the phase range, in apply order
    /// (Structure then Data per version). Mirrors what <c>DatabaseService.Update</c> executes, so the
    /// dry-run output is the exact SQL that would run. Data files are optional (some versions have none).
    /// </summary>
    public static string AssemblePhaseSql(string structureDir, string dataDir, SchemaUpgradePhase phase)
    {
        var sb = new StringBuilder();
        foreach (var v in VersionRange(phase))
        {
            var structurePath = Path.Combine(structureDir, $"{v}.sql");
            var dataPath = Path.Combine(dataDir, $"{v}.sql");

            sb.AppendLine($"-- ===== db_version {v} — structure =====");
            sb.AppendLine(File.Exists(structurePath) ? File.ReadAllText(structurePath).Trim() : "-- (missing structure file)");
            sb.AppendLine();

            if (File.Exists(dataPath))
            {
                var data = File.ReadAllText(dataPath).Trim();
                if (data.Length > 0)
                {
                    sb.AppendLine($"-- ===== db_version {v} — data =====");
                    sb.AppendLine(data);
                    sb.AppendLine();
                }
            }
        }
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}

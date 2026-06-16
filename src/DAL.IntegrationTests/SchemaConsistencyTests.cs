using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DAL.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Guards against the failure mode that broke production authentication: a change to the EF model
/// (<c>NRDbContext.OnModelCreating</c>) that shipped without (a) a migration regenerating the model
/// snapshot and (b) the numbered Structure/Data SQL that actually reaches the runtime DB. These are
/// pure model/file checks — no database or Docker — so they run in the fast unit suite
/// (<c>Category!=Integration</c>) and fail the build the moment the model drifts.
/// </summary>
public class SchemaConsistencyTests
{
    /// <summary>
    /// The model must match the snapshot of the latest migration. Before the <c>AddUserEntityRoles</c>
    /// fix this returned <c>true</c> (entities like <c>UserEntityRole</c> were in the model but not the
    /// snapshot), which is exactly what let the <c>user_entity_roles</c> table ship without schema.
    /// </summary>
    [Fact]
    public void Model_HasNoPendingChanges_AgainstMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<NRDbContext>()
            // Parse (not AutoDetect) so no connection is opened — this is a model-level comparison only.
            .UseMySql("server=unused;database=netrisk;user=x;password=y", ServerVersion.Parse("10.11.0-mariadb"))
            .Options;
        using var ctx = new NRDbContext(options);

        Assert.False(
            ctx.Database.HasPendingModelChanges(),
            "The EF model has changes not captured by a migration. Run ./migrationAdd.sh <Name>, then split the " +
            "generated SQL into the next numbered DB/Structure + DB/Data files and bump targetVersion " +
            "(see CLAUDE.md → Database Migrations).");
    }

    /// <summary>
    /// The newest EF migration must have a corresponding <c>__EFMigrationsHistory</c> insert in a numbered
    /// Data SQL file. This is the step that was skipped for the multi-entity roles feature: a migration with
    /// no numbered SQL never reaches a runtime DB (EF <c>Database.Migrate()</c> is not called in production)
    /// and EF state diverges. (Only the newest migration is checked — a batch of foundational 2024 migrations
    /// predates the numbered-SQL convention and is folded into the base schema, so auditing all of them would
    /// flag legacy state rather than new mistakes.)
    /// </summary>
    [Fact]
    public void LatestMigration_HasNumberedDataSqlEntry()
    {
        var latest = MigrationIds().OrderBy(id => id).Last();
        var historyInserts = ReadAllDataSqlMigrationIds();

        Assert.Contains(latest, historyInserts);
    }

    /// <summary>
    /// <c>targetVersion</c> in DatabaseInformation.yaml must match the highest numbered SQL file, so the
    /// runtime upgrades all the way to the newest schema instead of stopping short of it.
    /// </summary>
    [Fact]
    public void TargetVersion_MatchesHighestNumberedSqlFile()
    {
        var dbDir = RepoDbDir();
        var highestStructure = HighestNumberedFile(Path.Combine(dbDir, "Structure"));
        var highestData = HighestNumberedFile(Path.Combine(dbDir, "Data"));

        var yaml = File.ReadAllText(Path.Combine(dbDir, "DatabaseInformation.yaml"));
        var match = Regex.Match(yaml, @"targetVersion:\s*(\d+)");
        Assert.True(match.Success, "targetVersion not found in DatabaseInformation.yaml");
        var targetVersion = int.Parse(match.Groups[1].Value);

        Assert.Equal(highestStructure, highestData);
        Assert.Equal(highestStructure, targetVersion);
    }

    private static HashSet<string> ReadAllDataSqlMigrationIds()
    {
        var dataDir = Path.Combine(RepoDbDir(), "Data");
        var ids = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(dataDir, "*.sql"))
        foreach (Match m in Regex.Matches(File.ReadAllText(file), @"INSERT INTO\s+`__EFMigrationsHistory`.*?VALUES\s*\(\s*'([^']+)'", RegexOptions.Singleline))
            ids.Add(m.Groups[1].Value);
        return ids;
    }

    private static IEnumerable<string> MigrationIds()
    {
        // EF migration files are DAL/Migrations/<timestamp>_<Name>.cs (excluding *.Designer.cs and the snapshot).
        var migrationsDir = Path.Combine(RepoSrcDir(), "DAL", "Migrations");
        return Directory.EnumerateFiles(migrationsDir, "*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null && !n.EndsWith(".Designer") && n != "NRDbContextModelSnapshot")
            .Select(n => n!)
            .Where(n => Regex.IsMatch(n, @"^\d{14}_"));
    }

    private static int HighestNumberedFile(string dir) =>
        Directory.EnumerateFiles(dir, "*.sql")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => int.TryParse(n, out _))
            .Select(int.Parse)
            .Max();

    private static string RepoDbDir() => Path.Combine(RepoSrcDir(), "ConsoleClient", "DB");

    /// <summary>Absolute path to <c>src</c>, resolved from this source file's location.</summary>
    private static string RepoSrcDir([CallerFilePath] string thisFile = "") =>
        Directory.GetParent(thisFile)!.Parent!.FullName; // .../src/DAL.IntegrationTests/<file> -> .../src
}

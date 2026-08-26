using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConsoleClient.Models;
using JetBrains.Annotations;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ConsoleClient.Tests.DB;

/// <summary>
/// Guards the numbered-SQL upgrade ritual: <c>DB/Structure/{n}.sql</c> + <c>DB/Data/{n}.sql</c> are
/// applied in order by <c>DatabaseService.Update()</c> up to the <c>targetVersion</c> declared in
/// <c>DB/DatabaseInformation.yaml</c>, and each file only reaches a release if it is declared as
/// <c>Content</c> in ConsoleClient.csproj. Every one of those three things is edited by hand, so a
/// missed step is the likeliest way to ship a half-applied schema.
/// </summary>
[TestSubject(typeof(DatabaseInformation))]
public class SchemaUpgradeFilesTest
{
    private static readonly DatabaseInformation Info = LoadDatabaseInformation();

    private static DatabaseInformation LoadDatabaseInformation()
    {
        var path = Path.Combine(RepoLayout.DbDirectory.FullName, "DatabaseInformation.yaml");

        // Same deserializer configuration as ConsoleClient.Commands.DatabaseCommand.
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<DatabaseInformation>(File.ReadAllText(path));
    }

    private static List<int> VersionsIn(string subdirectory)
    {
        var directory = new DirectoryInfo(Path.Combine(RepoLayout.DbDirectory.FullName, subdirectory));

        return directory.GetFiles("*.sql")
            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
            .Where(name => int.TryParse(name, out _))
            .Select(int.Parse)
            .OrderBy(n => n)
            .ToList();
    }

    [Fact]
    public void TestDatabaseInformationParses()
    {
        Assert.Equal(1, Info.InitialVersion);
        Assert.True(Info.TargetVersion >= Info.InitialVersion,
            $"targetVersion ({Info.TargetVersion}) must not be below initialVersion ({Info.InitialVersion}).");
    }

    [Fact]
    public void TestTargetVersionMatchesTheHighestStructureScript()
    {
        Assert.Equal(VersionsIn("Structure").Max(), Info.TargetVersion);
    }

    [Fact]
    public void TestTargetVersionMatchesTheHighestDataScript()
    {
        Assert.Equal(VersionsIn("Data").Max(), Info.TargetVersion);
    }

    [Fact]
    public void TestEveryVersionHasBothAStructureAndADataScript()
    {
        var structure = VersionsIn("Structure");
        var data = VersionsIn("Data");

        Assert.Equal(structure, data);
    }

    [Fact]
    public void TestVersionSequenceHasNoGaps()
    {
        var versions = VersionsIn("Structure");
        var expected = Enumerable.Range(Info.InitialVersion, Info.TargetVersion - Info.InitialVersion + 1);

        Assert.Equal(expected, versions);
    }

    [Fact]
    public void TestEverySqlScriptIsDeclaredAsContentInTheProjectFile()
    {
        var project = File.ReadAllText(RepoLayout.ProjectFile.FullName);

        var declared = Regex.Matches(project, @"<Content Include=""DB\\(Structure|Data)\\(\d+)\.sql""")
            .Select(m => $"{m.Groups[1].Value}/{m.Groups[2].Value}")
            .ToHashSet();

        var missing = new List<string>();
        foreach (var subdirectory in new[] { "Structure", "Data" })
        {
            missing.AddRange(VersionsIn(subdirectory)
                .Select(version => $"{subdirectory}/{version}")
                .Where(key => !declared.Contains(key)));
        }

        Assert.True(missing.Count == 0,
            "These SQL scripts exist on disk but are not declared as <Content> in ConsoleClient.csproj, " +
            $"so they will not be copied to the output and the upgrade will stall: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// A Data script past the base seed must not name an explicit <c>permissions.id</c>.
    ///
    /// This is a regression guard for a real mistake, and the rule is stronger than "no two scripts
    /// use the same id" because the collision it prevents is not visible in the scripts at all.
    /// Version 4 is the last one that assigns ids by hand (up to 49); every script after it lets
    /// auto_increment allocate, so which id a permission ends up with depends on the order the
    /// scripts ran — and nothing in the SQL says what that will be.
    ///
    /// Track 8's first draft of <c>Data/81.sql</c> wrote
    /// <c>INSERT INTO permissions (id, ...) VALUES (50, 'business_risk_review', ...)
    /// ON DUPLICATE KEY UPDATE name = ..., description = ...</c>. On a real database id 50 was
    /// already <c>incident-response-plans</c> — allocated by <c>Data/34.sql</c>, which names no id —
    /// so the upsert silently *renamed that permission* and never created the new one. The portal
    /// would have been unreachable, and the IRP permission mislabelled, for reasons invisible in the
    /// schema. It surfaced only when all 82 versions were applied to a MariaDB container.
    ///
    /// The fix is <c>INSERT IGNORE</c> keyed on <c>key</c>, which is the unique column: idempotent,
    /// and it cannot collide with an id it does not name.
    /// </summary>
    [Fact]
    public void TestNoLaterDataScriptAssignsAnExplicitPermissionId()
    {
        // Versions 1 and 4 are the base seed and its first extension; both assign ids by hand and
        // both predate the convention. Everything after them allocates.
        const int lastHandNumberedVersion = 4;

        var offenders = new List<string>();

        foreach (var version in VersionsIn("Data").Where(v => v > lastHandNumberedVersion))
        {
            var sql = File.ReadAllText(Path.Combine(RepoLayout.DbDirectory.FullName, "Data",
                $"{version}.sql"));

            // Comments are stripped first: this file's own explanation mentions the offending shape.
            sql = Regex.Replace(sql, @"--[^\n]*", " ");

            foreach (Match match in Regex.Matches(sql,
                         @"INSERT\s+(?:IGNORE\s+)?INTO\s+`?permissions`?\s*\(([^)]*)\)",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var columns = match.Groups[1].Value;

                if (Regex.IsMatch(columns, @"(^|[,\s(])`?id`?\s*(,|$)", RegexOptions.IgnoreCase))
                    offenders.Add($"Data/{version}.sql");
            }
        }

        Assert.True(offenders.Count == 0,
            "Permission ids after version 4 are allocated by auto_increment, so a script that names " +
            "one is naming an id it cannot know is free — and with an ON DUPLICATE KEY clause that is " +
            "a silent rename of somebody else's permission rather than a failed insert: " +
            string.Join(", ", offenders.Distinct()));
    }

    [Fact]
    public void TestDatabaseInformationYamlIsDeclaredAsContent()
    {
        var project = File.ReadAllText(RepoLayout.ProjectFile.FullName);

        Assert.Contains(@"<Content Include=""DB\DatabaseInformation.yaml""", project);
    }

    [Fact]
    public void TestEveryDataScriptHasContent()
    {
        // A Structure script is legitimately empty for a data-only step (32, 44, 46, 49 and 55 are),
        // but a Data script always has to carry at least the db_version bump, so an empty one would
        // stall the upgrade at the previous version.
        var directory = new DirectoryInfo(Path.Combine(RepoLayout.DbDirectory.FullName, "Data"));

        var empty = directory.GetFiles("*.sql").Where(f => f.Length == 0).Select(f => f.Name).ToList();

        Assert.True(empty.Count == 0, $"Empty data scripts: {string.Join(", ", empty)}.");
    }

    [Fact]
    public void TestEveryDataScriptBumpsTheDatabaseVersion()
    {
        var missing = new List<string>();

        foreach (var version in VersionsIn("Data"))
        {
            var path = Path.Combine(RepoLayout.DbDirectory.FullName, "Data", $"{version}.sql");
            var sql = File.ReadAllText(path);

            // Version 1 is the base install and seeds the row; every later step updates it.
            var seeds = Regex.IsMatch(sql, $@"INSERT\s+INTO\s+`?settings`?.*'db_version',\s*'{version}'",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var bumps = Regex.IsMatch(sql, $@"value\s*=\s*'{version}'\s+where\s+name\s*=\s*'db_version'",
                RegexOptions.IgnoreCase);

            if (!seeds && !bumps) missing.Add($"Data/{version}.sql");
        }

        Assert.True(missing.Count == 0,
            "These data scripts do not set db_version to their own number, so DatabaseService.Update() " +
            $"would re-run them forever: {string.Join(", ", missing)}.");
    }
}

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

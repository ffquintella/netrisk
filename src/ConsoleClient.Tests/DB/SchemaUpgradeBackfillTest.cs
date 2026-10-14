using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Model.Status;
using Tools.Risks;
using Xunit;

namespace ConsoleClient.Tests.DB;

/// <summary>
/// The M52 backfills (S39 D-02, D-04, D-05; T238–T240) checked against the code they must agree with,
/// without a database. Their behaviour on real rows is <c>DAL.IntegrationTests/M52FoundationBackfillTests</c>.
///
/// The finding backfill hand-writes the legacy → lifecycle mapping twice (history row, then status)
/// and the desktop close reads it from <see cref="LegacyFindingStatus"/>. Three copies of one table
/// drift silently: a value added to the C# map and not to the SQL leaves the findings the desktop
/// closed with it open forever. This pins every copy to the C# one.
/// </summary>
[TestSubject(typeof(LegacyFindingStatus))]
public class SchemaUpgradeBackfillTest
{
    private static readonly RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    /// <summary>The Data script carrying the T239 backfill, found by its marker rather than by number.</summary>
    private static string BackfillScript()
    {
        var matches = new DirectoryInfo(Path.Combine(RepoLayout.DbDirectory.FullName, "Data"))
            .GetFiles("*.sql")
            .Where(f => File.ReadAllText(f.FullName).Contains("T239 backfill:"))
            .ToList();

        Assert.Single(matches);
        return File.ReadAllText(matches[0].FullName);
    }

    private static string WithoutComments(string sql) => Regex.Replace(sql, @"--[^\n]*", " ");

    private static Dictionary<int, int> ExpectedMap() =>
        LegacyFindingStatus.ClosingTargets.ToDictionary(p => p.Key, p => (int)p.Value);

    [Fact]
    public void TestEveryStatusCaseOfTheFindingBackfillIsTheClosingMap()
    {
        var sql = WithoutComments(BackfillScript());

        var cases = Regex.Matches(sql, @"CASE\s+v\.`Status`(.*?)\bEND\b", Opts);
        Assert.Equal(2, cases.Count);

        foreach (Match block in cases)
        {
            var pairs = Regex.Matches(block.Groups[1].Value, @"WHEN\s+(\d+)\s+THEN\s+(\d+)", Opts)
                .ToDictionary(m => int.Parse(m.Groups[1].Value), m => int.Parse(m.Groups[2].Value));

            Assert.Equal(ExpectedMap().OrderBy(p => p.Key), pairs.OrderBy(p => p.Key));
        }
    }

    [Fact]
    public void TestEveryCandidateListOfTheFindingBackfillIsTheClosedSet()
    {
        var sql = WithoutComments(BackfillScript());

        var lists = Regex.Matches(sql, @"v\.`Status`\s+IN\s*\(([^)]*)\)", Opts);
        Assert.Equal(2, lists.Count);

        foreach (Match list in lists)
        {
            var values = list.Groups[1].Value.Split(',').Select(v => int.Parse(v.Trim())).OrderBy(v => v);
            Assert.Equal(ClosedStatuses.Values.OrderBy(v => v), values);
        }
    }

    /// <summary>
    /// The backfill only moves rows still at Active — the predicate that makes it safe to apply again
    /// and keeps it from overriding a verdict the lifecycle already holds.
    /// </summary>
    [Fact]
    public void TestTheFindingBackfillOnlyTouchesActiveRows()
    {
        var sql = WithoutComments(BackfillScript());

        Assert.Equal(2, Regex.Matches(sql, @"WHERE\s+v\.`status_id`\s*=\s*1\b", Opts).Count);
    }

    [Fact]
    public void TestTheScopeBackfillNeverOverwritesAnExplicitScope()
    {
        var sql = WithoutComments(BackfillScript());

        Assert.Equal(2, Regex.Matches(sql, @"WHERE\s+r\.`entity_id`\s+IS\s+NULL\b", Opts).Count);
    }

    /// <summary>
    /// T238's leftover: the seeded Very High moves to the register limit <see cref="RiskBand.Faixa"/>
    /// uses, and only while it is still the seed's 10.1.
    /// </summary>
    [Fact]
    public void TestVeryHighMovesToTheRegisterLimitOnlyFromTheSeedValue()
    {
        var sql = WithoutComments(BackfillScript());

        var update = Regex.Match(sql,
            @"UPDATE\s+`risk_levels`\s+SET\s+`value`\s*=\s*([\d.]+)\s+WHERE\s+`name`\s*=\s*'Very High'\s+AND\s+`value`\s*=\s*10\.1\s*;",
            Opts);

        Assert.True(update.Success, "The Very High update must be guarded by the seed value 10.1.");
        Assert.Equal(RiskBand.DefaultRegisterLimits[RiskBand.VeryHigh],
            double.Parse(update.Groups[1].Value, CultureInfo.InvariantCulture));
    }
}

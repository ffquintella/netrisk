using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ConsoleClient.Tests.DB;

/// <summary>
/// Guards MariaDB's restriction on referential actions for columns used by a CHECK. MariaDB 10.11
/// rejects the whole CREATE TABLE with error 1901 when a checked foreign-key column uses ON DELETE
/// SET NULL (MDEV-30606), leaving the numbered upgrade partially applied.
/// </summary>
public class MariaDbCheckConstraintCompatibilityTest
{
    [Fact]
    public void TestReassessmentEventIncidentIntegrityAvoidsTheCheckSetNullConflict()
    {
        var sql = File.ReadAllText(Path.Combine(
            RepoLayout.DbDirectory.FullName, "Structure", "97.sql"));

        var check = Regex.Match(sql,
            @"CONSTRAINT\s+`ck_reassessment_events_incident_type`\s+CHECK\s*\((?<expression>.*?)\)\s*,",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var foreignKey = Regex.Match(sql,
            @"CONSTRAINT\s+`fk_reassessment_events_incident_id`\s+FOREIGN\s+KEY\s*\(`incident_id`\)" +
            @"(?<definition>.*?)\s*(?:,|\r?$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline);

        Assert.True(foreignKey.Success, "The incident referential-integrity constraint is missing.");
        var usesSetNull = foreignKey.Groups["definition"].Value.Contains(
            "ON DELETE SET NULL", StringComparison.OrdinalIgnoreCase);

        Assert.False(check.Success && usesSetNull,
            "MariaDB rejects a CHECK over incident_id when its foreign key uses ON DELETE SET NULL.");
        Assert.True(usesSetNull,
            "Incident deletion must retain the reassessment event and release its incident link, including when " +
            "the incident itself is removed by a foreign-key cascade.");

        foreach (var operation in new[] { "INSERT", "UPDATE" })
        {
            Assert.Matches(new Regex(
                $@"CREATE\s+TRIGGER\s+IF\s+NOT\s+EXISTS\s+`[^`]+`\s+BEFORE\s+{operation}\s+ON\s+`reassessment_events`" +
                @".*?NEW\.`incident_id`\s+IS\s+NOT\s+NULL.*?NEW\.`trigger_type`\s*<>\s*3.*?SIGNAL\s+SQLSTATE\s+'45000'",
                RegexOptions.IgnoreCase | RegexOptions.Singleline), sql);
        }
    }
}

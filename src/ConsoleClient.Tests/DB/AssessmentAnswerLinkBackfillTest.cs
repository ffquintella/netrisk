using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Model.Assessments;
using Xunit;

namespace ConsoleClient.Tests.DB;

/// <summary>
/// M58, T280 (S39 D-16): the backfill that links existing run answers to their option, checked against the rule of
/// <see cref="AssessmentAnswerLink"/> without a database — the same question, the decoded text, case aside, and only a
/// unique match; never a row already linked. Its run on real rows needs MariaDB (DAL.IntegrationTests, Docker).
/// </summary>
[TestSubject(typeof(AssessmentAnswerLink))]
public class AssessmentAnswerLinkBackfillTest
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;

    private static string Backfill()
    {
        var matches = new DirectoryInfo(Path.Combine(RepoLayout.DbDirectory.FullName, "Data"))
            .GetFiles("*.sql")
            .Where(f => File.ReadAllText(f.FullName).Contains("T280 backfill:"))
            .ToList();

        Assert.Single(matches);
        return Regex.Replace(File.ReadAllText(matches[0].FullName), @"--[^\n]*", " ");
    }

    [Fact]
    public void TestTheBackfillNeverOverwritesALinkedAnswer()
    {
        var sql = Backfill();

        Assert.Matches(new Regex(@"SET\s+ra\.`assessment_answer_id`\s*=\s*m\.`option_id`\s+WHERE\s+ra\.`assessment_answer_id`\s+IS\s+NULL\s*;", Opts), sql);
        Assert.Matches(new Regex(@"WHERE\s+a\.`assessment_answer_id`\s+IS\s+NULL", Opts), sql);
    }

    /// <summary><see cref="AssessmentAnswerLink.MatchByText"/>: only a unique match among the question's own options.</summary>
    [Fact]
    public void TestTheBackfillLinksOnlyAUniqueOptionOfTheSameQuestion()
    {
        var sql = Backfill();

        Assert.Matches(new Regex(@"ON\s+o\.`question_id`\s*=\s*a\.`assessment_question_id`", Opts), sql);
        Assert.Matches(new Regex(@"GROUP\s+BY\s+a\.`id`\s+HAVING\s+COUNT\(\*\)\s*=\s*1", Opts), sql);
    }

    /// <summary>
    /// <see cref="AssessmentAnswerLink.DecodeText"/>: a JSON string is unquoted, anything else has its quotes trimmed; the
    /// comparison ignores case on both sides, under a binary collation so accents still differ.
    /// </summary>
    [Fact]
    public void TestTheBackfillDecodesAndComparesTheTextLikeTheService()
    {
        var sql = Backfill();

        Assert.Matches(new Regex(
            @"CASE\s+WHEN\s+JSON_VALID\(a\.`answer_content_json`\)\s+AND\s+JSON_TYPE\(a\.`answer_content_json`\)\s*=\s*'STRING'\s+" +
            @"THEN\s+JSON_UNQUOTE\(a\.`answer_content_json`\)\s+ELSE\s+TRIM\(BOTH\s+'""'\s+FROM\s+a\.`answer_content_json`\)\s+END", Opts), sql);
        Assert.Equal(2, Regex.Matches(sql, @"LOWER\(CONVERT\(", Opts).Count);
        Assert.Equal(2, Regex.Matches(sql, @"COLLATE\s+utf8mb4_bin", Opts).Count);
    }

    /// <summary>The model change this version carries is recorded with the backfill, in the same transaction.</summary>
    [Fact]
    public void TestTheBackfillVersionRecordsItsMigration()
    {
        var sql = Backfill();

        Assert.Contains("20261118162742_IncidentSeverityAndAssessmentSubmission", sql);
        Assert.Matches(new Regex(@"START\s+TRANSACTION;.*UPDATE\s+`assessment_run_answers`.*update\s+settings\s+SET\s+value\s*=\s*'114'.*COMMIT;", Opts), sql);
    }
}

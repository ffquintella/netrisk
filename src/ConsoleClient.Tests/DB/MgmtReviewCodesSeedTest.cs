using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using JetBrains.Annotations;
using Model.Risks;
using Xunit;

namespace ConsoleClient.Tests.DB;

/// <summary>
/// T277 (S39 D-06): the review and next-step codes the services write and read are the rows <c>Data/1.sql</c> seeds. The
/// Track 8 constants were each written from memory — <c>ReviewAcceptTheRisk = 2</c> is "Reject Risk and Close",
/// <c>NextStepAcceptUntilNextReview = 3</c> is "Submit as a Production Issue", <c>NextStepRequestRiskReview = 1</c> is
/// "Accept until Next Review" — so every acceptance and campaign decision recorded an outcome nobody chose. The seed is
/// read from the file, not restated, so changing either side alone fails here.
/// </summary>
[TestSubject(typeof(ReviewCodes))]
public class MgmtReviewCodesSeedTest
{
    /// <summary>The <c>(value, name)</c> rows <c>Data/1.sql</c> inserts into <paramref name="table"/>.</summary>
    private static Dictionary<int, string> Seeded(string table)
    {
        var sql = File.ReadAllText(Path.Combine(RepoLayout.DbDirectory.FullName, "Data", "1.sql"));
        var rows = Regex.Matches(sql,
            $@"INSERT INTO `{Regex.Escape(table)}` \(`value`, `name`\) VALUES \((\d+), '([^']*)'\);");

        Assert.NotEmpty(rows);
        return rows.ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
    }

    [Theory]
    [InlineData(ReviewCodes.ApproveRisk, "Approve Risk")]
    [InlineData(ReviewCodes.RejectRiskAndClose, "Reject Risk and Close")]
    [InlineData(ReviewCodes.RequestRiskReview, "Request Risk review")]
    public void TestEveryReviewCodeNamesItsSeededRow(int code, string seededName)
    {
        Assert.Equal(seededName, Seeded("review")[code]);
    }

    [Theory]
    [InlineData(NextStepCodes.AcceptUntilNextReview, "Accept until Next Review")]
    [InlineData(NextStepCodes.ConsiderForProject, "Consider for Project")]
    [InlineData(NextStepCodes.SubmitAsProductionIssue, "Submit as a Production Issue")]
    [InlineData(NextStepCodes.Reject, "Reject")]
    public void TestEveryNextStepCodeNamesItsSeededRow(int code, string seededName)
    {
        Assert.Equal(seededName, Seeded("next_step")[code]);
    }

    /// <summary>No seeded row is left without a constant, so a new seed row cannot be written by a bare number.</summary>
    [Fact]
    public void TestEverySeededRowHasAConstant()
    {
        int[] reviews = [ReviewCodes.ApproveRisk, ReviewCodes.RejectRiskAndClose, ReviewCodes.RequestRiskReview];
        int[] nextSteps =
        [
            NextStepCodes.AcceptUntilNextReview, NextStepCodes.ConsiderForProject, NextStepCodes.SubmitAsProductionIssue,
            NextStepCodes.Reject
        ];

        Assert.Equal(Seeded("review").Keys.Order(), reviews.Order());
        Assert.Equal(Seeded("next_step").Keys.Order(), nextSteps.Order());
    }
}

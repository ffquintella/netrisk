using System;
using System.Linq;
using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>
/// Severity is stored as a string, so the grid cannot sort it numerically on its own and the
/// "needs action first" default order (S38 §3.3) has to be computed. Unparsable severities count as
/// None, the same rule the server's summary applies, so the header and the grid agree.
/// </summary>
[TestSubject(typeof(SeverityScale))]
public class SeverityScaleTest
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("4", 4)]
    [InlineData(" 3 ", 3)]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("High", 0)]
    [InlineData("7", 0)]
    [InlineData("-1", 0)]
    [InlineData("+3", 0)]
    public void RankParsesTheStoredStringAndFloorsAnythingElseToNone(string? severity, int expected)
    {
        Assert.Equal(expected, SeverityScale.Rank(severity));
        Assert.Equal("s" + expected, SeverityScale.ClassFor(severity));
    }

    [Fact]
    public void EverySeverityClassIsStyledOnTheSameRampAsCriticality()
    {
        var styles = HostsTestFiles.Read("Styles/WindowStyles.axaml");

        // s1 Low → 2, s2 Medium → 3, s3 High → 4, s4 Critical → 5, s0 None → unset.
        Assert.Contains("Border.criticality.c2, Border.severity.s1", styles);
        Assert.Contains("Border.criticality.c3, Border.severity.s2", styles);
        Assert.Contains("Border.criticality.c4, Border.severity.s3", styles);
        Assert.Contains("Border.criticality.c5, Border.severity.s4", styles);
        Assert.Contains("Border.criticality.unset, Border.severity.s0", styles);
    }

    [Fact]
    public void TriageOrderIsSeverityDescendingThenMostRecentDetection()
    {
        var day = new DateTime(2026, 9, 1);
        var rows = new[]
        {
            (Id: 1, Severity: "2", Last: day),
            (Id: 2, Severity: "4", Last: day.AddDays(-5)),
            (Id: 3, Severity: "4", Last: day.AddDays(1)),
            (Id: 4, Severity: "garbage", Last: day.AddDays(9)),
            (Id: 5, Severity: "3", Last: day),
            (Id: 6, Severity: (string?)null, Last: day.AddDays(2)),
        };

        var order = SeverityScale.TriageOrder(rows, r => r.Severity, r => r.Last).Select(r => r.Id).ToArray();

        Assert.Equal(new[] { 3, 2, 5, 1, 4, 6 }, order);
    }
}

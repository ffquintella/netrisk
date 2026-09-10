using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using ServerServices.Integrations;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// The progress trail's formatting and capping rules (Track 4).
///
/// Separate from the service tests and pure, because the two ways a trail can be worse than useless are
/// both here rather than in any one integration: a line that attributes its message to the wrong step,
/// and a trail that grows until the UPDATE carrying it is rejected — the same UPDATE that records the
/// run's outcome.
/// </summary>
[TestSubject(typeof(IntegrationSyncProgressLog))]
public class IntegrationSyncProgressLogTest
{
    private static readonly DateTime At = new(2026, 9, 10, 13, 45, 6, DateTimeKind.Utc);

    // --- one line ---------------------------------------------------------------------------

    [Fact]
    public void ALineCarriesItsUtcTimestampStepAndMessage()
    {
        var line = IntegrationSyncProgressLog.Line(At, "inventory", "Device inventory received.");

        Assert.Equal("[2026-09-10 13:45:06Z] inventory: Device inventory received.", line);
    }

    [Fact]
    public void ALineWithACountReportsIt()
    {
        var line = IntegrationSyncProgressLog.Line(At, "cves", "Records received.", 412);

        Assert.Contains("(412 item(s))", line);
    }

    [Fact]
    public void AZeroCountIsStillReported()
    {
        // Distinct from "no count given". A step that examined nothing is a finding, not an absence:
        // it is the difference between "the query returned no devices" and "this step does not count".
        var line = IntegrationSyncProgressLog.Line(At, "cves", "Records received.", 0);

        Assert.Contains("(0 item(s))", line);
    }

    [Fact]
    public void ALineWithoutACountReportsNoCount()
    {
        var line = IntegrationSyncProgressLog.Line(At, "cves", "Records received.");

        Assert.DoesNotContain("item(s)", line);
    }

    [Fact]
    public void ABlankStepBecomesAPlaceholderRatherThanNothing()
    {
        // An untagged line reads as a continuation of the line above it, so a blank step silently
        // attributes its message to whatever step ran before it.
        var line = IntegrationSyncProgressLog.Line(At, "   ", "Something happened.");

        Assert.Contains("step: Something happened.", line);
    }

    [Fact]
    public void AMultiLineMessageIsFlattenedToOneLine()
    {
        // A provider error body with newlines in it would otherwise forge line boundaries: the trail
        // would show several untimestamped, unattributed lines that look like separate events.
        var line = IntegrationSyncProgressLog.Line(At, "cves",
            "The request failed.\nHTTP 502\r\nupstream connect error");

        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\r", line);
        Assert.Contains("The request failed.", line);
        Assert.Contains("upstream connect error", line);
    }

    [Fact]
    public void AnEmptyMessageSaysSoRatherThanRenderingAnEmptyLine()
    {
        var line = IntegrationSyncProgressLog.Line(At, "cves", "  ");

        Assert.EndsWith("cves: (no detail)", line);
    }

    [Fact]
    public void AnOverLongLineIsTruncated()
    {
        var line = IntegrationSyncProgressLog.Line(At, "cves", new string('x', 50_000));

        Assert.True(line.Length <= IntegrationSyncProgressLog.MaxLineLength,
            $"A single progress line was {line.Length} characters long.");

        Assert.EndsWith("…", line);
    }

    // --- appending --------------------------------------------------------------------------

    [Fact]
    public void AppendingToNothingStartsTheTrail()
    {
        var trail = IntegrationSyncProgressLog.Append(null, ["first", "second"]);

        Assert.Equal("first\nsecond", trail);
    }

    [Fact]
    public void AppendingKeepsWhatWasAlreadyThere()
    {
        var trail = IntegrationSyncProgressLog.Append("first", ["second"]);

        Assert.Equal("first\nsecond", trail);
    }

    [Fact]
    public void AppendingNothingLeavesTheTrailAlone()
    {
        var trail = IntegrationSyncProgressLog.Append("first", []);

        Assert.Equal("first", trail);
    }

    // --- capping ----------------------------------------------------------------------------

    [Fact]
    public void ATrailUnderTheLimitIsNotTouched()
    {
        var trail = string.Join("\n", Enumerable.Range(0, 10).Select(i => $"line {i}"));

        Assert.Equal(trail, IntegrationSyncProgressLog.Cap(trail));
    }

    [Fact]
    public void AnOverLongTrailIsCappedAtTheLimit()
    {
        var capped = IntegrationSyncProgressLog.Cap(ManyLines(60_000));

        Assert.True(capped.Length <= IntegrationSyncProgressLog.MaxLength,
            $"The capped trail was {capped.Length} characters, over the {IntegrationSyncProgressLog.MaxLength} limit.");
    }

    [Fact]
    public void CappingKeepsTheBeginningAndTheEndAndSaysWhatItDropped()
    {
        var capped = IntegrationSyncProgressLog.Cap(ManyLines(60_000));

        // The end is where the run stopped and is the reason anybody opens the trail; the beginning
        // records what it set out to do. What goes is the repetitive middle — and it has to say so,
        // or the trail reads as though the run jumped from line 30 to line 59,000.
        Assert.Contains("line 0 ", capped);
        Assert.Contains("line 59999 ", capped);
        Assert.Contains(IntegrationSyncProgressLog.ElisionMarker, capped);
        Assert.DoesNotContain("line 30000 ", capped);
    }

    [Fact]
    public void CappingCutsOnLineBoundaries()
    {
        var capped = IntegrationSyncProgressLog.Cap(ManyLines(60_000));

        // A half line reads as a different event than the one it came from, so neither side of the
        // elision may be a fragment.
        var lines = capped.Split('\n');
        var marker = Array.IndexOf(lines, IntegrationSyncProgressLog.ElisionMarker);

        Assert.True(marker > 0, "The elision marker was not on a line of its own.");

        Assert.StartsWith("line ", lines[marker - 1]);
        Assert.StartsWith("line ", lines[marker + 1]);
    }

    [Fact]
    public void AppendingToAnAlreadyFullTrailStaysCapped()
    {
        // The path that actually happens: a long run flushes batch after batch onto a trail that is
        // already at the limit. Capping only on the way in would let it grow without bound.
        var trail = IntegrationSyncProgressLog.Cap(ManyLines(60_000));

        for (var i = 0; i < 20; i++)
            trail = IntegrationSyncProgressLog.Append(trail, ManyLines(500).Split('\n'));

        Assert.True(trail.Length <= IntegrationSyncProgressLog.MaxLength,
            $"The trail grew to {trail.Length} characters across repeated appends.");

        // And the most recent batch is what survived.
        Assert.Contains("line 499 ", trail);
    }

    private static string ManyLines(int count) =>
        string.Join("\n", Enumerable.Range(0, count).Select(i => $"line {i} of the progress trail"));
}

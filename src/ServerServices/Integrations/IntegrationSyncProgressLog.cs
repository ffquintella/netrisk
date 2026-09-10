using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ServerServices.Integrations;

/// <summary>
/// The formatting and capping rules for <c>integration_sync_logs.progress_log</c> (Track 4).
///
/// Pure and static on purpose: every rule here is one that used to be a comment. The trail is the only
/// record of what a long sync was doing when it stopped, so its two failure modes both had to become
/// testable — a line that hides the step it belongs to, and a trail that grows until the UPDATE that
/// writes it is rejected for exceeding <c>max_allowed_packet</c>.
/// </summary>
public static class IntegrationSyncProgressLog
{
    /// <summary>
    /// How much progress text one run may keep.
    ///
    /// 256KB fits a few thousand steps, which is more than any real sync emits, and stays an order of
    /// magnitude under a default 4MB <c>max_allowed_packet</c> — the trail must never be the reason a
    /// completion write fails, because that write is also what records the outcome.
    /// </summary>
    public const int MaxLength = 256 * 1024;

    /// <summary>Longest single line kept. A provider error body can be megabytes on its own.</summary>
    public const int MaxLineLength = 1000;

    /// <summary>The marker left where the middle of an over-long trail was dropped.</summary>
    public const string ElisionMarker = "… [earlier progress dropped: the trail exceeded its size limit] …";

    /// <summary>
    /// Renders one progress line.
    ///
    /// The timestamp is UTC and second-resolution, matching <c>started_at</c>/<c>finished_at</c> on the
    /// same row — a trail timestamped in local time cannot be compared against the row that carries it,
    /// which is the first thing anybody does with it.
    /// </summary>
    /// <param name="atUtc">When the step happened.</param>
    /// <param name="step">Short machine-ish step name, e.g. <c>inventory</c>.</param>
    /// <param name="message">What happened.</param>
    /// <param name="processed">Item count, when the step counts items.</param>
    public static string Line(DateTime atUtc, string step, string message, int? processed = null)
    {
        var text = new StringBuilder();

        text.Append('[').Append(atUtc.ToString("yyyy-MM-dd HH:mm:ss")).Append("Z] ");

        // A step name is never allowed to be blank. An untagged line reads as a continuation of the
        // previous step, so a blank one silently attributes its message to whatever ran before it.
        text.Append(string.IsNullOrWhiteSpace(step) ? "step" : step.Trim()).Append(": ");

        text.Append(Collapse(message));

        if (processed != null) text.Append(" (").Append(processed.Value).Append(" item(s))");

        var line = text.ToString();

        return line.Length <= MaxLineLength ? line : line[..(MaxLineLength - 1)] + "…";
    }

    /// <summary>
    /// Appends <paramref name="lines"/> to <paramref name="existing"/> and caps the result.
    ///
    /// Capping drops from the front, not the back. The end of the trail is where the run stopped and is
    /// the whole reason anybody opens it; the beginning is kept too, because it records what the run set
    /// out to do. What goes is the repetitive middle.
    /// </summary>
    public static string Append(string? existing, IEnumerable<string> lines)
    {
        var text = new StringBuilder(existing ?? "");

        foreach (var line in lines)
        {
            if (text.Length > 0) text.Append('\n');
            text.Append(line);
        }

        return Cap(text.ToString());
    }

    /// <summary>Caps a trail at <see cref="MaxLength"/>, keeping the head and the tail.</summary>
    public static string Cap(string text)
    {
        if (text.Length <= MaxLength) return text;

        // A tenth to the head, the rest to the tail. The head only has to answer "what was this run
        // and what did it start with", which is a handful of lines; everything else is worth more.
        var headBudget = MaxLength / 10;
        var tailBudget = MaxLength - headBudget - ElisionMarker.Length - 2;

        var head = text[..headBudget];
        var tail = text[^tailBudget..];

        // Cut on line boundaries so neither end starts or ends mid-line — a half line reads as a
        // different event than the one it came from.
        var headCut = head.LastIndexOf('\n');
        if (headCut > 0) head = head[..headCut];

        var tailCut = tail.IndexOf('\n');
        if (tailCut >= 0 && tailCut < tail.Length - 1) tail = tail[(tailCut + 1)..];

        return head + "\n" + ElisionMarker + "\n" + tail;
    }

    /// <summary>
    /// Flattens a message to one line.
    ///
    /// Newlines in a message would otherwise forge line boundaries: a provider error body containing
    /// <c>\n</c> renders as several untimestamped, unattributed progress lines.
    /// </summary>
    private static string Collapse(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "(no detail)";

        var parts = message
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length == 0 ? "(no detail)" : string.Join(" ⏎ ", parts);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GUIClient.Tools.Hosts;

/// <summary>
/// A finding's severity on NetRisk's 0–4 scale (4 Critical, 3 High, 2 Medium, 1 Low, 0 None), as it
/// is stored: a string on <c>Vulnerability.Severity</c>.
///
/// The server's per-host summary (S38 §5.3) counts a severity that is not one of those five numbers
/// as None, so this does too — otherwise the header badges and the grid would disagree about the
/// same finding.
/// </summary>
public static class SeverityScale
{
    public const int None = 0;
    public const int Critical = 4;

    /// <summary>The numeric severity, 0 for anything unparsable or off the scale.</summary>
    public static int Rank(string? severity) =>
        int.TryParse(severity?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        && value is >= None and <= Critical
            ? value
            : None;

    /// <summary>The modifier class on <c>Border.severity</c>: <c>s0</c>…<c>s4</c>.</summary>
    public static string ClassFor(string? severity) =>
        "s" + Rank(severity).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The default order of a host's findings (S38 §3.3): "needs action first" — severity
    /// descending, then the most recently detected. Stable, so equal rows keep the server's order.
    /// </summary>
    public static IEnumerable<T> TriageOrder<T>(IEnumerable<T> rows, Func<T, string?> severity,
        Func<T, DateTime> lastDetection) =>
        rows.OrderByDescending(row => Rank(severity(row)))
            .ThenByDescending(lastDetection);
}

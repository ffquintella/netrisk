using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GUIClient.Tools.Hosts;

/// <summary>
/// Builds the one server-side filter string the Hosts list is loaded with (S38 §3.1), from the
/// search box and the four facets. Comma is AND, as in Sieve; an unset facet contributes nothing,
/// so "everything blank" is the empty filter rather than a clause that happens to match all.
///
/// Values typed or chosen by the user are escaped (<c>\</c>, <c>,</c>, <c>|</c>): the server
/// splits on unescaped commas and pipes before it reads a value, so a search for <c>web,db</c>
/// would otherwise arrive as a host-name clause plus a malformed second one.
/// </summary>
public static class HostsFilterComposer
{
    /// <summary>
    /// The Criticality facet's "Not set" choice. Zero is not on the 1–5 scale, so it cannot
    /// collide with a level; it composes to <c>criticality==null</c>.
    /// </summary>
    public const int CriticalityNotSet = 0;

    /// <param name="text">Host-name search; blank for none.</param>
    /// <param name="status">Host status (<c>IntStatus.Active</c>, <c>IntStatus.Retired</c>), null for all.</param>
    /// <param name="teamId">Responsible team id, null for all.</param>
    /// <param name="criticality">1–5, <see cref="CriticalityNotSet"/> for hosts with none, null for all.</param>
    /// <param name="environment">Exact environment value, blank for all.</param>
    public static string Compose(string? text, int? status, int? teamId, int? criticality, string? environment)
    {
        var clauses = new List<string>(5);

        if (!string.IsNullOrWhiteSpace(text))
            clauses.Add("hostName@=" + Escape(text.Trim()));

        if (status is { } s)
            clauses.Add("status==" + s.ToString(CultureInfo.InvariantCulture));

        if (teamId is { } t)
            clauses.Add("teamId==" + t.ToString(CultureInfo.InvariantCulture));

        if (criticality == CriticalityNotSet)
            clauses.Add("criticality==null");
        else if (criticality is { } c)
            clauses.Add("criticality==" + c.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(environment))
            clauses.Add("environment==" + Escape(environment.Trim()));

        return string.Join(",", clauses);
    }

    /// <summary>Backslash-escape the characters the server's filter parser splits on.</summary>
    public static string Escape(string value)
    {
        var result = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (c is '\\' or ',' or '|') result.Append('\\');
            result.Append(c);
        }

        return result.ToString();
    }
}

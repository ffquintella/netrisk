using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// The sentence of a profile validation issue (S40 §6.2.3): <c>RiskIssue_‹code›</c> with the issue's arguments in a fixed
/// order per code (indexed placeholders, so every language formats them alike), prefixed by the parameter's label. A code
/// no resource declares falls back to the code itself, never to an empty line.
/// </summary>
public static class ProfileIssueText
{
    /// <summary>The argument names each code's template reads, in placeholder order ({0}, {1}, …).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ArgumentOrder = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["full_mode_prerequisites_missing"] = [],
        ["unknown_mode"] = ["mode"],
        ["ter_requires_full_mode"] = ["value"],
        ["parameter_out_of_range"] = ["value", "min", "max"],
        ["locked_parameter"] = ["value", "locked"],
        ["omega_sum"] = ["sum"],
        ["omega_min_categories"] = ["count"],
        ["bands_invalid"] = ["index"],
        ["exploitation_order"] = ["ceiling", "floor"],
        ["quality_order"] = [],
        ["coverage_order"] = [],
        ["anchors_not_monotonic"] = [],
        ["ssc_anchor_endpoints"] = [],
        ["unknown_factor"] = [],
        ["quota_sum"] = ["sum"],
        ["unknown_source"] = ["source"],
        ["incident_window_too_short"] = ["minimum"],
        ["inventory_window_too_short"] = ["minimum"],
        ["register_copies_incoherent"] = [],
        ["tail_lock"] = ["product", "minimum"],
        ["rebaseline_preset_change"] = ["from", "to"],
        ["mode_change"] = ["from", "to"],
        ["rebaseline_omega_pace"] = ["from", "to", "limit"],
        ["rebaseline_parameter_count"] = ["count", "limit"],
        ["board_ref_required"] = []
    };

    /// <summary>The resource key of an issue; <c>bands_invalid</c> has one per reason (count, first_not_zero, …).</summary>
    public static string ResourceKey(RiskIndexValidationIssueDto issue) =>
        issue.Code == "bands_invalid" && issue.Args.TryGetValue("reason", out var reason)
            ? "RiskIssue_bands_invalid_" + reason
            : "RiskIssue_" + issue.Code;

    /// <summary>The message alone: the template with its arguments, or the code when no template exists.</summary>
    public static string Message(RiskIndexValidationIssueDto issue, TextLookup text)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var template = text(ResourceKey(issue));
        if (string.IsNullOrWhiteSpace(template)) return issue.Code;

        var names = ArgumentOrder.TryGetValue(issue.Code, out var order) ? order : [];
        var values = names.Select(n => (object)(issue.Args.TryGetValue(n, out var v) ? Number(v) : "—")).ToArray();
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, values);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>"‹label›: ‹message›" when the issue names a parameter whose label exists; the message alone otherwise.</summary>
    public static string Line(RiskIndexValidationIssueDto issue, TextLookup text)
    {
        var message = Message(issue, text);
        if (issue.ParameterKey is not { Length: > 0 } key) return message;
        var label = Label(key, text);
        return label is null ? message : label + ": " + message;
    }

    /// <summary>The label of a key or of the composite it belongs to (<c>u.EXP.D</c> → the ceilings' label · EXP.D).</summary>
    public static string? Label(string key, TextLookup text)
    {
        if (text(ProfileParameterRows.LabelKey(key)) is { Length: > 0 } own) return own;
        var dot = key.IndexOf('.');
        if (dot <= 0) return null;
        return text(ProfileParameterRows.LabelKey(key[..dot])) is { Length: > 0 } parent ? parent + " · " + key[(dot + 1)..] : null;
    }

    /// <summary>An invariant number argument shown in the reader's culture; any other text unchanged.</summary>
    private static string Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.######", CultureInfo.CurrentCulture)
            : value;
}

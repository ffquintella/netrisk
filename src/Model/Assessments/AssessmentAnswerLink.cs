using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DAL.Entities;

namespace Model.Assessments;

/// <summary>
/// How a run's answer names the option it chose (M58, T280; S39 D-16). The id is the link; the text is only how answers
/// saved before the link — and clients older than it — are matched, and a match counts only when it is unique.
/// <c>Data/114.sql</c>'s backfill applies the same rule in SQL (pinned by <c>AssessmentAnswerLinkBackfillTest</c>).
/// </summary>
public static class AssessmentAnswerLink
{
    /// <summary>Why an answer's text names no option.</summary>
    public enum Unmatched
    {
        /// <summary>No option of the question carries the text.</summary>
        NoMatch = 1,

        /// <summary>More than one option of the question carries it (case aside).</summary>
        Ambiguous = 2
    }

    /// <summary>
    /// The answer text a draft stores: a JSON string (<c>"Yes"</c>), or — written by older clients — the bare text.
    /// Empty when there is none.
    /// </summary>
    public static string DecodeText(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(contentJson);
            if (document.RootElement.ValueKind == JsonValueKind.String) return document.RootElement.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            // Not JSON: an older client's bare text.
        }

        return contentJson.Trim('"');
    }

    /// <summary>
    /// The one option of <paramref name="options"/> whose text equals <paramref name="text"/>, ignoring case; null with the
    /// reason when none or several do.
    /// </summary>
    public static (AssessmentAnswer? Option, Unmatched? Reason) MatchByText(IEnumerable<AssessmentAnswer> options, string? text)
    {
        if (string.IsNullOrEmpty(text)) return (null, Unmatched.NoMatch);

        var matches = options.Where(o => string.Equals(o.Answer, text, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches.Count switch
        {
            1 => (matches[0], null),
            0 => (null, Unmatched.NoMatch),
            _ => (null, Unmatched.Ambiguous)
        };
    }

    /// <summary>
    /// The option an answer chose: the one named by <paramref name="optionId"/> when it is among
    /// <paramref name="options"/>, else the unique text match. Null when neither names one.
    /// </summary>
    public static AssessmentAnswer? Resolve(IReadOnlyCollection<AssessmentAnswer> options, int? optionId, string? text)
    {
        if (optionId is { } id) return options.FirstOrDefault(o => o.Id == id);
        return MatchByText(options, text).Option;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace GUIClient.Tools;

/// <summary>
/// Whether a connection editor's template fields differ from what the server has stored
/// (Track 4 milestone 4.6).
///
/// Extracted from <c>IntegrationsViewModel</c> rather than left inline because it decides whether
/// pressing *Preview* performs a **write**. The preview renders the *saved* connection, so an unsaved
/// template edit would otherwise preview the old text — an operator tweaks a placeholder, sees no
/// change and concludes the placeholder is wrong. Saving first fixes that, and this predicate is what
/// keeps it from turning a read-only-looking button into a save on every click.
///
/// Takes the values rather than the connection types, so it depends on nothing: that is what lets
/// <c>GUIClient.Tests</c> compile it directly without pulling EF Core or Avalonia into a headless run,
/// the same arrangement as <c>ScaleAnchorFormatter</c> and <c>RiskScoreSummary</c>.
/// </summary>
public static class IssueTemplateDraft
{
    /// <summary>
    /// True when any field the preview depends on has been edited.
    ///
    /// The caller passes exactly the inputs to a rendered draft — the two templates, the priority
    /// mapping and the default labels. Anything else on the form (the base URL, the poll interval)
    /// cannot change what the preview shows, so including it would mean saving for no reason.
    /// </summary>
    /// <param name="pairs">Stored value and edited value, in that order, one pair per field.</param>
    public static bool AnyChanged(params (string? Stored, string? Draft)[] pairs) =>
        pairs.Any(pair => Changed(pair.Stored, pair.Draft));

    /// <summary>
    /// Ordinal, and not trimmed.
    ///
    /// A template's trailing newline is part of what it renders, and a case change to a placeholder
    /// (<c>{{severity}}</c> for <c>{{Severity}}</c>) is an edit the operator is entitled to see
    /// refreshed even though the substitution itself is case-insensitive.
    ///
    /// Null and empty compare equal, because the editor writes <c>""</c> where the server stores null
    /// for a field nobody has touched — treating those as different would make every preview a save,
    /// which is the whole thing this predicate exists to avoid.
    /// </summary>
    public static bool Changed(string? stored, string? draft) =>
        !string.Equals(stored ?? string.Empty, draft ?? string.Empty, StringComparison.Ordinal);
}

using Model.Notifications;

namespace ServerServices.Notifications;

/// <summary>
/// Rendering helpers shared by the providers (Track 4 milestone 4.1.2).
///
/// Small, but shared on purpose: severity colour and the "and N more" suffix appearing slightly
/// differently in Slack and Teams is exactly the kind of drift that makes two channels look like two
/// products.
/// </summary>
internal static class NotificationRendering
{
    /// <summary>
    /// Hex colours for severity, used by Slack attachments and Teams card accents. Red/orange/yellow
    /// /blue rather than a theme token because these are rendered by someone else's client, which
    /// knows nothing about NetRisk's palette.
    /// </summary>
    internal static string ColourFor(int? severity) => severity switch
    {
        4 => "#B4232B",
        3 => "#D9822B",
        2 => "#D9B02B",
        1 => "#3B7DBF",
        _ => "#6C757D"
    };

    /// <summary>Teams Adaptive Card colour vocabulary — it takes names, not hex.</summary>
    internal static string AdaptiveColourFor(int? severity) => severity switch
    {
        4 => "attention",
        3 => "warning",
        2 => "warning",
        1 => "accent",
        _ => "default"
    };

    /// <summary>
    /// The title as shown, with the aggregation suffix a digest needs. Without it a digest of 42
    /// imports reads as one import, which is a lie the operator acts on.
    /// </summary>
    internal static string TitleWithCount(NotificationMessage message) =>
        message.AggregatedCount > 1
            ? $"{message.Title} (+{message.AggregatedCount - 1} more)"
            : message.Title;

    /// <summary>
    /// A plain-text rendering used by the email provider's text alternative and as the fallback text
    /// Slack shows in notifications and screen readers.
    /// </summary>
    internal static string PlainText(NotificationMessage message)
    {
        var lines = new List<string> { TitleWithCount(message) };

        if (!string.IsNullOrWhiteSpace(message.Body)) lines.Add(message.Body);

        lines.AddRange(message.Fields.Select(f => $"{f.Label}: {f.Value}"));

        if (!string.IsNullOrWhiteSpace(message.Link)) lines.Add(message.Link);

        return string.Join("\n", lines);
    }
}

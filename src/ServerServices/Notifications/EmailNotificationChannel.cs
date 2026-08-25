using System.Diagnostics;
using System.Net;
using System.Text;
using DAL.Enums;
using Model.Notifications;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Notifications;

/// <summary>
/// Email provider, adapting the existing SMTP path onto <see cref="INotificationChannel"/>
/// (Track 4 milestone 4.1.2).
///
/// It renders its own HTML rather than reusing one of the Razor templates in
/// <c>ServerServices/EmailTemplates</c>: those templates are per-feature (a vulnerability update, a
/// task execution) and a channel-agnostic notification has no feature. Building the body here also
/// keeps the field grid identical in shape to the Slack and Teams renderings, which is what makes
/// three channels feel like one product.
///
/// A plain-text alternative is composed alongside the HTML because a security alert that renders as
/// a blank message in a text-only client is an alert that did not arrive.
/// </summary>
public class EmailNotificationChannel(ILogger logger, IEmailService emailService) : INotificationChannel
{
    public NotificationChannelKind Kind => NotificationChannelKind.Email;

    public string Name => "Email";

    public async Task<DeliveryResult> SendAsync(NotificationMessage message,
        ChannelConfiguration configuration, CancellationToken ct = default)
    {
        var recipients = ParseRecipients(configuration.Recipients);

        if (recipients.Count == 0)
            return DeliveryResult.Permanent("This email channel has no recipients configured.");

        var subject = BuildSubject(message, configuration);
        var body = BuildHtmlBody(message);
        var text = NotificationRendering.PlainText(message);

        logger.Debug("Emailing a {Event} notification to {Count} recipient(s)",
            message.EventType, recipients.Count);

        var failures = new List<string>();

        foreach (var recipient in recipients)
        {
            try
            {
                await emailService.SendNotificationAsync(recipient, subject, body, text);
            }
            catch (Exception ex)
            {
                // Per recipient rather than all-or-nothing: one bad address must not stop the other
                // four people from being told about a critical finding.
                failures.Add($"{recipient}: {ex.Message}");
            }
        }

        if (failures.Count == 0) return DeliveryResult.Delivered();

        var detail = $"Could not email {failures.Count} of {recipients.Count} recipient(s): "
                     + string.Join("; ", failures.Take(3));

        // Retryable: an SMTP failure is nearly always transient (a down relay, a throttle), and the
        // cases that are not — a permanently invalid address — are visible in the delivery log.
        return failures.Count == recipients.Count
            ? DeliveryResult.Retry(detail)
            : DeliveryResult.Delivered();
    }

    public async Task<ChannelTestResult> TestAsync(ChannelConfiguration configuration,
        CancellationToken ct = default)
    {
        var recipients = ParseRecipients(configuration.Recipients);

        if (recipients.Count == 0) return ChannelTestResult.Fail("No recipients are configured.");

        var stopwatch = Stopwatch.StartNew();
        var result = await SendAsync(SlackNotificationChannel.TestMessage(), configuration, ct);
        stopwatch.Stop();

        return result.Success
            ? ChannelTestResult.Ok($"Test message sent to {recipients.Count} recipient(s).",
                stopwatch.ElapsedMilliseconds)
            : ChannelTestResult.Fail(result.Error ?? "The test message could not be sent.",
                stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Splits on comma and semicolon, both of which people type. Empty entries are dropped rather
    /// than becoming an attempt to mail the empty string.
    /// </summary>
    internal static List<string> ParseRecipients(string? recipients) =>
        (recipients ?? string.Empty)
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    internal static string BuildSubject(NotificationMessage message, ChannelConfiguration configuration)
    {
        var prefix = string.IsNullOrWhiteSpace(configuration.SubjectPrefix)
            ? "[NetRisk]"
            : configuration.SubjectPrefix.Trim();

        return $"{prefix} [{message.SeverityLabel}] {NotificationRendering.TitleWithCount(message)}";
    }

    /// <summary>
    /// Inline styles rather than a stylesheet, and a table rather than flexbox: mail clients strip
    /// <c>&lt;style&gt;</c> blocks and Outlook does not implement flexbox. Every value that comes
    /// from a finding is HTML-encoded — a finding title is attacker-influenced text.
    /// </summary>
    internal static string BuildHtmlBody(NotificationMessage message)
    {
        var colour = NotificationRendering.ColourFor(message.Severity);
        var html = new StringBuilder();

        html.Append("<div style=\"font-family:Segoe UI,Helvetica,Arial,sans-serif;font-size:14px;color:#222\">");
        html.Append($"<div style=\"border-left:4px solid {colour};padding-left:12px;margin-bottom:16px\">");
        html.Append($"<h2 style=\"margin:0 0 4px 0;font-size:18px\">{Encode(NotificationRendering.TitleWithCount(message))}</h2>");
        html.Append($"<div style=\"color:{colour};font-weight:600\">{Encode(message.SeverityLabel)}</div>");
        html.Append("</div>");

        if (!string.IsNullOrWhiteSpace(message.Body))
            html.Append($"<p style=\"margin:0 0 16px 0\">{Encode(message.Body).Replace("\n", "<br/>")}</p>");

        if (message.Fields.Count > 0)
        {
            html.Append("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;margin-bottom:16px\">");
            foreach (var field in message.Fields)
            {
                html.Append("<tr>");
                html.Append($"<td style=\"border:1px solid #ddd;font-weight:600;background:#f6f6f6\">{Encode(field.Label)}</td>");
                html.Append($"<td style=\"border:1px solid #ddd\">{Encode(field.Value)}</td>");
                html.Append("</tr>");
            }
            html.Append("</table>");
        }

        if (!string.IsNullOrWhiteSpace(message.Link))
            html.Append($"<p><a href=\"{Encode(message.Link)}\" "
                        + $"style=\"background:{colour};color:#fff;padding:8px 14px;text-decoration:none;"
                        + "border-radius:4px;display:inline-block\">Open in NetRisk</a></p>");

        html.Append($"<p style=\"color:#888;font-size:12px\">{Encode(NotificationCatalog.NameOf(message.EventType))} "
                    + $"· {message.OccurredAt:yyyy-MM-dd HH:mm} UTC</p>");
        html.Append("</div>");

        return html.ToString();
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

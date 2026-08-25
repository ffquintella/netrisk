using System.Collections.Generic;
using System.Threading.Tasks;
using ServerServices.Interfaces;

namespace ServerServices.Tests.Mock;

/// <summary>
/// A mail sender that records instead of sending.
///
/// Recording rather than discarding, because the Track 4 email notification channel is only testable if
/// something can assert what it composed — a mock that swallows the body makes "the HTML escapes a
/// finding title" untestable.
/// </summary>
public class EmailMock: IEmailService
{
    /// <summary>One entry per templated send.</summary>
    public List<(string To, string Subject, string Template)> TemplatedSends { get; } = new();

    /// <summary>One entry per pre-rendered notification send.</summary>
    public List<(string To, string Subject, string Html, string? Text)> NotificationSends { get; } = new();

    /// <summary>Set to make the next sends throw, standing in for a refused SMTP relay.</summary>
    public bool FailSends { get; set; }

    public Task SendEmailAsync(string to, string subject, string template, string localizationCode, object parameters)
    {
        if (FailSends) throw new System.Exception("The mock mail sender was configured to fail.");

        TemplatedSends.Add((to, subject, template));
        return Task.CompletedTask;
    }

    public Task SendNotificationAsync(string to, string subject, string htmlBody, string? plainTextBody = null)
    {
        if (FailSends) throw new System.Exception("The mock mail sender was configured to fail.");

        NotificationSends.Add((to, subject, htmlBody, plainTextBody));
        return Task.CompletedTask;
    }
}


using System.Reflection;

namespace ServerServices.Interfaces;

public interface IEmailService
{
    public Task SendEmailAsync(string to, string subject, string template, string localizationCode, Object parameters);

    /// <summary>
    /// Sends an already-rendered message (Track 4 milestone 4.1.2).
    ///
    /// The notification channel renders its own HTML because a channel-agnostic notification has no
    /// per-feature Razor template to use, so it needs a way in that does not go through
    /// <c>UsingTemplateFromFile</c>. The plain-text alternative is a parameter rather than derived
    /// here: only the caller knows the source structure well enough to flatten it readably.
    /// </summary>
    Task SendNotificationAsync(string to, string subject, string htmlBody, string? plainTextBody = null);
}

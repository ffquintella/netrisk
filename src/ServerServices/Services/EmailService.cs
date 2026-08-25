using System.Reflection;
using FluentEmail.Core;
using Serilog;
using ServerServices.Interfaces;
using Tools.Extensions;

namespace ServerServices.Services;

public class EmailService: IEmailService
{
    private readonly IFluentEmail _fluentEmail;

    public EmailService(IFluentEmail fluentEmail) {
        _fluentEmail = fluentEmail;
    }
    
    
    public async Task SendEmailAsync(string to, string subject, string template, string localizationCode, Object parameters)
    {
        try
        {
            var currentDir = Assembly.GetExecutingAssembly().AssemblyDirectory();
            await ResetRecipients()
                .To(to)
                .Subject(subject)
                .UsingTemplateFromFile($"{currentDir}/EmailTemplates/{template}-{localizationCode}.cshtml",
                    parameters).SendAsync();
        }
        catch (Exception e)
        {
            Log.Error(e,
                "Error sending email to {To} with subject {Subject} and template {Template} and localizationCode {LocalizationCode} and parameters {Parameters}. Message: {Message}",
                to, subject, template, localizationCode, parameters, e.Message);
            throw new Exception("Error sending mail.", e);
        }
    }

    public async Task SendNotificationAsync(string to, string subject, string htmlBody, string? plainTextBody = null)
    {
        try
        {
            var email = ResetRecipients()
                .To(to)
                .Subject(subject)
                .Body(htmlBody, true);

            if (!string.IsNullOrWhiteSpace(plainTextBody))
                email = email.PlaintextAlternativeBody(plainTextBody);

            var response = await email.SendAsync();

            // FluentEmail reports a refused send as an unsuccessful response rather than an
            // exception, so a caller that only catches would treat a rejected message as delivered —
            // which for a notification channel means the delivery log says "sent" and nobody was told.
            if (!response.Successful)
                throw new Exception(response.ErrorMessages.Count > 0
                    ? string.Join("; ", response.ErrorMessages)
                    : "The SMTP sender refused the message without giving a reason.");
        }
        catch (Exception e)
        {
            Log.Error(e, "Error sending notification email to {To} with subject {Subject}: {Message}",
                to, subject, e.Message);
            throw new Exception("Error sending mail.", e);
        }
    }

    /// <summary>
    /// <c>IFluentEmail</c> is a builder whose recipient list accumulates, and one instance is injected
    /// per service. Two sends from the same instance would therefore mail the second message to the
    /// first message's recipient as well — so the address lists are cleared before each send.
    /// </summary>
    private IFluentEmail ResetRecipients()
    {
        _fluentEmail.Data.ToAddresses.Clear();
        _fluentEmail.Data.CcAddresses.Clear();
        _fluentEmail.Data.BccAddresses.Clear();
        return _fluentEmail;
    }
}

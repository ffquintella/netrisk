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
            await _fluentEmail
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
}
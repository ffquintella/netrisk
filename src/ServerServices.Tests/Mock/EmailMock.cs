using System.Threading.Tasks;
using ServerServices.Interfaces;

namespace ServerServices.Tests.Mock;

public class EmailMock: IEmailService
{
    public Task SendEmailAsync(string to, string subject, string template, string localizationCode, object parameters)
    {
        return Task.CompletedTask;
    }
}
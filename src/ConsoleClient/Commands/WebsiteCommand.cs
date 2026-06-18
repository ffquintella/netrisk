using System.Diagnostics.CodeAnalysis;
using ConsoleClient.Commands.Settings;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class WebsiteCommand : Command<WebsiteSettings>
{
    private readonly ISyncClient _syncClient;
    private readonly ISyncKeyService _keyService;

    public WebsiteCommand(ISyncClient syncClient, ISyncKeyService keyService)
    {
        _syncClient = syncClient;
        _keyService = keyService;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] WebsiteSettings settings, CancellationToken cancellationToken)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        switch (settings.Operation)
        {
            case "enroll":
                return ExecuteEnroll(settings);
            default:
                AnsiConsole.MarkupLine("[red]*** Invalid operation selected ***[/]");
                AnsiConsole.MarkupLine("[white] valid options are: enroll [/]");
                return -1;
        }
    }

    private int ExecuteEnroll(WebsiteSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Url))
        {
            AnsiConsole.MarkupLine("[red]--url is required.[/]");
            return -1;
        }
        if (!_keyService.KeyExists())
        {
            AnsiConsole.MarkupLine("[red]No sync signing key found. Run 'keys create' first.[/]");
            return -1;
        }
        try
        {
            var ok = _syncClient.EnrollAsync(settings.Url!, settings.Insecure).GetAwaiter().GetResult();
            if (!ok)
            {
                AnsiConsole.MarkupLine("[red]Enrollment failed. The website may already be enrolled (TOFU is one-time).[/]");
                return -1;
            }
            AnsiConsole.MarkupLine("[green]Website enrolled. It now trusts this server's signing key.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return -1;
        }
    }
}

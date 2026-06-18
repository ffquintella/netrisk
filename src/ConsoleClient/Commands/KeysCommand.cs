using System.Diagnostics.CodeAnalysis;
using ConsoleClient.Commands.Settings;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class KeysCommand : Command<KeysSettings>
{
    private readonly ISyncKeyService _keyService;
    private readonly ISyncClient _syncClient;

    public KeysCommand(ISyncKeyService keyService, ISyncClient syncClient)
    {
        _keyService = keyService;
        _syncClient = syncClient;
    }

    protected override int Execute([NotNull] CommandContext context, [NotNull] KeysSettings settings, CancellationToken cancellationToken)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        switch (settings.Operation)
        {
            case "create":
                return ExecuteCreate(settings);
            case "rotate":
                return ExecuteRotate(settings);
            case "show":
                return ExecuteShow();
            default:
                AnsiConsole.MarkupLine("[red]*** Invalid operation selected ***[/]");
                AnsiConsole.MarkupLine("[white] valid options are: create, rotate, show [/]");
                return -1;
        }
    }

    private int ExecuteCreate(KeysSettings settings)
    {
        try
        {
            _keyService.CreateKeyPair(settings.Force);
            AnsiConsole.MarkupLine("[green]Sync signing key created.[/]");
            AnsiConsole.MarkupLine($"[white]KeyId:[/] {_keyService.GetKeyId()}");
            AnsiConsole.MarkupLine("[grey]Enroll the website with: netrisk-console website enroll --url <https://site:6443>[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return -1;
        }
    }

    private int ExecuteRotate(KeysSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.WebsiteUrl))
        {
            AnsiConsole.MarkupLine("[red]--website-url is required for rotate so the new key can be installed on the website.[/]");
            return -1;
        }
        try
        {
            var ok = _syncClient.RotateAsync(settings.WebsiteUrl!, settings.Insecure).GetAwaiter().GetResult();
            if (!ok)
            {
                AnsiConsole.MarkupLine("[red]Rotation failed — the website did not accept the new key. The current key is unchanged.[/]");
                return -1;
            }
            AnsiConsole.MarkupLine("[green]Key rotated and installed on the website.[/]");
            AnsiConsole.MarkupLine($"[white]New KeyId:[/] {_keyService.GetKeyId()}");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return -1;
        }
    }

    private int ExecuteShow()
    {
        try
        {
            AnsiConsole.MarkupLine($"[white]KeyId:[/] {_keyService.GetKeyId()}");
            AnsiConsole.WriteLine(_keyService.GetPublicKeyPem());
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message}[/]");
            return -1;
        }
    }
}

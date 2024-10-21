using ConsoleClient.Commands.Settings;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class SettingsCommand(ISettingsService settingsService): Command<SettingsSettings>
{
    
    private ISettingsService SettingsService { get; } = settingsService;
    
    public override int Execute(CommandContext context, SettingsSettings settings)
    {

        switch (settings.Operation)
        {
            case "list":
                return ListSettings();
            case "change_backup_pwd":
                return ChangeBackupPassword(settings);
            default:
                AnsiConsole.MarkupLine("[red]Invalid operation![/]");
                AnsiConsole.MarkupLine("[red]Valid operations are: list, change_backup_pwd[/]");
                return -1;
        }
        
        
    }

    private int ChangeBackupPassword(SettingsSettings settings)
    {
        if (settings.Parameter1 == null || settings.Parameter1 == "")
        {
            AnsiConsole.MarkupLine("[red]Please enter the password![/]");
            return -1;
        }

        return 0;
    }
    
    private int ListSettings()
    {
        AnsiConsole.MarkupLine("[blue]Settings list:[/]");
        AnsiConsole.WriteLine("change_backup_pwd - Change backup password");
        return 0;
    }
}
using System.Diagnostics.CodeAnalysis;
using ConsoleClient.Commands.Settings;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class DatabaseCommand: Command<DatabaseSettings>
{
    public DatabaseCommand()
    {
        
    }
    
    public override int Execute([NotNull] CommandContext context, [NotNull] DatabaseSettings settings)
    {
        
        
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        
        switch (settings.Operation)
        {
            case "init":
                ExecuteInit(context, settings);
                return 0;
            case "backup":
                ExecuteBackup(context, settings);
                return 0;
            case "restore":
                ExecuteRestore(context, settings);
                return 0;
            case "status":
                ExecuteStatus(context, settings);
                return 0;
            default:
                AnsiConsole.MarkupLine("[red]*** Invalid operation selected ***[/]");
                AnsiConsole.MarkupLine("[white] valid options are: status, init, backup, restore [/]");
                return -1;
        }


    }
    
    private void ExecuteInit(CommandContext context, DatabaseSettings settings)
    {
        throw new System.NotImplementedException();
    }
    private void ExecuteBackup(CommandContext context, DatabaseSettings settings)
    {
        throw new System.NotImplementedException();
    }
    private void ExecuteRestore(CommandContext context, DatabaseSettings settings)
    {
        throw new System.NotImplementedException();
    }
    private void ExecuteStatus(CommandContext context, DatabaseSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]**********************[/]");
        AnsiConsole.MarkupLine("[bold]Database status[/]");
        AnsiConsole.MarkupLine("[blue]----------------------[/]");
        
        
    }
}
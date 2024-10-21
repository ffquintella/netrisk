using ConsoleClient.Commands.Settings;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class TechnologyCommand(ITechnologiesService technologiesService): Command<TechnologySettings>
{
    private ITechnologiesService TechnologiesService { get; } = technologiesService;
    
    public override int Execute(CommandContext context, TechnologySettings settings)
    {

        switch (settings.Operation)
        {
            case "list":
                return ListSettings();
            default:
                AnsiConsole.MarkupLine("[red]Invalid operation![/]");
                AnsiConsole.MarkupLine("[red]Valid operations are: list, change_backup_pwd[/]");
                return -1;
        }
        
        
    }
    
    private int ListSettings()
    {
        AnsiConsole.MarkupLine("[blue]Technologies list:[/]");
        
        var tecs = TechnologiesService.GetAll();
        int i = 1;
        foreach (var tec in tecs)
        {
            AnsiConsole.MarkupLine("[blue] " + i + ":[/] "+tec.Name);
            i++;
        }
        
        return 0;
    }
}
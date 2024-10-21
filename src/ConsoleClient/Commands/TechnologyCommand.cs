using ConsoleClient.Commands.Settings;
using MigraDoc.DocumentObjectModel;
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
                return List().Result;
            case "add":
                if (settings.Parameter1.IsValueNullOrEmpty())
                {
                    AnsiConsole.MarkupLine("[red] Please provide a technology name![/]");
                    return -1;
                }
                return Add(settings.Parameter1!).Result;   
            case "remove":
                if (settings.Parameter1.IsValueNullOrEmpty())
                {
                    AnsiConsole.MarkupLine("[red] Please provide a technology name![/]");
                    return -1;
                }
                return Remove(settings.Parameter1!).Result;
            default:
                AnsiConsole.MarkupLine("[red]Invalid operation![/]");
                AnsiConsole.MarkupLine("[red]Valid operations are: list, add, remove[/]");
                return -1;
        }
        
        
    }

    private async Task<int> Add(string technology)
    {
        try
        {
            await TechnologiesService.AddTechnologyAsync(technology); 
            await List();
            return 0;
        }catch(Exception e)
        {
            AnsiConsole.MarkupLine("[red]Error adding technology: "+e.Message+"[/]");
            return -1;
        }
    }
    
    private async Task<int> Remove(string technology)
    {
        try
        {
            await TechnologiesService.RemoveTechnologyAsync(technology);
            await List();
            return 0;
        }catch(Exception e)
        {
            AnsiConsole.MarkupLine("[red]Error removing technology: "+e.Message+"[/]");
            return -1;
        }
    }
    
    private async Task<int> List()
    {
        AnsiConsole.MarkupLine("[blue]Technologies list:[/]");
        
        var tecs = await TechnologiesService.GetAllAsync();
        int i = 1;
        foreach (var tec in tecs)
        {
            AnsiConsole.MarkupLine("[blue] " + i + ":[/] "+tec.Name);
            i++;
        }
        
        return 0;
    }
}
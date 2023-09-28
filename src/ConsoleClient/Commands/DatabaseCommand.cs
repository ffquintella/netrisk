using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ConsoleClient.Commands.Settings;
using ConsoleClient.Models;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ConsoleClient.Commands;

public class DatabaseCommand: Command<DatabaseSettings>
{
    private IDatabaseService DatabaseService { get; }
    
    public DatabaseCommand(IDatabaseService databaseService)
    {
        DatabaseService = databaseService;
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
            case "update":
                ExecuteUpdate(context, settings);
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
                AnsiConsole.MarkupLine("[white] valid options are: status, init, update, backup, restore [/]");
                return -1;
        }


    }
    
    private void ExecuteInit(CommandContext context, DatabaseSettings settings)
    {
        
        AnsiConsole.MarkupLine("[blue]***********************[/]");
        AnsiConsole.MarkupLine("[bold]Database initialization[/]");
        AnsiConsole.MarkupLine("[blue]-----------------------[/]");
        
        
        var dbInfo = GetDatabaseInformation();
        var status = DatabaseService.Status();
        
        AnsiConsole.MarkupLine($"[bold]Status:[/] {status.Status}");
        AnsiConsole.MarkupLine($"[bold]Target Version:[/] {dbInfo.TargetVersion}");
        
        if(status.Status == "Offline") 
        {
            AnsiConsole.MarkupLine("[red]Database is offline[/]");
            return;
        }

        try
        {
            var result = DatabaseService.Init(dbInfo.InitialVersion, dbInfo.TargetVersion);
        
            if (result.Status == "Error")
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {result.Code} - {result.Message}");
                return;
            }
        
            AnsiConsole.MarkupLine($"[green]Success:[/] {result.Message}");
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine("[red]Error during operation[/]");
            Console.WriteLine(e);
            throw;
        }


    }

    private void ExecuteUpdate(CommandContext context, DatabaseSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]***********************[/]");
        AnsiConsole.MarkupLine("[bold]Database UPDATE[/]");
        AnsiConsole.MarkupLine("[blue]-----------------------[/]");
        
        
        var dbInfo = GetDatabaseInformation();
        var status = DatabaseService.Status();
        
        AnsiConsole.MarkupLine($"[bold]Status:[/] {status.Status}");
        AnsiConsole.MarkupLine($"[bold]Current Version:[/] {status.Version}");
        AnsiConsole.MarkupLine($"[bold]Target Version:[/] {dbInfo.TargetVersion}");
        
        if(status.Status == "Offline") 
        {
            AnsiConsole.MarkupLine("[red]Database is offline[/]");
            return;
        }

        try
        {
            var result = DatabaseService.Update(dbInfo.InitialVersion, dbInfo.TargetVersion);
        
            if (result.Status == "Error")
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {result.Code} - {result.Message}");
                return;
            }
        
            AnsiConsole.MarkupLine($"[green]Success:[/] {result.Message}");
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLine("[red]Error during operation[/]");
            Console.WriteLine(e);
            throw;
        }
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
        
        var status = DatabaseService.Status();
        
        AnsiConsole.MarkupLine($"[bold]Status:[/] {status.Status}");
        AnsiConsole.MarkupLine($"[bold]Message:[/] {status.Message}");
        AnsiConsole.MarkupLine($"[bold]Version:[/] {status.Version}");
        AnsiConsole.MarkupLine($"[bold]DBVersion:[/] {status.ServerVersion}");
    }

    private  DatabaseInformation GetDatabaseInformation()
    {

        var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        
        var dbInfoPath = $"{currentDir}/DB/DatabaseInformation.yaml";
        
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention( CamelCaseNamingConvention.Instance)  
            .Build();

        var yml =  File.ReadAllText(dbInfoPath);
        
        var dbInfo = deserializer.Deserialize<DatabaseInformation>(yml);
        
        return dbInfo;
    }
}
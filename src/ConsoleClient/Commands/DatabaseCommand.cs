using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ConsoleClient.Commands.Settings;
using ConsoleClient.Models;
using Model.Database;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ConsoleClient.Commands;

public class DatabaseCommand: Command<DatabaseSettings>
{
    private IDatabaseService DatabaseService { get; }
    private ISchemaUpgradeService SchemaUpgradeService { get; }

    public DatabaseCommand(IDatabaseService databaseService, ISchemaUpgradeService schemaUpgradeService)
    {
        DatabaseService = databaseService;
        SchemaUpgradeService = schemaUpgradeService;
    }

    
    protected override int Execute([NotNull] CommandContext context, [NotNull] DatabaseSettings settings, CancellationToken cancellationToken)
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
            case "fixData":
                return ExecuteFixData(context, settings);
            case "status":
                ExecuteStatus(context, settings);
                return 0;
            case "upgrade-schema":
                return ExecuteUpgradeSchema(context, settings);
            case "baseline":
                return ExecuteBaseline(context, settings);
            default:
                AnsiConsole.MarkupLine("[red]*** Invalid operation selected ***[/]");
                AnsiConsole.MarkupLine("[white] valid options are: status, init, update, backup, restore, fixData, upgrade-schema, baseline [/]");
                return -1;
        }


    }

    private int ExecuteFixData(CommandContext context, DatabaseSettings settings)
    {
        var result = -1;
        
        // Here we will copy the data from the old semi-comma separated fields to the new tables
        if (settings.FixCatalog is true)
        {
            result = DatabaseService.FixData("riskCatalog");
            if(result == 0) AnsiConsole.MarkupLine("[green]Success[/]");
            else AnsiConsole.MarkupLine("[red]Error[/]");
        }
        
        return result;
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
        
        if( int.Parse(status.Version) == dbInfo.TargetVersion)
        {
            AnsiConsole.MarkupLine("[green]Database is up to date[/]");
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
        AnsiConsole.MarkupLine("[blue]***********************[/]");
        AnsiConsole.MarkupLine("[bold]Database backup        [/]");
        AnsiConsole.MarkupLine("[blue]-----------------------[/]");
        
        if(settings.BackupPath != null) DatabaseService.Backup(settings.BackupPath);
        else DatabaseService.Backup();
    }
    private void ExecuteRestore(CommandContext context, DatabaseSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]***********************[/]");
        AnsiConsole.MarkupLine("[bold]Database restore        [/]");
        AnsiConsole.MarkupLine("[blue]-----------------------[/]");

        try
        {
            if (settings.BackupPath != null)
            {
                if(! string.IsNullOrEmpty(settings.BackupPwd))
                    DatabaseService.Restore(settings.BackupPath, settings.BackupPwd);
                else
                    DatabaseService.Restore(settings.BackupPath);
            }
            else AnsiConsole.MarkupLine("[red]No backup path provided[/]");
            
            AnsiConsole.MarkupLine("[green]Success[/]");
            
        }catch(Exception e)
        {
            AnsiConsole.MarkupLine("[red]Error during operation[/]");
            Console.WriteLine(e);
            throw;
        }

    }
    private int ExecuteUpgradeSchema(CommandContext context, DatabaseSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]*****************************[/]");
        AnsiConsole.MarkupLine("[bold]Database schema upgrade (Track 6)[/]");
        AnsiConsole.MarkupLine("[blue]-----------------------------[/]");

        if (string.IsNullOrWhiteSpace(settings.Phase))
        {
            AnsiConsole.MarkupLine("[red]--phase is required (e.g. --phase 1).[/]");
            return -1;
        }

        var environment = string.IsNullOrWhiteSpace(settings.Environment) ? "homolog" : settings.Environment!.ToLowerInvariant();

        SchemaUpgradeReport report;
        if (settings.Check)
        {
            report = SchemaUpgradeService.Check(settings.Phase!, environment);
        }
        else if (settings.DryRun)
        {
            report = SchemaUpgradeService.DryRun(settings.Phase!, environment, settings.Output);
        }
        else
        {
            // A real application is destructive and operates against the selected environment.
            // Production requires interactive name confirmation unless --yes is supplied.
            if (environment == "prod" && !settings.Yes)
            {
                AnsiConsole.MarkupLine("[yellow]Refusing to apply against prod without --yes (interactive confirmation not available here).[/]");
                AnsiConsole.MarkupLine("[white]Run with --check and --dry-run first, then re-run with --yes once reviewed.[/]");
                return -1;
            }
            report = SchemaUpgradeService.Apply(settings.Phase!, environment, settings.Yes);
        }

        RenderReport(report);
        return report.Success ? 0 : -1;
    }

    private int ExecuteBaseline(CommandContext context, DatabaseSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]*****************************[/]");
        AnsiConsole.MarkupLine("[bold]Database baseline (Track 6, Phase 0)[/]");
        AnsiConsole.MarkupLine("[blue]-----------------------------[/]");

        var environment = string.IsNullOrWhiteSpace(settings.Environment) ? "homolog" : settings.Environment!.ToLowerInvariant();
        var report = SchemaUpgradeService.Baseline(environment, settings.Output);

        AnsiConsole.MarkupLine($"[bold]Env:[/] {report.Environment}   [bold]db_version:[/] {report.CurrentVersion}   [bold]MySQL:[/] {report.ServerVersion.EscapeMarkup()}");
        AnsiConsole.MarkupLine($"[bold]Pending migrations:[/] {(report.PendingMigrations.Count == 0 ? "none" : string.Join(", ", report.PendingMigrations).EscapeMarkup())}");
        AnsiConsole.MarkupLine($"[bold]Pending model changes:[/] {(report.HasPendingModelChanges ? "[yellow]YES[/]" : "no")}");

        if (report.RemovalCandidates.Count > 0)
        {
            var table = new Table().AddColumns("Table", "Exists", "Rows", "Recommendation");
            foreach (var c in report.RemovalCandidates)
                table.AddRow(c.Table.EscapeMarkup(), c.Exists ? "yes" : "no",
                    c.Exists ? c.RowCount.ToString() : "-", c.Recommendation);
            AnsiConsole.Write(table);
        }

        if (!string.IsNullOrEmpty(report.OutputPath))
            AnsiConsole.MarkupLine($"[green]Report written to:[/] {report.OutputPath.EscapeMarkup()}");

        AnsiConsole.MarkupLine($"[white]{report.Message.EscapeMarkup()}[/]");
        return report.DatabaseOnline ? 0 : -1;
    }

    private static void RenderReport(SchemaUpgradeReport report)
    {
        AnsiConsole.MarkupLine($"[bold]Mode:[/] {report.Mode}   [bold]Phase:[/] {report.Phase}   [bold]Env:[/] {report.Environment}");
        if (report.CurrentVersion >= 0)
            AnsiConsole.MarkupLine($"[bold]db_version:[/] {report.CurrentVersion} → {report.TargetVersion}");

        foreach (var check in report.Checks)
        {
            var mark = check.Passed ? "[green]PASS[/]" : "[red]FAIL[/]";
            AnsiConsole.MarkupLine($"  {mark} [bold]{check.Name.EscapeMarkup()}[/]: {check.Detail.EscapeMarkup()}");
        }

        if (!string.IsNullOrEmpty(report.GeneratedSql))
        {
            AnsiConsole.MarkupLine("[blue]----- generated SQL -----[/]");
            AnsiConsole.WriteLine(report.GeneratedSql);
            AnsiConsole.MarkupLine("[blue]-------------------------[/]");
            if (!string.IsNullOrEmpty(report.OutputPath))
                AnsiConsole.MarkupLine($"[green]SQL written to:[/] {report.OutputPath.EscapeMarkup()}");
        }

        var color = report.Success ? "green" : "red";
        AnsiConsole.MarkupLine($"[{color}]{report.Message.EscapeMarkup()}[/]");
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
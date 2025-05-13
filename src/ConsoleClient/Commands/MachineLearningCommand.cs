using System.Diagnostics.CodeAnalysis;
using ConsoleClient.Commands.Settings;
using Microsoft.EntityFrameworkCore;
using Parquet;
using Parquet.Data;
using Parquet.Schema;
using Parquet.Serialization;
using ServerServices.Interfaces;
using ServerServices.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class MachineLearningCommand: Command<MachineLearningSettings>
{
    
    private IDalService DalService { get; }
    
    public MachineLearningCommand(IDalService dalService)
    {
        DalService = dalService;
    }
    
    public override int Execute( CommandContext context,  MachineLearningSettings settings)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        
        switch (settings.Operation)
        {
            case "extract":
                ExecuteExtractAsync(context, settings).GetAwaiter().GetResult();
                return 0;
            default:
                AnsiConsole.MarkupLine("[red]*** Invalid operation selected ***[/]");
                AnsiConsole.MarkupLine("[white] valid options are: extract [/]");
                return -1;
        }
    }

    private async Task ExecuteExtractAsync(CommandContext context, MachineLearningSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]**********************[/]");
        AnsiConsole.MarkupLine("[bold]Loading ML data[/]");

         await using var dbContext = DalService.GetContext();
         var vulnerabilities =  dbContext.Vulnerabilities.ToList();
         
         
         var schema = new ParquetSchema(
             new DataField<int>("ID"),
             new DataField<string>("Details"),
             new DataField<string>("Description"));
         
         var column1 = new DataColumn(
             schema.DataFields[0],
             vulnerabilities.Select(i => i.Id).ToArray());

         var column2 = new DataColumn(
             schema.DataFields[1],
             vulnerabilities.Select(i => i.Details).ToArray());
         
         var column3 = new DataColumn(
             schema.DataFields[2],
             vulnerabilities.Select(i => i.Description).ToArray());
        

        //ParquetSerializer.SerializeAsync(vulnerabilities, "/tmp/data.parquet");
        
        using(Stream fs = System.IO.File.OpenWrite(settings.Output)) {
            using(ParquetWriter writer = await ParquetWriter.CreateAsync(schema, fs)) {
                using(ParquetRowGroupWriter groupWriter = writer.CreateRowGroup()) {

                    await groupWriter.WriteColumnAsync(column1);
                    await groupWriter.WriteColumnAsync(column2);
                    await groupWriter.WriteColumnAsync(column3);
                }
            }
        }
    }
}
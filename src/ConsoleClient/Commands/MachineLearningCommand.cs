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
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

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

    // CONVERTE A DATA EM .PARQUET (consertando os typeof) E YAML
    // ==========================================================

    private Array ConvertToTypedArray(Type type, IEnumerable<object?> values)
    {
        if (type == typeof(int) || type == typeof(int?))
            return values.Select(v => v == null ? default(int) : (int)v).ToArray();
        if (type == typeof(string))
            return values.Select(v => v?.ToString()).ToArray();
        if (type == typeof(bool) || type == typeof(bool?))
            return values.Select(v => v == null ? default(bool) : (bool)v).ToArray();
        if (type == typeof(double) || type == typeof(double?))
            return values.Select(v => v == null ? default(double) : (double)v).ToArray();
        if (type == typeof(float) || type == typeof(float?))
            return values.Select(v => v == null ? default(float) : (float)v).ToArray();
        if (type == typeof(long) || type == typeof(long?))
            return values.Select(v => v == null ? default(long) : (long)v).ToArray();

        throw new NotSupportedException($"Tipo no soportado: {type}");
    }

    private async Task ExecuteExtractAsync(CommandContext context, MachineLearningSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]**********************[/]");
        AnsiConsole.MarkupLine("[bold]Loading ML data[/]");

        await using var dbContext = DalService.GetContext();

        // === 1. Exportar tabla vulnerabilities ===
        var vulnerabilities = dbContext.Vulnerabilities.ToList();
        //var vulnerabilities = dbContext.Vulnerabilities.Include(r => r.Risks).ToList();

        if (!vulnerabilities.Any())
            throw new InvalidOperationException("No hay datos en Vulnerabilities para exportar.");

        var supportedTypes = new Type[]
        {
        typeof(int), typeof(int?),
        typeof(string),
        typeof(bool), typeof(bool?),
        typeof(double), typeof(double?),
        typeof(float), typeof(float?),
        typeof(long), typeof(long?)
        };

        await ExportToParquetAndYaml(
            vulnerabilities,
            supportedTypes,
            settings.Output,
            "vulnerabilities"
        );

        // === 2. Exportar tabla risk ===
        var risks = dbContext.Risks.Include(r => r.Vulnerabilities).ToList();
        //var risks = dbContext.Risks.ToList();
        //var risks = dbContext.RiskToTeams.ToList();
        if (!risks.Any())
            AnsiConsole.MarkupLine("[yellow]No hay datos en Risk para exportar.[/]");
        else
            await ExportToParquetAndYaml(
                risks,
                supportedTypes,
                Path.Combine(Path.GetDirectoryName(settings.Output) ?? ".", "risk.parquet"),
                "risk"
            );
    }

    // Método genérico para exportar cualquier lista de objetos a Parquet y YAML
    private async Task ExportToParquetAndYaml<T>(
        List<T> data,
        Type[] supportedTypes,
        string outputPath,
        string tableName)
    {
        if (data == null || data.Count == 0)
            throw new InvalidOperationException("No hay datos para exportar.");

        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var firstItem = data.First();
        var properties = firstItem.GetType().GetProperties();

        var filteredProperties = properties
            .Where(p => supportedTypes.Contains(p.PropertyType))
            .ToArray();

        var fields = filteredProperties
            .Select(p =>
            {
                var type = p.PropertyType;
                if (type == typeof(int) || type == typeof(int?))
                    return (DataField)new DataField<int>(p.Name);
                if (type == typeof(string))
                    return (DataField)new DataField<string>(p.Name);
                if (type == typeof(bool) || type == typeof(bool?))
                    return (DataField)new DataField<bool>(p.Name);
                if (type == typeof(double) || type == typeof(double?))
                    return (DataField)new DataField<double>(p.Name);
                if (type == typeof(float) || type == typeof(float?))
                    return (DataField)new DataField<float>(p.Name);
                if (type == typeof(long) || type == typeof(long?))
                    return (DataField)new DataField<long>(p.Name);

                throw new NotSupportedException($"Tipo no soportado: {type} en propiedad {p.Name}");
            }).ToArray();

        var schema = new ParquetSchema(fields);

        var columns = fields.Select(field =>
        {
            var prop = filteredProperties.First(p => p.Name == field.Name);
            var rawValues = data.Select(v => prop.GetValue(v));
            var typedArray = ConvertToTypedArray(prop.PropertyType, rawValues);
            return new DataColumn(field, typedArray);
        }).ToArray();

        using (Stream fs = System.IO.File.OpenWrite(outputPath))
        {
            using (ParquetWriter writer = await ParquetWriter.CreateAsync(schema, fs))
            {
                using (ParquetRowGroupWriter groupWriter = writer.CreateRowGroup())
                {
                    foreach (var column in columns)
                    {
                        await groupWriter.WriteColumnAsync(column);
                    }
                }
            }
        }

        AnsiConsole.MarkupLine($"[green]Data export completed successfully to Parquet: {outputPath}[/]");

        // CONVERTE A METADATA YAML
        var yamlData = data.Select(v =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in filteredProperties)
            {
                dict[prop.Name] = prop.GetValue(v);
            }
            return dict;
        }).ToList();

        var yamlPath = Path.ChangeExtension(outputPath, ".yaml");
        using (var writer = new StreamWriter(yamlPath))
        {
            serializer.Serialize(writer, yamlData);
        }

        AnsiConsole.MarkupLine($"[green]Data export completed successfully to YAML: {yamlPath}[/]");
    }

    




















    /*private Array ConvertToTypedArray(Type type, IEnumerable<object?> values)
    {
        if (type == typeof(int) || type == typeof(int?))
            return values.Select(v => v == null ? default(int) : (int)v).ToArray();
        if (type == typeof(string))
            return values.Select(v => v?.ToString()).ToArray();
        if (type == typeof(bool) || type == typeof(bool?))
            return values.Select(v => v == null ? default(bool) : (bool)v).ToArray();
        if (type == typeof(double) || type == typeof(double?))
            return values.Select(v => v == null ? default(double) : (double)v).ToArray();
        if (type == typeof(float) || type == typeof(float?))
            return values.Select(v => v == null ? default(float) : (float)v).ToArray();
        if (type == typeof(long) || type == typeof(long?))
            return values.Select(v => v == null ? default(long) : (long)v).ToArray();

        throw new NotSupportedException($"Tipo no soportado: {type}");
    }

    private async Task ExecuteExtractAsync(CommandContext context, MachineLearningSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]**********************[/]");
        AnsiConsole.MarkupLine("[bold]Loading ML data[/]");

        await using var dbContext = DalService.GetContext();
        var vulnerabilities = dbContext.Vulnerabilities.ToList();

        if (!vulnerabilities.Any())
            throw new InvalidOperationException("No hay datos para exportar.");

        var firstItem = vulnerabilities.First();

        var properties = firstItem.GetType().GetProperties();

        var supportedTypes = new Type[]
        {
            typeof(int), typeof(int?),
            typeof(string),
            typeof(bool), typeof(bool?),
            typeof(double), typeof(double?),
            typeof(float), typeof(float?),
            typeof(long), typeof(long?)
        };

        // Aquí defines las columnas que quieres exportar.

        List<string>? columnasAExportar = null; // inclui todas
        //List<string> columnasAExportar = new List<string> { "Id", "Score" }; // inclui algumas

        var filteredProperties = properties
            .Where(p => supportedTypes.Contains(p.PropertyType) &&
                        (columnasAExportar == null || columnasAExportar.Count == 0 || columnasAExportar.Contains(p.Name)))
            .ToArray();

        var fields = filteredProperties
            .Select(p =>
            {
                var type = p.PropertyType;
                if (type == typeof(int) || type == typeof(int?))
                    return (DataField)new DataField<int>(p.Name);
                if (type == typeof(string))
                    return (DataField)new DataField<string>(p.Name);
                if (type == typeof(bool) || type == typeof(bool?))
                    return (DataField)new DataField<bool>(p.Name);
                if (type == typeof(double) || type == typeof(double?))
                    return (DataField)new DataField<double>(p.Name);
                if (type == typeof(float) || type == typeof(float?))
                    return (DataField)new DataField<float>(p.Name);
                if (type == typeof(long) || type == typeof(long?))
                    return (DataField)new DataField<long>(p.Name);

                throw new NotSupportedException($"Tipo no soportado: {type} en propiedad {p.Name}");
            }).ToArray();

        if (fields.Length == 0)
            throw new InvalidOperationException("No se encontraron propiedades con tipos soportados para construir el esquema Parquet.");

        var schema = new ParquetSchema(fields);

        var columns = fields.Select(field =>
        {
            var prop = filteredProperties.First(p => p.Name == field.Name);
            var rawValues = vulnerabilities.Select(v => prop.GetValue(v));
            var typedArray = ConvertToTypedArray(prop.PropertyType, rawValues);
            return new DataColumn(field, typedArray);
        }).ToArray();

        using (Stream fs = System.IO.File.OpenWrite(settings.Output))
        {
            using (ParquetWriter writer = await ParquetWriter.CreateAsync(schema, fs))
            {
                using (ParquetRowGroupWriter groupWriter = writer.CreateRowGroup())
                {
                    foreach (var column in columns)
                    {
                        await groupWriter.WriteColumnAsync(column);
                    }
                }
            }
        }

        AnsiConsole.MarkupLine("[green]Data export completed successfully to Parquet![/]");

        // ================================== YAML ==========================================

        // --- Exportar a YAML ---
        // Serializar la lista de objetos 'vulnerabilities' filtrando solo las propiedades seleccionadas

        var serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        // Crear lista de diccionarios con solo las propiedades filtradas y seleccionadas
        var yamlData = vulnerabilities.Select(v =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in filteredProperties)
            {
                dict[prop.Name] = prop.GetValue(v);
            }
            return dict;
        }).ToList();

        // Definir ruta para archivo YAML (misma ruta que parquet pero con extensión .yaml)
        var yamlPath = Path.ChangeExtension(settings.Output, ".yaml");

        using (var writer = new StreamWriter(yamlPath))
        {
            serializer.Serialize(writer, yamlData);
        }

        AnsiConsole.MarkupLine($"[green]Data export completed successfully to YAML: {yamlPath}[/]");



    }*/






    /*private async Task ExecuteExtractAsync(CommandContext context, MachineLearningSettings settings)
    {
        AnsiConsole.MarkupLine("[blue]**********************[/]");
        AnsiConsole.MarkupLine("[bold]Loading ML data[/]");

         await using var dbContext = DalService.GetContext();
         var vulnerabilities =  dbContext.Vulnerabilities.ToList();
         
         
         var schema = new ParquetSchema(
             new DataField<int>("Id"),
             new DataField<string>("Severity"),
             new DataField<string>("Details"),
             new DataField<string>("Description"));
         
         var column1 = new DataColumn(
             schema.DataFields[0],
             vulnerabilities.Select(i => i.Id).ToArray());

         var column2 = new DataColumn(
             schema.DataFields[1],
             vulnerabilities.Select(i => i.Severity).ToArray());

         var column3 = new DataColumn(
             schema.DataFields[2],
             vulnerabilities.Select(i => i.Details).ToArray());

         var column4 = new DataColumn(
             schema.DataFields[3],
             vulnerabilities.Select(i => i.Description).ToArray());


        //ParquetSerializer.SerializeAsync(vulnerabilities, "/tmp/data.parquet");

        using (Stream fs = System.IO.File.OpenWrite(settings.Output)) {
            using(ParquetWriter writer = await ParquetWriter.CreateAsync(schema, fs)) {
                using(ParquetRowGroupWriter groupWriter = writer.CreateRowGroup()) {

                    await groupWriter.WriteColumnAsync(column1);
                    await groupWriter.WriteColumnAsync(column2);
                    await groupWriter.WriteColumnAsync(column3);
                    await groupWriter.WriteColumnAsync(column4);
                }
            }
        }
    }*/






}
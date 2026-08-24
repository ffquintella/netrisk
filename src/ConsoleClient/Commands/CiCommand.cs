using System.Text.Json;
using ConsoleClient.Commands.Settings;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Findings;
using ServerServices.Interfaces;
using ServerServices.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

/// <summary>
/// <c>netrisk-console ci gate</c> — evaluates an import against a gating policy and exits non-zero
/// when it fails (Track 3 milestone 3.5.4).
///
/// A CLI subcommand rather than a server endpoint because the exit code <em>is</em> the interface: a
/// pipeline step succeeds or fails on it, and asking every CI platform to parse a JSON body and map
/// it to an exit code is the thing this exists to avoid.
/// </summary>
public class CiCommand(IDalService dalService, IFindingIngestionService ingestionService)
    : AsyncCommand<CiSettings>
{
    /// <summary>Exit code for a policy violation. Distinct from 1 so a violation is not confused
    /// with a usage error or a crash.</summary>
    public const int GateFailedExitCode = 2;

    protected override async Task<int> ExecuteAsync(CommandContext context, CiSettings settings,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(settings.Operation, "gate", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[red]Unknown operation.[/] The only supported operation is 'gate'.");
            return 1;
        }

        if (settings.ImportId == null)
        {
            AnsiConsole.MarkupLine("[red]--job is required.[/] Pass the import id the upload returned.");
            return 1;
        }

        try
        {
            var import = await ingestionService.GetImportAsync(settings.ImportId.Value);

            // Counted here rather than in the policy evaluator so the evaluator stays a pure
            // function of the import's counts: only the sla-breach policy needs a query, and it
            // needs one because a due date already in the past at import time is not something the
            // per-severity counts can express.
            var slaBreached = await CountSlaBreachedAsync(settings.ImportId.Value);

            var result = CiGatePolicy.Evaluate(settings.FailOn, import, slaBreached);

            if (settings.Json) PrintJson(import, result);
            else PrintTable(import, result);

            return result.Failed ? GateFailedExitCode : 0;
        }
        catch (DataNotFoundException)
        {
            AnsiConsole.MarkupLine($"[red]Import {settings.ImportId} was not found.[/]");
            return 1;
        }
        catch (InvalidParameterException ex)
        {
            AnsiConsole.MarkupLine($"[red]Invalid policy:[/] {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Findings this import created that are already past their remediation deadline. Non-zero only
    /// when the report carried a real first-seen date older than the SLA window — a scanner
    /// reporting a vulnerability it first saw months ago.
    /// </summary>
    private async Task<int> CountSlaBreachedAsync(int importId)
    {
        await using var db = dalService.GetContext();

        var today = DateTime.UtcNow.Date;

        return await db.Vulnerabilities
            .Where(v => v.LastImportId == importId
                        && v.SlaDueDate != null
                        && v.SlaDueDate.Value < today
                        && (v.LifecycleStatus == FindingStatus.Active || v.LifecycleStatus == FindingStatus.Verified))
            .CountAsync();
    }

    private static void PrintTable(DAL.Entities.ScanImport import, GateResult result)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Metric");
        table.AddColumn("Value");

        table.AddRow("Import", import.Id.ToString());
        table.AddRow("Importer", import.Importer);
        table.AddRow("Status", ((ScanImportStatus)import.Status).ToString());
        table.AddRow("New", import.NewCount.ToString());
        table.AddRow("Updated", import.UpdatedCount.ToString());
        table.AddRow("Suppressed", import.DuplicateCount.ToString());
        table.AddRow("Closed", import.ClosedCount.ToString());
        table.AddRow("Skipped", import.SkippedCount.ToString());
        table.AddRow("New by severity", import.NewBySeverity ?? "none");
        table.AddRow("Policy", result.Policy);

        AnsiConsole.Write(table);

        // The verdict is printed after the table and in colour: a human reading a CI log scrolls to
        // the bottom, and the one line they need is this one.
        if (result.Failed) AnsiConsole.MarkupLine($"[red]GATE FAILED:[/] {result.Message}");
        else AnsiConsole.MarkupLine($"[green]GATE PASSED:[/] {result.Message}");
    }

    private static void PrintJson(DAL.Entities.ScanImport import, GateResult result)
    {
        var payload = new
        {
            importId = import.Id,
            importer = import.Importer,
            status = ((ScanImportStatus)import.Status).ToString(),
            counts = new
            {
                @new = import.NewCount,
                updated = import.UpdatedCount,
                suppressed = import.DuplicateCount,
                closed = import.ClosedCount,
                skipped = import.SkippedCount
            },
            newBySeverity = import.NewBySeverity,
            gate = new
            {
                policy = result.Policy,
                failed = result.Failed,
                actual = result.Actual,
                threshold = result.Threshold,
                message = result.Message
            }
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}

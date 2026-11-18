using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Entities;
using DAL.Enums;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>The operational cells of one source line (S40 §6.5): text only, "—" where there is nothing to say.</summary>
public sealed record RiskSourceStatusCells(string Connections, string LastSync, string Status, string Objects, string Stale,
    string CountsHint);

/// <summary>
/// How the Sources tab writes <c>GET /RiskIndex/Sources</c> and <c>/Runs</c> (S40 §6.5; T263). The server decides what a
/// reader may see; this only words it: withheld counts read "—" with "Requer escopo global", never 0.
/// </summary>
public static class RiskSourceStatusFormatter
{
    public const string Dash = "—";
    public const string RequiresGlobalScope = "RiskSourcesRequiresGlobalScope";

    public static RiskSourceStatusCells Cells(RiskIndexSourceDto? source, TextLookup text, CultureInfo culture)
    {
        if (source is null) return new RiskSourceStatusCells(Dash, Dash, Dash, Dash, Dash, "");

        var moment = source.LastSyncUtc is { } at ? at.ToLocalTime().ToString("g", culture) : null;
        var lastSync = moment is null ? Dash
            : source.LastSyncKindKey == RiskIndexSourceStatusKeys.KindImport
                ? string.Format(culture, text("RiskSourcesImportFMT") ?? "{0}", moment)
                : moment;
        var status = source.LastSyncStatusKey is { } key ? text("RiskSourceStatus_" + key) ?? key : Dash;
        var withheld = source.CountsWithheld;
        return new RiskSourceStatusCells(
            source.Connections?.ToString(culture) ?? Dash,
            lastSync,
            status,
            withheld ? Dash : source.ObjectCount?.ToString("N0", culture) ?? Dash,
            withheld ? Dash : source.StaleFraction is { } f ? f.ToString("P0", culture) : Dash,
            withheld ? text(RequiresGlobalScope) ?? RequiresGlobalScope : "");
    }

    /// <summary>One line of the "Execuções do índice" block: what ran, when, how it ended and its summary.</summary>
    public static string RunLine(IntegrationSyncLog run, TextLookup text, CultureInfo culture)
    {
        var started = run.StartedAt.ToLocalTime().ToString("g", culture);
        var finished = run.FinishedAt is { } f ? f.ToLocalTime().ToString("t", culture) : Dash;
        var status = text("RiskSourceStatus_" + StatusKey(run.Status)) ?? StatusKey(run.Status);
        var parts = new List<string> { $"{run.ConnectionName} · {started}–{finished} · {status}" };
        if (!string.IsNullOrWhiteSpace(run.Summary)) parts.Add(run.Summary!);
        if (!string.IsNullOrWhiteSpace(run.ErrorMessage)) parts.Add(run.ErrorMessage!);
        return string.Join(" · ", parts);
    }

    public static string StatusKey(IntegrationSyncStatus status) => status switch
    {
        IntegrationSyncStatus.Succeeded => RiskIndexSourceStatusKeys.Ok,
        IntegrationSyncStatus.PartiallySucceeded => RiskIndexSourceStatusKeys.Partial,
        IntegrationSyncStatus.Failed => RiskIndexSourceStatusKeys.Failed,
        _ => RiskIndexSourceStatusKeys.Running
    };

    /// <summary>The source lines' status, by id; a source the server did not list reads as unknown.</summary>
    public static IReadOnlyDictionary<string, RiskIndexSourceDto> ById(IEnumerable<RiskIndexSourceDto> sources) =>
        sources.GroupBy(s => s.SourceId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
}

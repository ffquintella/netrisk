using System.Diagnostics.CodeAnalysis;
using ConsoleClient.Commands.Settings;
using ServerServices.Interfaces;
using Spectre.Console;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

/// <summary>
/// Maintenance on stored secret-vault references.
///
/// One operation so far: <c>normalize-references</c>, which asks each vault plugin to re-read the
/// references stored against it and rewrites the ones it now expresses differently. That is how a
/// plugin retires a grammar it once had to encode inside a secret id — the host walks the columns,
/// the plugin does the reading, and nothing here knows what any of those ids mean.
///
/// Reports by default and writes only with <c>--apply</c>, because a rewrite a plugin got wrong
/// repoints a credential field at a different secret.
/// </summary>
public class VaultCommand : Command<VaultSettings>
{
    private readonly ISecretReferenceNormalizer _normalizer;

    public VaultCommand(ISecretReferenceNormalizer normalizer) => _normalizer = normalizer;

    protected override int Execute([NotNull] CommandContext context, [NotNull] VaultSettings settings,
        CancellationToken cancellationToken)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        switch (settings.Operation)
        {
            case "normalize-references":
                return ExecuteNormalize(settings, cancellationToken);
            default:
                AnsiConsole.MarkupLine("[red]*** Invalid operation selected ***[/]");
                AnsiConsole.MarkupLine("[white] valid options are: normalize-references [/]");
                return -1;
        }
    }

    private int ExecuteNormalize(VaultSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var rewrites = _normalizer.NormalizeAsync(settings.Apply, cancellationToken)
                .GetAwaiter().GetResult();

            if (rewrites.Count == 0)
            {
                AnsiConsole.MarkupLine("[green]Every stored vault reference is already in the form "
                                       + "its plugin produces. Nothing to do.[/]");
                return 0;
            }

            var table = new Table();
            table.AddColumn("Field");
            table.AddColumn("Stored");
            table.AddColumn("Becomes");

            foreach (var rewrite in rewrites)
                table.AddRow(rewrite.Location.EscapeMarkup(), rewrite.Before.EscapeMarkup(),
                    rewrite.After.EscapeMarkup());

            AnsiConsole.Write(table);

            if (settings.Apply)
            {
                AnsiConsole.MarkupLine($"[green]{rewrites.Count} reference(s) rewritten.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[yellow]{rewrites.Count} reference(s) would be rewritten. "
                                   + "Nothing was written.[/]");
            AnsiConsole.MarkupLine("[grey]Re-run with --apply once the table above looks right.[/]");
            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{ex.Message.EscapeMarkup()}[/]");
            return -1;
        }
    }
}

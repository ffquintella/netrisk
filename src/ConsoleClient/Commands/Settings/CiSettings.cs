using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

/// <summary>
/// Arguments for <c>netrisk-console ci gate</c> (Track 3 milestone 3.5.4).
/// </summary>
public class CiSettings : CommandSettings
{
    [Description("Operation to run. Currently only 'gate'.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";

    [Description("The import id to evaluate, as returned by the import endpoint (scan_imports.id).")]
    [CommandOption("--job|--import")]
    public int? ImportId { get; set; }

    [Description(
        "Policy expression. Examples: 'new-critical' (fail on any new critical), 'any-high>5' " +
        "(fail on more than five new highs or worse), 'sla-breach' (fail if a new finding is " +
        "already past its SLA), 'none' (never fail).")]
    [CommandOption("--fail-on")]
    public string FailOn { get; set; } = "new-critical";

    [Description("Print the decision as JSON instead of a table, for a pipeline that parses it.")]
    [CommandOption("--json")]
    public bool Json { get; set; }
}

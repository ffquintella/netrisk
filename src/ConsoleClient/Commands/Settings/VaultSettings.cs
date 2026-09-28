using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class VaultSettings : CommandSettings
{
    [Description("Operation to execute. Valid values are: normalize-references.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";

    [Description("Write the rewrites. Without it the command only reports what it would change.")]
    [CommandOption("--apply")]
    public bool Apply { get; set; }
}

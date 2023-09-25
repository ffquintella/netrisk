using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class UserSettings: CommandSettings
{
    [Description("Manage users on netrisk server. Valid values are: add, remove, list.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";

    [CommandOption("--ignore-password-complexity")]
    public bool? IgnorePwdComplexity { get; set; } = false;
}
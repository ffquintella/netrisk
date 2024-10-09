using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class SettingsSettings: CommandSettings
{
    [Description("Manage netrisk server general settings. Valid values are: list.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";
    
    [CommandArgument(1, "[parameter1]")]
    public string? Parameter1 { get; set; }
    
}
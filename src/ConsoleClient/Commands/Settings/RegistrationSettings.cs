using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class RegistrationSettings: CommandSettings
{
    [Description("One of the operations to execute. Valid values are: list, approve, reject, delete.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";
    
    [CommandOption("-i|--id")]
    public int? Id { get; set; }
    
    [CommandOption("--all")]
    public bool? All { get; set; } 
}
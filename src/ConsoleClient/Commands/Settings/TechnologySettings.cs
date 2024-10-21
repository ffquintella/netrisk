using System.ComponentModel;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands.Settings;

public class TechnologySettings: CommandSettings
{
    [Description("Manage the technologies avaliable on the program.")]
    
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";
}
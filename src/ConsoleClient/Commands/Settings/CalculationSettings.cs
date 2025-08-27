using Spectre.Console.Cli;
using System.ComponentModel;

namespace ConsoleClient.Commands.Settings;

public class CalculationSettings: CommandSettings
{
    [Description("One of the operations to execute. Valid values are: riskScore and contributingImpact.")]
    [CommandArgument(0, "<operation>")]
    public string Operation { get; set; } = "";
}
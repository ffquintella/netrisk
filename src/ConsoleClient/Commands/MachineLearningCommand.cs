using System.Diagnostics.CodeAnalysis;
using ConsoleClient.Commands.Settings;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class MachineLearningCommand: Command<MachineLearningSettings>
{
    
    public MachineLearningCommand()
    {
        
    }
    
    public override int Execute( CommandContext context,  MachineLearningSettings settings)
    {
        return 0;
    }
}
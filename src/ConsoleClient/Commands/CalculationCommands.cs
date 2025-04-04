using System.Diagnostics.CodeAnalysis;
using ConsoleClient.Commands.Settings;
using ServerServices.Interfaces;
using Spectre.Console.Cli;

namespace ConsoleClient.Commands;

public class CalculationCommands: Command<CalculationSettings>
{
    private IRiskCalculationService RiskCalculationService { get; }
    
    public CalculationCommands(IRiskCalculationService riskCalculationService)
    {
        RiskCalculationService = riskCalculationService;
        
        RiskCalculationService.RiskScoreCalculated += (sender, args) =>
        {
            Console.WriteLine($"Risk score calculated ID: {args.RiskScoring.Id} score: {args.RiskScoring.CalculatedRisk}");

        };
    }
    

    public override int Execute(CommandContext context, CalculationSettings settings)
    {
        switch(settings)
        {
            case { Operation: "riskScore" }:
                _= RiskCalculationService.CalculateRiskScoreAsync();
                return 0;
            default:
                Console.WriteLine("Invalid operation. Valid values are: riskScore.");
                return -1;
        }
        
    }
}
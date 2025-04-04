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
        
        RiskCalculationService.RiskContributingImpactCalculated += (sender, args) =>
        {
            Console.WriteLine($"Risk contributing score calculated ID: {args.RiskScoring.Id} value: {args.RiskScoring.ContributingScore}");

        };
    }
    

    public override int Execute(CommandContext context, CalculationSettings settings)
    {
        switch(settings)
        {
            case { Operation: "riskScore" }:
                _= RiskCalculationService.CalculateRiskScoreAsync();
                return 0;
            case { Operation: "contributingImpact" }:
                _= RiskCalculationService.CalculateContributingImpactAsync();
                return 0;
            default:
                Console.WriteLine("Invalid operation. Valid values are: riskScore or contributingImpact.");
                return -1;
        }
        
    }
}
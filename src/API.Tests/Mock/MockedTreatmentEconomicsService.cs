using System;
using System.Collections.Generic;
using DAL.Enums;
using Model.Exceptions;
using Model.TreatmentEconomics;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="ITreatmentEconomicsService"/> for <c>TreatmentEconomicsControllerTest</c> (Stage 9.6). Its
/// ids drive every branch of the controller's error mapping without a per-test double: 400 an invalid parameter, 404
/// missing (or out of scope), 422 a broken rule (Gate A or a dependency cycle), 500 anything else.
/// </summary>
public static class MockedTreatmentEconomicsService
{
    public const int Known = 10;
    public const int Invalid = 400;
    public const int Missing = 404;
    public const int GateA = 422;
    public const int Cycle = 423;
    public const int Broken = 500;

    private static readonly DateTime When = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static void Gate(int id)
    {
        switch (id)
        {
            case Invalid: throw new InvalidParameterException("Option", "The treatment option is required.");
            case Missing: throw new DataNotFoundException("mitigations", id.ToString());
            case GateA: throw new RuleBrokenException("This risk cannot be accepted: Gate A.", "gate_a_non_discretionary");
            case Cycle: throw new RuleBrokenException("A circular plan cannot be executed.", "dependency_cycle");
            case Broken: throw new InvalidOperationException("boom");
        }
    }

    public static MitigationEconomicsDto Economics(int id) => new()
    {
        MitigationId = id, RiskId = 1, Declared = true, Option = TreatmentOption.Reduce,
        Cost = new TreatmentCostDto { Annual = 15_000, AnnualizedTotal = 15_000, FirstYear = 15_000 },
        GateC = new GateCResultDto { Outcome = GateCOutcome.Passes, Benefit = 60_000, AnnualizedCost = 15_000 }
    };

    public static RiskTargetDto Target(int riskId) => new()
    {
        RiskId = riskId, TargetScore = 3, Rationale = "MFA.", CreatedAt = When,
        Status = new RiskTargetStatusDto { ScoreMet = false, WithinAppetite = true }
    };

    public static ITreatmentEconomicsService Create()
    {
        var service = Substitute.For<ITreatmentEconomicsService>();

        service.GetMitigationAsync(Arg.Any<int>()).Returns(call => { Gate(call.Arg<int>()); return Economics(call.Arg<int>()); });
        service.SaveMitigationAsync(Arg.Any<int>(), Arg.Any<MitigationEconomicsRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Economics(call.ArgAt<int>(0)); });

        service.GetRiskAsync(Arg.Any<int>()).Returns(call =>
        {
            Gate(call.Arg<int>());
            return new RiskTreatmentEconomicsDto
            {
                RiskId = call.Arg<int>(), Subject = "Ransomware", Systemic = true, Target = Target(call.Arg<int>()),
                Mitigations = [Economics(Known)]
            };
        });

        service.SaveTargetAsync(Arg.Any<int>(), Arg.Any<RiskTargetRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Target(call.ArgAt<int>(0)); });
        service.DeleteTargetAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call => { Gate(call.ArgAt<int>(0)); return System.Threading.Tasks.Task.CompletedTask; });

        service.SelectPortfolioAsync(Arg.Any<PortfolioSelectionRequest>()).Returns(call =>
        {
            var request = call.Arg<PortfolioSelectionRequest>();
            if (request.Budget is null) throw new InvalidParameterException("Budget", "Gate D needs a budget.");
            return new PortfolioSelectionDto
            {
                GeneratedAt = When, Budget = request.Budget.Value, Considered = 1, Selected = 1,
                Items = [new PortfolioItemDto { MitigationId = Known, Tier = PortfolioTier.Protected, Status = PortfolioItemStatus.Selected }]
            };
        });

        return service;
    }
}

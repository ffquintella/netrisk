using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.Exceptions;
using Model.TailRisk;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="ITailRiskService"/> for <c>TailRiskControllerTest</c> (Stage 9.7). Its ids drive every
/// branch of the controller's error mapping without a per-test double: 400 an invalid parameter, 404 missing (or out of
/// scope), 422 a broken rule (a matrix that is not positive semidefinite, a group too large), 500 anything else. For a
/// correlation the id is the one of <see cref="RiskCorrelationRequest.RiskAId"/>.
/// </summary>
public static class MockedTailRiskService
{
    public const int Known = 10;
    public const int Invalid = 400;
    public const int Missing = 404;
    public const int NotPsd = 422;
    public const int TooLarge = 423;
    public const int Broken = 500;

    private static readonly DateTime When = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static void Gate(int id)
    {
        switch (id)
        {
            case Invalid: throw new InvalidParameterException("Components", "At least one loss component is required.");
            case Missing: throw new DataNotFoundException("risks", id.ToString());
            case NotPsd:
                throw new RuleBrokenException("The correlation matrix is not positive semidefinite.",
                    "correlation_not_positive_semidefinite");
            case TooLarge:
                throw new RuleBrokenException("The correlated group is too large.", "correlation_group_too_large");
            case Broken: throw new InvalidOperationException("boom");
        }
    }

    public static RiskTailDto Tail(int riskId) => new()
    {
        RiskId = riskId,
        Recomputed = true,
        Inherent = new TailStatisticsDto
        {
            Run = TailRun.Inherent, Iterations = 10_000, Seed = 1, ConfidenceLevel = 0.95m, ExpectedLoss = 1_000,
            P95 = 5_000, Cvar95 = 9_000, MagnitudeSource = MagnitudeSource.Components, ComputedAt = When
        },
        DeclaredComponents =
        [
            new LossComponentDto { Component = LossComponent.Response, Min = 1, MostLikely = 2, Max = 3, CreatedAt = When }
        ],
        Appetite = new TailAppetiteEvaluation { State = TailAppetiteState.WithinTolerance, Explanation = "Within tolerance." },
        TailFlag = new TailFlagCriterionDto { Holds = true, Declared = false, Derived = true }
    };

    public static RiskCorrelationDto Correlation(int a, int b, decimal coefficient = 0.4m) => new()
    {
        Id = 5, RiskAId = Math.Min(a, b), RiskBId = Math.Max(a, b), Coefficient = coefficient,
        Rationale = "Same supplier.", CreatedAt = When
    };

    public static RiskAppetiteTailLimitsDto Limits(int appetiteId) => new()
    {
        AppetiteId = appetiteId, MaxScenarioP95 = 250_000m, MaxPortfolioCvar95 = 2_000_000m,
        Rationale = "Phase 0 decision.", CreatedAt = When
    };

    public static ITailRiskService Create()
    {
        var service = Substitute.For<ITailRiskService>();

        service.GetRiskAsync(Arg.Any<int>()).Returns(call => { Gate(call.Arg<int>()); return Tail(call.Arg<int>()); });
        service.SaveLossComponentsAsync(Arg.Any<int>(), Arg.Any<LossComponentsRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Tail(call.ArgAt<int>(0)); });
        service.DeleteLossComponentsAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Tail(call.ArgAt<int>(0)); });

        service.GetCorrelationsAsync(Arg.Any<int?>()).Returns(call =>
        {
            var riskId = call.Arg<int?>();
            if (riskId is { } id) Gate(id);
            return new List<RiskCorrelationDto> { Correlation(riskId ?? Known, 11) };
        });
        service.SaveCorrelationAsync(Arg.Any<RiskCorrelationRequest>(), Arg.Any<int>()).Returns(call =>
        {
            var request = call.Arg<RiskCorrelationRequest>();
            if (request.RiskAId is null || request.RiskBId is null)
                throw new InvalidParameterException("RiskAId", "Both risks of the pair are required.");
            Gate(request.RiskAId.Value);
            return Correlation(request.RiskAId.Value, request.RiskBId.Value, request.Coefficient ?? 0m);
        });
        service.DeleteCorrelationAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Task.CompletedTask; });

        service.AggregatePortfolioAsync(Arg.Any<PortfolioTailRequest>()).Returns(call =>
        {
            var request = call.Arg<PortfolioTailRequest>();
            if (request.Basis is null) throw new InvalidParameterException("Basis", "The portfolio basis is required.");
            return new PortfolioTailDto
            {
                GeneratedAt = When, Basis = request.Basis.Value, Seed = request.Seed ?? TailRiskLimits.DefaultPortfolioSeed,
                Iterations = 10_000, ConfidenceLevel = 0.95m, Dependence = PortfolioDependence.Declared,
                ExpectedLoss = 3_000, Cvar95 = 20_000, SumOfCvar95 = 25_000, Diversification = 5_000,
                Members = [new PortfolioMemberDto { RiskId = Known, Subject = "Ransomware", ExpectedLoss = 3_000 }]
            };
        });

        service.GetAppetiteLimitsAsync(Arg.Any<int>()).Returns(call => { Gate(call.Arg<int>()); return Limits(call.Arg<int>()); });
        service.SaveAppetiteLimitsAsync(Arg.Any<int>(), Arg.Any<RiskAppetiteTailLimitsRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Limits(call.ArgAt<int>(0)); });
        service.DeleteAppetiteLimitsAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Task.CompletedTask; });

        return service;
    }
}

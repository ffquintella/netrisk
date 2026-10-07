using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Enums;
using Model.Exceptions;
using Model.RiskFlags;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="IRiskFlagsService"/> for <c>RiskFlagsControllerTest</c> (Stage 9.5). Its risk ids
/// drive every branch of the controller's error mapping without a per-test double: 400 is an invalid parameter,
/// 404 missing (or out of scope), 409 an invalid transition (answered 422), 422 a broken rule (Gate A or
/// segregation of duties), 500 anything else.
/// </summary>
public static class MockedRiskFlagsService
{
    public const int Known = 10;
    public const int Invalid = 400;
    public const int Missing = 404;
    public const int Transition = 409;
    public const int GateA = 422;
    public const int Broken = 500;

    private static readonly DateTime When = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static void Gate(int id)
    {
        switch (id)
        {
            case Invalid: throw new InvalidParameterException("Reason", "A declared flag needs a written reason.");
            case Missing: throw new DataNotFoundException("risks", id.ToString());
            case Transition: throw new InvalidStateTransitionException("Declared", "Declared", "Already declared.");
            case GateA: throw new RuleBrokenException("This risk carries a Gate A condition.", "gate_a_non_discretionary");
            case Broken: throw new InvalidOperationException("boom");
        }
    }

    public static RiskFlagsStateDto State(int riskId) => new()
    {
        RiskId = riskId,
        Flags = [new RiskFlagStateDto { Code = RiskFlagCode.HumanSafety, Number = 1, Name = "Life", IsSet = true, Declared = true }],
        GateA = new GateAEvaluationDto { Holds = true, Conditions = [RiskFlagCode.HumanSafety] }
    };

    public static RiskDecisionDto Decision(int riskId) => new()
    {
        Id = 3, RiskId = riskId, Decision = RiskDecisionKind.ActImmediately, Source = RiskDecisionSource.Declared,
        Reason = "Now.", DecidedAt = When, DecidedById = 1, EscalatedAt = When
    };

    public static IRiskFlagsService Create()
    {
        var service = Substitute.For<IRiskFlagsService>();

        service.GetCatalogue().Returns(RiskFlagCatalogue.All);

        service.GetAsync(Arg.Any<int>()).Returns(call => { Gate(call.Arg<int>()); return State(call.Arg<int>()); });
        service.RefreshAsync(Arg.Any<int>()).Returns(call => { Gate(call.Arg<int>()); return State(call.Arg<int>()); });

        service.DeclareAsync(Arg.Any<int>(), Arg.Any<RiskFlagCode>(), Arg.Any<RiskFlagDeclarationRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return State(call.ArgAt<int>(0)); });
        service.WithdrawAsync(Arg.Any<int>(), Arg.Any<RiskFlagCode>(), Arg.Any<RiskFlagWithdrawalRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return State(call.ArgAt<int>(0)); });

        service.GetDecisionsAsync(Arg.Any<int>())
            .Returns(call => { Gate(call.Arg<int>()); return new List<RiskDecisionDto> { Decision(call.Arg<int>()) }; });
        service.RecordDecisionAsync(Arg.Any<int>(), Arg.Any<RiskDecisionRequest>(), Arg.Any<int>())
            .Returns(call => { Gate(call.ArgAt<int>(0)); return Decision(call.ArgAt<int>(0)); });

        service.GetFlaggedAsync(Arg.Any<RiskFlagCode?>(), Arg.Any<bool?>()).Returns(call =>
        {
            if (call.ArgAt<RiskFlagCode?>(0) is { } code && !RiskFlagCatalogue.IsDefined(code))
                throw new InvalidParameterException("flag", "The flag is 1 to 11, or 12.");
            return new List<FlaggedRiskDto>
            {
                new() { RiskId = Known, Subject = "Pump", Flags = [RiskFlagCode.HumanSafety], GateA = true }
            };
        });

        service.GetTopRisksAsync(Arg.Any<int>()).Returns(call =>
        {
            var limit = call.Arg<int>();
            if (limit is < 1 or > 50) throw new InvalidParameterException("limit", "1 to 50.");
            return new TopRisksDto
            {
                GeneratedAt = When, Limit = limit, OpenRisks = 1,
                Items = [new TopRiskDto { Rank = 1, RiskId = Known, GateA = true }]
            };
        });

        return service;
    }
}

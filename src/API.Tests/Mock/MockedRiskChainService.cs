using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using DAL.Enums;
using Model.Exceptions;
using Model.Risks.Chain;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// A deterministic <see cref="IRiskChainService"/> for <c>RiskChainControllerTest</c> (Stage 9.1).
///
/// Its ids are chosen to drive every branch of the controller's error mapping without a per-test
/// double: risk <see cref="KnownRisk"/> exists and 404 does not; entity <see cref="NewTarget"/> links,
/// <see cref="LegacyTarget"/> promotes, 409 conflicts and 422 is not a chain node; host 403 lacks the
/// <c>hosts</c> permission; link <see cref="DeletableLink"/> deletes, <see cref="DemotableLink"/>
/// demotes and <see cref="LegacyLink"/> is refused.
/// </summary>
public static class MockedRiskChainService
{
    public const int KnownRisk = 1;
    public const int NewTarget = 10;
    public const int LegacyTarget = 11;
    public const int DeletableLink = 5;
    public const int DemotableLink = 6;
    public const int LegacyLink = 7;
    public const int Forbidden = 403;
    public const int Missing = 404;
    public const int Conflicting = 409;
    public const int OutsideChain = 422;

    public static RiskChainLinkDto Link(int id, RiskChainLinkOrigin origin = RiskChainLinkOrigin.Declared) => new()
    {
        Id = id, RiskId = KnownRisk, Level = RiskChainLevel.Process, EntityId = NewTarget,
        TargetName = "Enrolment", TargetType = "businessProcess", Origin = origin,
        CreatedAt = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc)
    };

    public static IRiskChainService Create()
    {
        var service = Substitute.For<IRiskChainService>();

        service.GetRiskChainAsync(Arg.Any<int>(), Arg.Any<ClaimsPrincipal?>()).Returns(call =>
        {
            var riskId = call.ArgAt<int>(0);
            if (riskId != KnownRisk) throw new DataNotFoundException("Risk", riskId.ToString());

            var chain = new RiskChainDto { RiskId = KnownRisk, ScopeEntityId = 100, ScopeEntityName = "Unit A" };
            foreach (var level in Enum.GetValues<RiskChainLevel>())
            {
                chain.Levels.Add(new RiskChainLevelDto
                {
                    Level = level,
                    Links = level == RiskChainLevel.Process ? [Link(1)] : []
                });
                if (level != RiskChainLevel.Process) chain.MissingLevels.Add(level);
            }

            return Task.FromResult(chain);
        });

        service.AddLinkAsync(Arg.Any<int>(), Arg.Any<RiskChainLinkCreateDto>(), Arg.Any<int?>(),
            Arg.Any<ClaimsPrincipal?>()).Returns(call =>
        {
            var riskId = call.ArgAt<int>(0);
            var request = call.ArgAt<RiskChainLinkCreateDto>(1);

            if ((request.EntityId is null) == (request.HostId is null))
                throw new InvalidParameterException("target", "Give either entityId or hostId.");
            if (request.HostId == Forbidden)
                throw new PermissionInvalidException("hosts", 1, "RiskChain.AddLink");
            if (riskId == Missing || request.EntityId == Missing)
                throw new DataNotFoundException("Risk", riskId.ToString());
            if (request.EntityId == Conflicting)
                throw new DataAlreadyExistsException("netrisk", "risk_chain_links", "1:409", "Already linked.");
            if (request.EntityId == OutsideChain)
                throw new RuleBrokenException("A unit is not a chain node.", "entity_not_in_chain");

            return Task.FromResult(request.EntityId == LegacyTarget
                ? new RiskChainLinkWriteResult(Link(2), Created: false)
                : new RiskChainLinkWriteResult(Link(3), Created: true));
        });

        service.DeleteLinkAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<ClaimsPrincipal?>()).Returns(call =>
        {
            var linkId = call.ArgAt<int>(1);

            return linkId switch
            {
                DeletableLink => Task.FromResult(new RiskChainLinkDeleteResult(null)),
                DemotableLink => Task.FromResult(new RiskChainLinkDeleteResult(Link(DemotableLink,
                    RiskChainLinkOrigin.Legacy))),
                LegacyLink => throw new RuleBrokenException("Remove it from the Entity field.", "legacy_link"),
                Forbidden => throw new PermissionInvalidException("hosts", 1, "RiskChain.DeleteLink"),
                _ => throw new DataNotFoundException("RiskChainLink", linkId.ToString())
            };
        });

        service.GetRisksByEntityAsync(Arg.Any<int>(), Arg.Any<bool>()).Returns(call =>
        {
            var entityId = call.ArgAt<int>(0);
            var inferred = call.ArgAt<bool>(1);

            if (entityId == Missing) throw new DataNotFoundException("Entity", entityId.ToString());
            if (entityId == OutsideChain) throw new RuleBrokenException("Not a chain node.", "entity_not_in_chain");

            var matches = new List<RiskChainMatchDto>
            {
                new() { RiskId = KnownRisk, Subject = "Risk 1", Status = "New" }
            };
            if (inferred)
                matches.Add(new RiskChainMatchDto
                {
                    RiskId = 2, Subject = "Risk 2", Status = "Closed", Inferred = true,
                    ViaLevel = RiskChainLevel.ItService, ViaEntityId = 20, ViaEntityName = "Student portal"
                });

            return Task.FromResult(matches);
        });

        service.GetRisksByHostAsync(Arg.Any<int>(), Arg.Any<ClaimsPrincipal?>()).Returns(call =>
        {
            var hostId = call.ArgAt<int>(0);

            if (hostId == Forbidden) throw new PermissionInvalidException("hosts", 1, "RiskChain.GetRisksByHost");
            if (hostId == Missing) throw new DataNotFoundException("Host", hostId.ToString());

            return Task.FromResult(new List<RiskChainMatchDto>
            {
                new() { RiskId = KnownRisk, Subject = "Risk 1", Status = "New" }
            });
        });

        service.GetCriticalProcessCoverageAsync().Returns(Task.FromResult(new CriticalProcessCoverageDto
        {
            ComputedAt = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
            Threshold = 4,
            CriticalProcessCount = 3,
            CoveredCount = 1,
            CoverageRatio = 1m / 3m,
            ProcessesWithoutCriticality = 2,
            IsScopeRestricted = true,
            Rows =
            [
                new CriticalProcessCoverageRowDto
                {
                    ProcessId = 10, ProcessName = "Enrolment", Criticality = 5, DirectOpenRiskCount = 1,
                    InferredOpenRiskCount = 2, Covered = true
                }
            ]
        }));

        return service;
    }
}

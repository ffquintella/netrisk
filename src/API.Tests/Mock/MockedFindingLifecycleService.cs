using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// Finding lifecycle and risk acceptance (Track 3 milestone 3.2).
///
/// Deterministic by id: 1 works, 2 is a finding whose transition the state machine refuses, and
/// anything else is unknown. That gives a controller test one id per response code without any
/// per-test wiring.
/// </summary>
public static class MockedFindingLifecycleService
{
    public const int RefusedFindingId = 2;

    public static IFindingLifecycleService Create()
    {
        var service = Substitute.For<IFindingLifecycleService>();

        service.TransitionAsync(Arg.Any<int>(), Arg.Any<FindingStatus>(), Arg.Any<int?>(),
                Arg.Any<FindingStatusChangeSource>(), Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var id = call.ArgAt<int>(0);
                var to = call.ArgAt<FindingStatus>(1);

                if (id == RefusedFindingId)
                    throw new InvalidStateTransitionException("FalsePositive", to.ToString(),
                        "A finding cannot move from FalsePositive to Mitigated.");

                if (id != 1)
                    throw new DataNotFoundException("vulnerabilities", id.ToString(),
                        new Exception("Finding not found"));

                return Task.FromResult(new Vulnerability
                {
                    Id = id, Title = "Mocked finding", LifecycleStatus = to
                });
            });

        service.GetHistoryAsync(Arg.Any<int>()).Returns(Task.FromResult(new List<FindingStatusHistory>
        {
            new()
            {
                Id = 2, VulnerabilityId = 1, FromStatus = FindingStatus.Active,
                ToStatus = FindingStatus.Verified, UserId = 1,
                ChangedAt = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc),
                Source = FindingStatusChangeSource.Manual
            },
            new()
            {
                Id = 1, VulnerabilityId = 1, FromStatus = null, ToStatus = FindingStatus.Active,
                ChangedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                Source = FindingStatusChangeSource.Import
            }
        }));

        service.GetAllowedTransitionsAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != 1)
                throw new DataNotFoundException("vulnerabilities", id.ToString(),
                    new Exception("Finding not found"));

            return Task.FromResult(new List<FindingStatus>
            {
                FindingStatus.Verified, FindingStatus.FalsePositive, FindingStatus.Mitigated
            });
        });

        service.GetAcceptancesAsync(Arg.Any<int?>()).Returns(call =>
        {
            var within = call.ArgAt<int?>(0);

            // The expiring-within filter narrows to the one acceptance inside the window.
            return Task.FromResult(within == null
                ? new List<RiskAcceptance> { Acceptance(1, 10), Acceptance(2, 200) }
                : new List<RiskAcceptance> { Acceptance(1, 10) });
        });

        service.GetAcceptanceAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != 1)
                throw new DataNotFoundException("risk_acceptances", id.ToString(),
                    new Exception("Risk acceptance not found"));

            return Task.FromResult(Acceptance(1, 10));
        });

        service.CreateAcceptanceAsync(Arg.Any<RiskAcceptance>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var acceptance = call.ArgAt<RiskAcceptance>(0);

                if (string.IsNullOrWhiteSpace(acceptance.Name))
                    throw new InvalidParameterException(nameof(acceptance.Name),
                        "A risk acceptance requires a name.");

                acceptance.Id = 1;
                acceptance.Status = RiskAcceptanceStatus.Active;
                return Task.FromResult(acceptance);
            });

        service.UpdateAcceptanceAsync(Arg.Any<RiskAcceptance>(), Arg.Any<int?>())
            .Returns(call => Task.FromResult(call.ArgAt<RiskAcceptance>(0)));

        service.AddFindingsToAcceptanceAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var id = call.ArgAt<int>(0);
                if (id != 1)
                    throw new DataNotFoundException("risk_acceptances", id.ToString(),
                        new Exception("Risk acceptance not found"));

                return Task.FromResult(Acceptance(1, 10));
            });

        service.RevokeAcceptanceAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var id = call.ArgAt<int>(0);
                var reason = call.ArgAt<string>(1);

                if (string.IsNullOrWhiteSpace(reason))
                    throw new InvalidParameterException("reason",
                        "Revoking a risk acceptance requires a stated reason.");

                if (id != 1)
                    throw new DataNotFoundException("risk_acceptances", id.ToString(),
                        new Exception("Risk acceptance not found"));

                var acceptance = Acceptance(1, 10);
                acceptance.Status = RiskAcceptanceStatus.Revoked;
                acceptance.RevocationReason = reason;
                return Task.FromResult(acceptance);
            });

        return service;
    }

    private static RiskAcceptance Acceptance(int id, int daysUntilExpiry) => new()
    {
        Id = id,
        Name = $"Acceptance {id}",
        BusinessJustification = "Mocked justification",
        AuthorizingManagerId = 1,
        ExpiresAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(daysUntilExpiry),
        Status = RiskAcceptanceStatus.Active,
        CreatedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
    };
}

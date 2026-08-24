using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Interfaces;
using Sieve.Models;

namespace API.Tests.Mock;

/// <summary>
/// The vulnerability register. Finding 1 exists; anything else is unknown, so a controller test gets
/// its 200 and its 404 without per-test wiring.
/// </summary>
public static class MockedVulnerabilitiesService
{
    public static IVulnerabilitiesService Create()
    {
        var service = Substitute.For<IVulnerabilitiesService>();

        service.GetAll().Returns([Finding(1), Finding(2)]);

        service.GetByIdAsync(Arg.Any<int>(), Arg.Any<bool>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != 1)
                throw new DataNotFoundException("vulnerabilities", id.ToString(),
                    new Exception("Vulnerability not found"));

            return Task.FromResult(Finding(1));
        });

        service.GetLastScanDateAsync()
            .Returns(Task.FromResult<DateTime?>(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));

        return service;
    }

    private static Vulnerability Finding(int id) => new()
    {
        Id = id,
        Title = $"Mocked finding {id}",
        Severity = "3",
        FirstDetection = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        LastDetection = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        DetectionCount = 1,
        Status = 1,
        LifecycleStatus = FindingStatus.Active
    };
}

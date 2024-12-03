using System.Collections.Generic;
using DAL.Entities;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedRisksService
{
    public static IRisksService Create()
    {
        var risksService = Substitute.For<IRisksService>();

        /*risksService.GetRiskAsync("testRisk").Returns(new Risk()
        {
            Name = "testRisk",
            Description = "testRisk"
        });*/

        risksService.GetVulnerabilitiesAsync(1, false).Returns(new List<Vulnerability>()
        {
            new ()
            {
                Id = 1,
                AnalystId = 1,
                Severity = "1",
                Score = 5
            },
            new ()
            {
                Id = 2,
                AnalystId = 1,
                Severity = "1",
                Score = 5
            }
        });

        return risksService;
    }
}
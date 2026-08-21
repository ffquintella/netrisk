using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Model.Dashboard;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedMasterDashboardService
{
    public static IMasterDashboardService Create()
    {
        var service = Substitute.For<IMasterDashboardService>();

        service.GetMasterDashboardAsync(Arg.Any<bool>()).Returns(callInfo =>
        {
            var useCache = callInfo.Arg<bool>();

            return Task.FromResult(new MasterDashboard
            {
                GeneratedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                // A cache-bypassing call is by definition never served from cache.
                FromCache = useCache,
                Entities = new List<EntityPostureSummary>
                {
                    new() { EntityId = 1, EntityName = "Alpha", OpenRisks = 2, OpenVulnerabilities = 1 },
                    new() { EntityId = 2, EntityName = "Beta", OpenRisks = 1, OpenIncidents = 1 }
                },
                Totals = new EntityPostureSummary
                {
                    EntityName = "All entities", OpenRisks = 3, OpenVulnerabilities = 1, OpenIncidents = 1
                }
            });
        });

        return service;
    }
}

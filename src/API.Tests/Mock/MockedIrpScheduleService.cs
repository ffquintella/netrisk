using System;
using System.Collections.Generic;
using Model.IncidentResponsePlan;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedIrpScheduleService
{
    /// <summary>Plan 1 schedules; anything else is unknown and returns null (a 404 at the controller).</summary>
    public static IIrpScheduleService Create()
    {
        var service = Substitute.For<IIrpScheduleService>();

        service.GetScheduleAsync(Arg.Any<int>()).Returns(callInfo =>
        {
            var planId = callInfo.Arg<int>();
            if (planId != 1) return System.Threading.Tasks.Task.FromResult<IrpSchedule?>(null);

            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            return System.Threading.Tasks.Task.FromResult<IrpSchedule?>(new IrpSchedule
            {
                PlanId = 1,
                PlanName = "Mocked plan",
                PlanStart = start,
                PlanEnd = start.AddHours(3),
                TotalDuration = TimeSpan.FromHours(3),
                CriticalPath = new List<int> { 1, 2 },
                Items = new List<IrpScheduleItem>
                {
                    new()
                    {
                        TaskId = 1, Name = "Contain", ExecutionOrder = 1,
                        Duration = TimeSpan.FromHours(2), EarlyFinish = TimeSpan.FromHours(2),
                        IsCritical = true, StartDate = start, EndDate = start.AddHours(2)
                    },
                    new()
                    {
                        TaskId = 2, Name = "Eradicate", ExecutionOrder = 2,
                        DependsOn = new List<int> { 1 },
                        Duration = TimeSpan.FromHours(1),
                        EarlyStart = TimeSpan.FromHours(2), EarlyFinish = TimeSpan.FromHours(3),
                        IsCritical = true, StartDate = start.AddHours(2), EndDate = start.AddHours(3)
                    }
                }
            });
        });

        return service;
    }
}

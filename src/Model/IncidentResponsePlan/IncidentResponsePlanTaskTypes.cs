using Microsoft.Extensions.Localization;

namespace Model.IncidentResponsePlan;

public static class IncidentResponsePlanTaskTypes
{
    public static List<TaskType> GetTypes(IStringLocalizer localizer)
    {
        var list = new List<TaskType>()
        {
            new()
            {
                Name = localizer["Monitoring"],
                DbName = "monitoring"
            },
            new()
            {
                Name = localizer["Notification"],
                DbName = "notification"
            },
            new()
            {
                Name = localizer["Mitigation"],
                DbName = "mitigation"
            },
            new()
            {
                Name = localizer["Recovery"],
                DbName = "recovery"
            },
            new()
            {
                Name = localizer["Investigation"],
                DbName = "investigation"
            },
            new()
            {
                Name = localizer["Documentation"],
                DbName = "documentation"
            }
        };

        return list.OrderBy(l => l.Name).ToList();

    }
}
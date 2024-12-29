using Microsoft.Extensions.Localization;
using Model.Incidents;

namespace Model.Status;

public class IncidentStatus
{
    public static List<IntStatusItem> GetAll(IStringLocalizer localizer)
    {
        var list = new List<IntStatusItem>()
        {
            new()
            {
                Name = localizer["Active"],
                IntStatus = (int)IntStatus.Active
            },
            new()
            {
                Name = localizer["Under Investigation"],
                IntStatus = (int)IntStatus.UnderInvestigation
            },
            new()
            {
                Name = localizer["Closed"],
                IntStatus = (int)IntStatus.Closed
            },
            new()
            {
                Name = localizer["Solved"],
                IntStatus = (int)IntStatus.Solved
            },
            new()
            {
                Name = localizer["Not Relevant"],
                IntStatus = (int)IntStatus.NotRelevant
            },
            new()
            {
                Name = localizer["Awaiting Fix"],
                IntStatus = (int)IntStatus.AwaitingFix
            },
            new()
            {
                Name = localizer["Cancelled"],
                IntStatus = (int)IntStatus.Cancelled
            },
            
        };

        return list.OrderBy(l => l.Name).ToList();

    }
}
using Microsoft.Extensions.Localization;

namespace Model.Incidents;

public static class IncidentCategories
{
    public static List<IncidentCategory> GetCategories(IStringLocalizer localizer)
    {
        var list = new List<IncidentCategory>()
        {
            new()
            {
                Name = localizer["Malware infection"],
                DbName = "malware_infection"
            },
            new()
            {
                Name = localizer["Denial of Service"],
                DbName = "denial_of_service"
            },
            new()
            {
                Name = localizer["Data Breach"],
                DbName = "data_breach"
            },
            new()
            {
                Name = localizer["Insider threats"],
                DbName = "insider_threats"
            },
            new()
            {
                Name = localizer["Phishing"],
                DbName = "phishing"
            },
            new()
            {
                Name = localizer["Unauthorized Access"],
                DbName = "unauthorized_access"
            },
            new()
            {
                Name = localizer["Other - Social Engineering"],
                DbName = "other_social_engineering"
            },
            new()
            {
                Name = localizer["Other"],
                DbName = "other"
            },
            new()
            {
                Name = localizer["Not Specified"],
                DbName = "not_specified"
            }
        };

        return list.OrderBy(l => l.Name).ToList();

    }
}
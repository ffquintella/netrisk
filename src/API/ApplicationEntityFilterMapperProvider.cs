using DAL.Entities;
using Gridify;
using ServerServices.Filtering;
using ServerServices.Interfaces;
using Host = DAL.Entities.Host;

namespace API;

/// <summary>
/// The filterable/sortable surface of the list endpoints, replacing ApplicationEntityFilterMapperProvider.
/// External names are localized, so this is registered scoped and rebuilt per request — a
/// pt-BR caller filters on <c>título</c> where an en-US caller filters on <c>title</c>.
/// </summary>
public class ApplicationEntityFilterMapperProvider(ILocalizationService localization)
    : IEntityFilterMapperProvider
{
    public IGridifyMapper<T> For<T>()
    {
        var localizer = localization.GetLocalizer();

        if (typeof(T) == typeof(Vulnerability))
            return (IGridifyMapper<T>)VulnerabilityMapper(localizer);

        if (typeof(T) == typeof(Host))
            return (IGridifyMapper<T>)HostMapper(localizer);

        // An entity with no configured map has no filterable properties — the same position Sieve
        // took. An empty query still lists it (the export endpoint relies on that); naming a
        // property in a filter is rejected by the mapper, which is the point of the whitelist.
        return new GridifyMapper<T>();
    }

    private static IGridifyMapper<Vulnerability> VulnerabilityMapper(
        Microsoft.Extensions.Localization.IStringLocalizer localizer) =>
        new GridifyMapper<Vulnerability>()
            .AddMap(localizer["title"], v => v.Title)
            .AddMap("id", v => v.Id)
            .AddMap(localizer["Score"], v => v.Score)
            .AddMap(localizer["impact"], v => v.Severity)
            .AddMap(localizer["status"], v => v.Status)
            .AddMap(localizer["first_detection"], v => v.FirstDetection)
            .AddMap(localizer["last_detection"], v => v.LastDetection)
            .AddMap(localizer["detections"], v => v.DetectionCount)
            .AddMap(localizer["analyst"], v => v.AnalystId)
            .AddMap(localizer["host"], v => v.HostId)
            .AddMap(localizer["application"], v => v.EntityId)
            .AddMap(localizer["source"], v => v.ImportSource)
            .AddMap(localizer["technology"], v => v.Technology)
            .AddMap(localizer["hostname"], v => v.Host!.HostName);

    private static IGridifyMapper<Host> HostMapper(
        Microsoft.Extensions.Localization.IStringLocalizer localizer) =>
        new GridifyMapper<Host>()
            .AddMap(localizer["hostname"], h => h.HostName)
            .AddMap("id", h => h.Id)
            .AddMap(localizer["status"], h => h.Status)
            .AddMap("fqdn", h => h.Fqdn)
            .AddMap("ip", h => h.Ip)
            .AddMap("os", h => h.Os)
            .AddMap("teamId", h => h.TeamId)
            .AddMap(localizer["RegistrationDate"], h => h.RegistrationDate);
}

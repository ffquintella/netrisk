using System.Linq.Expressions;
using DAL.Entities;
using Gridify;
using Microsoft.Extensions.Localization;
using ServerServices.Filtering;
using ServerServices.Interfaces;
using Host = DAL.Entities.Host;

namespace API;

/// <summary>
/// The filterable/sortable surface of the list endpoints, replacing ApplicationEntityFilterMapperProvider.
/// Every column is reachable under two names: the invariant one (the resx key, which is also the
/// English spelling) and, where a translation exists, the localized one — so a pt-BR caller can
/// filter on <c>nome</c> while a program keeps using <c>hostname</c> in every culture. Registered
/// scoped and rebuilt per request, because the localized half depends on the request's culture.
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

    private static IGridifyMapper<Vulnerability> VulnerabilityMapper(IStringLocalizer localizer) =>
        Build<Vulnerability>(localizer,
            ("title", v => v.Title),
            ("id", v => v.Id),
            ("Score", v => v.Score),
            ("impact", v => v.Severity),
            ("status", v => v.Status),
            ("first_detection", v => v.FirstDetection),
            ("last_detection", v => v.LastDetection),
            ("detections", v => v.DetectionCount),
            ("analyst", v => v.AnalystId),
            ("host", v => v.HostId),
            ("application", v => v.EntityId),
            ("source", v => v.ImportSource),
            ("technology", v => v.Technology),
            ("hostname", v => v.Host!.HostName));

    private static IGridifyMapper<Host> HostMapper(IStringLocalizer localizer) =>
        Build<Host>(localizer,
            ("hostname", h => h.HostName),
            ("id", h => h.Id),
            ("status", h => h.Status),
            ("fqdn", h => h.Fqdn),
            ("ip", h => h.Ip),
            ("os", h => h.Os),
            ("teamId", h => h.TeamId),
            ("RegistrationDate", h => h.RegistrationDate));

    /// <summary>
    /// Builds a mapper where each column answers to both its invariant name and its translation.
    /// Localized names go in first so that a translation which happens to collide with another
    /// column's invariant name loses to it: the machine-facing contract is the one that has to
    /// keep meaning the same thing in every culture. Gridify matches names case-insensitively, so
    /// a client sending <c>hostName</c> reaches the <c>hostname</c> map.
    /// </summary>
    private static IGridifyMapper<T> Build<T>(
        IStringLocalizer localizer,
        params (string Name, Expression<Func<T, object?>> Selector)[] columns)
    {
        var mapper = new GridifyMapper<T>();

        foreach (var (name, selector) in columns)
        {
            var localized = localizer[name].Value;
            if (!string.Equals(localized, name, StringComparison.OrdinalIgnoreCase))
                mapper.AddMap(localized, selector);
        }

        foreach (var (name, selector) in columns)
            mapper.AddMap(name, selector);

        return mapper;
    }
}

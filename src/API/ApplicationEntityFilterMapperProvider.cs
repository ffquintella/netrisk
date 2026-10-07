using System.Globalization;
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

    private static IGridifyMapper<Vulnerability> VulnerabilityMapper(IStringLocalizer localizer)
    {
        var mapper = VulnerabilityColumns(localizer);

        // Stage 9.4 (T162, S45 §6): the effective EPSS reading, filterable and sortable. A probability is
        // fractional, and Gridify parses a value with the request's culture — under pt-BR "0.1" is not a
        // number and "0,1" splits the filter — so these two parse the value invariantly.
        AddInvariantNumber(mapper, localizer, "epss", v => v.EpssScore);
        AddInvariantNumber(mapper, localizer, "epssPercentile", v => v.EpssPercentile);

        return mapper;
    }

    private static IGridifyMapper<Vulnerability> VulnerabilityColumns(IStringLocalizer localizer) =>
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

    /// <summary>
    /// A fractional column whose filter value is parsed in the invariant culture (a dot as the decimal
    /// separator) whatever the request's culture. A value that is not a number is a
    /// <see cref="GridifyFilteringException"/>, which the list endpoints answer with 400.
    /// </summary>
    private static void AddInvariantNumber<T>(IGridifyMapper<T> mapper, IStringLocalizer localizer, string name,
        Expression<Func<T, object?>> selector)
    {
        object Parse(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new GridifyFilteringException(
                    $"'{value}' is not a number for {name}: use a dot as the decimal separator, e.g. {name}>=0.1.");

        var localized = localizer[name].Value;
        if (!string.Equals(localized, name, StringComparison.OrdinalIgnoreCase))
            mapper.AddMap(localized, selector, Parse);

        mapper.AddMap(name, selector, Parse);
    }

    private static IGridifyMapper<Host> HostMapper(IStringLocalizer localizer) =>
        Build<Host>(localizer,
            ("hostname", h => h.HostName),
            ("id", h => h.Id),
            ("status", h => h.Status),
            ("fqdn", h => h.Fqdn),
            ("ip", h => h.Ip),
            ("os", h => h.Os),
            ("teamId", h => h.TeamId),
            ("RegistrationDate", h => h.RegistrationDate),
            // The Hosts view facets and header (S38 §5.1). Columns the posture and CMDB imports
            // already write; free text except criticality (1–5) and the 0–100 risk score.
            ("criticality", h => h.Criticality),
            ("environment", h => h.Environment),
            ("owner", h => h.Owner),
            ("source", h => h.Source),
            ("riskScore", h => h.RiskScore),
            ("lastVerificationDate", h => h.LastVerificationDate));

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

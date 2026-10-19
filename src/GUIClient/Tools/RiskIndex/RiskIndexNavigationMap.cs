using System.Collections.Generic;
using DAL.Enums;
using GUIClient.Models;
using GUIClient.Navigation;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// The API's navigation destinations as the GUI's (S40 §10.3): <see cref="RiskIndexNavigationTargetDto"/> →
/// <see cref="NavigationTarget"/>, and the destinations an object kind offers in the detail's "Navegação" section.
/// </summary>
public static class RiskIndexNavigationMap
{
    /// <summary>The §10.3 table; null for a module this client does not know.</summary>
    public static NavigationTarget? ToTarget(RiskIndexNavigationTargetDto target) => target.Module switch
    {
        RiskIndexNavigationModule.Risk => new NavigationTarget(AvaliableViews.Risk, target.RecordId),
        RiskIndexNavigationModule.Host => new NavigationTarget(AvaliableViews.Devices, target.RecordId),
        RiskIndexNavigationModule.HostFindings =>
            new NavigationTarget(AvaliableViews.Vulnerabilities, target.RecordId, NavigationTarget.HostSubView),
        RiskIndexNavigationModule.ApplicationFindings =>
            new NavigationTarget(AvaliableViews.Vulnerabilities, target.RecordId, NavigationTarget.EntitySubView),
        RiskIndexNavigationModule.Incident => new NavigationTarget(AvaliableViews.Incidents, target.RecordId),
        RiskIndexNavigationModule.Assessment => new NavigationTarget(AvaliableViews.Assessment, target.RecordId),
        RiskIndexNavigationModule.Entity => new NavigationTarget(AvaliableViews.Entities, target.RecordId),
        _ => null
    };

    /// <summary>The shell module a destination opens.</summary>
    public static AvaliableViews? ViewOf(RiskIndexNavigationModule module) =>
        ToTarget(new RiskIndexNavigationTargetDto(module, 0, ""))?.View;

    /// <summary>
    /// The destinations a kind of object has in NetRisk, in the order of §5.9.2. Vendor objects (external domains, vendor
    /// assets, tenants, suppliers) have no screen here and show their id instead.
    /// </summary>
    public static IReadOnlyList<RiskIndexNavigationModule> Destinations(RiskIndexObjectKind kind) => kind switch
    {
        RiskIndexObjectKind.Scenario => [RiskIndexNavigationModule.Risk],
        RiskIndexObjectKind.Host => [RiskIndexNavigationModule.Host, RiskIndexNavigationModule.HostFindings],
        RiskIndexObjectKind.Application => [RiskIndexNavigationModule.ApplicationFindings, RiskIndexNavigationModule.Entity],
        RiskIndexObjectKind.TargetEntity => [RiskIndexNavigationModule.Entity],
        _ => []
    };

    /// <summary>Whether the object lives outside NetRisk: the detail shows its reference as selectable text.</summary>
    public static bool IsVendorObject(RiskIndexObjectKind kind) => kind is RiskIndexObjectKind.ExternalDomain
        or RiskIndexObjectKind.VendorAsset or RiskIndexObjectKind.Tenant or RiskIndexObjectKind.Supplier;

    /// <summary>The button's text key: <c>RiskNav_‹module›</c>, the key the server also sends.</summary>
    public static string LabelKey(RiskIndexNavigationModule module) => "RiskNav_" + module;
}

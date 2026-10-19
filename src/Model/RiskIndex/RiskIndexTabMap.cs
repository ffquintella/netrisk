using System.Collections.Generic;
using DAL.Enums;

namespace Model.RiskIndex;

/// <summary>
/// The dashboard's asset-class tabs (S40 §5.5): which classes each holds and which categories it carries. Shared by the
/// server (the snapshot job's class days and the read API) and the desktop client (the tab headers and their enablement),
/// so the two can never disagree on what a tab is.
/// </summary>
public static class RiskIndexTabMap
{
    public static readonly RiskIndexTabGroup[] All =
    [
        RiskIndexTabGroup.Devices, RiskIndexTabGroup.InternetFacing, RiskIndexTabGroup.Identities,
        RiskIndexTabGroup.Applications, RiskIndexTabGroup.Clouds
    ];

    public static RiskIndexTabGroup Of(RiskIndexAssetClass assetClass) => assetClass switch
    {
        RiskIndexAssetClass.InternetFacing or RiskIndexAssetClass.ExternalDomain => RiskIndexTabGroup.InternetFacing,
        RiskIndexAssetClass.Identity => RiskIndexTabGroup.Identities,
        RiskIndexAssetClass.Application => RiskIndexTabGroup.Applications,
        RiskIndexAssetClass.Cloud => RiskIndexTabGroup.Clouds,
        _ => RiskIndexTabGroup.Devices // server, workstation, network, OT/IoT, unknown
    };

    /// <summary>The tab's categories in the fixed chart order (REG, EXP, AME, CTL, GOV, TER).</summary>
    public static IReadOnlyList<RiskIndexCategory> Categories(RiskIndexTabGroup tab) => tab switch
    {
        RiskIndexTabGroup.Devices => [RiskIndexCategory.Exp, RiskIndexCategory.Ctl, RiskIndexCategory.Gov],
        RiskIndexTabGroup.Applications => [RiskIndexCategory.Exp, RiskIndexCategory.Gov],
        _ => [RiskIndexCategory.Exp]
    };
}

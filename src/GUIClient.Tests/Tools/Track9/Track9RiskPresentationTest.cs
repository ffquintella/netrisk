using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.Authentication;
using Model.DecisionCycle;
using Model.RiskFlags;
using Model.TailRisk;
using Model.TreatmentEconomics;
using Xunit;

namespace GUIClient.Tests.Tools.Track9;

public class Track9RiskPresentationTest
{
    [Fact]
    public void ThirdLineNeverGetsAWriteEvenWhenItAlsoHoldsTheWritePermission()
    {
        var user = User("riskmanagement", "submit_risks", "plan_mitigations", ThirdLineAssurance.PermissionKey);

        Assert.False(Track9RiskPresentation.CanDeclareFlags(user));
        Assert.False(Track9RiskPresentation.CanSubmitRisk(user));
        Assert.False(Track9RiskPresentation.CanPlanMitigations(user));
    }

    [Fact]
    public void ReviewAndAppetiteGatesMatchTheExactApiPolicyAudiences()
    {
        var isAdminFlagOnly = new AuthenticatedUserInfo { IsAdmin = true, UserRole = "Analyst", UserPermissions = [] };
        var reviewer = new AuthenticatedUserInfo { UserRole = "Analyst", UserPermissions = ["review_high"] };
        var adminRole = new AuthenticatedUserInfo { UserRole = "Admin", UserPermissions = [] };

        Assert.False(Track9RiskPresentation.CanReview(isAdminFlagOnly));
        Assert.True(Track9RiskPresentation.CanReview(reviewer));
        Assert.False(Track9RiskPresentation.CanAdministerAppetite(isAdminFlagOnly));
        Assert.True(Track9RiskPresentation.CanAdministerAppetite(adminRole));
        Assert.False(Track9RiskPresentation.CanReadRisks(isAdminFlagOnly));
        Assert.True(Track9RiskPresentation.CanReadAppetite(adminRole));
        Assert.True(Track9RiskPresentation.CanReadMitigations(new AuthenticatedUserInfo
            { UserRole = "Analyst", UserPermissions = ["plan_mitigations"] }));
    }

    [Fact]
    public void FineNeedsABasisAndEveryRangeMustBeOrdered()
    {
        Assert.Equal("Track9FineBasisRequired", Track9RiskPresentation.LossComponentsError(
            [new LossComponentDraft(LossComponent.Fine, 1, 2, 3, null)]));
        Assert.Equal("Track9LossRangeInvalid", Track9RiskPresentation.LossComponentsError(
            [new LossComponentDraft(LossComponent.Response, 3, 2, 4, "estimate")])) ;
        Assert.Null(Track9RiskPresentation.LossComponentsError(
            [new LossComponentDraft(LossComponent.Fine, 1, 2, 3, "LGPD art. 52")])) ;
    }

    [Fact]
    public void TransferNeedsCounterpartyAndPositiveOneTimeCostNeedsHorizon()
    {
        var empty = new MitigationEconomicsDraft(TreatmentOption.TransferShare, null, null, null, null,
            null, null, null, null, []);
        Assert.Equal("Track9CounterpartyRequired", Track9RiskPresentation.MitigationError(empty));

        var noHorizon = empty with { TransferCounterparty = "Insurer", OneTime = 10 };
        Assert.Equal("Track9HorizonRequired", Track9RiskPresentation.MitigationError(noHorizon));

        Assert.Null(Track9RiskPresentation.MitigationError(noHorizon with { HorizonYears = 3 }));
    }

    [Fact]
    public void TargetRequiresAValueDateAndRationaleWithoutInventingZero()
    {
        Assert.Equal("Track9TargetValueRequired",
            Track9RiskPresentation.RiskTargetError(null, null, DateTimeOffset.UtcNow, "basis"));
        Assert.Equal("Track9TargetDateRequired",
            Track9RiskPresentation.RiskTargetError(5, null, null, "basis"));
        Assert.Equal("Track9RationaleRequired",
            Track9RiskPresentation.RiskTargetError(5, null, DateTimeOffset.UtcNow, " "));
    }

    [Fact]
    public void CorrelationRejectsTheSameRiskAndTailLimitsNeedAtLeastOneValue()
    {
        Assert.Equal("Track9DistinctRisksRequired",
            Track9RiskPresentation.CorrelationError(3, 3, .5m, "basis"));
        Assert.Equal("Track9TailLimitRequired",
            Track9RiskPresentation.TailLimitsError([null, null, null, null, null, null], "basis"));
    }

    [Fact]
    public void MissingMoneyIsExplicitlyNotAvailable()
    {
        Assert.Equal("n/a", Track9RiskPresentation.Money((decimal?)null, "n/a"));
    }

    [Fact]
    public void AnOlderResponseCannotReplaceTheCurrentSelectionOrLatestFilterLoad()
    {
        Assert.False(Track9RiskPresentation.IsCurrentSelection(7, 8));
        Assert.True(Track9RiskPresentation.IsCurrentSelection(8, 8));
        Assert.False(Track9RiskPresentation.IsLatestLoad(2, 3));
        Assert.True(Track9RiskPresentation.IsLatestLoad(3, 3));
    }

    [Theory]
    [InlineData("Localization.resx")]
    [InlineData("Localization.en-US.resx")]
    [InlineData("Localization.pt-BR.resx")]
    public void EveryComputedEnumLabelExistsInEveryShippedCulture(string file)
    {
        Type[] enumTypes =
        [
            typeof(RiskFlagCode), typeof(RiskDecisionKind), typeof(NextDecisionKind),
            typeof(TreatmentOption), typeof(GateCOutcome), typeof(PortfolioTier),
            typeof(PortfolioItemStatus), typeof(LossComponent), typeof(TailAppetiteState),
            typeof(TailRun), typeof(PortfolioBasis), typeof(PortfolioDependence),
            typeof(PortfolioExclusionReason)
        ];
        var computed = enumTypes.SelectMany(type => Enum.GetValues(type).Cast<Enum>())
            .Select(value => Track9RiskPresentation.EnumKey(value))
            .ToList();
        var declared = XDocument.Load(ResourcePath(file)).Root!
            .Elements("data")
            .Select(element => element.Attribute("name")?.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missing = computed.Where(key => !declared.Contains(key)).ToList();
        Assert.True(missing.Count == 0, $"{file} does not declare: {string.Join(", ", missing)}");
        Assert.Equal(60, computed.Count);
    }

    private static AuthenticatedUserInfo User(params string[] permissions) => new()
    {
        UserRole = "Administrator",
        UserPermissions = [.. permissions]
    };

    private static string ResourcePath(string file, [CallerFilePath] string thisFile = "")
    {
        var src = new FileInfo(thisFile).Directory!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(src, "GUIClient", "Resources", file);
    }
}

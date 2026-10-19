using System.Collections.Generic;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// T249 (S40 §8.1): the node-key grammar — at most one entity term and one term of each filter kind, normalized to the
/// canonical order every cache key is built from — and the redaction policy every response that names an object applies.
/// </summary>
[TestSubject(typeof(RiskIndexNodeKey))]
public class RiskIndexNodeKeyTest
{
    [Theory]
    [InlineData(null, "org")]
    [InlineData("org", "org")]
    [InlineData("scope", "scope")]
    [InlineData("e:10", "e:10")]
    [InlineData("u", "u")]
    [InlineData("a:dev;k:4;s:V1-DEV;c:devices;e:10", "e:10;c:devices;s:V1-DEV;k:4;a:dev")]
    [InlineData("c:server", "c:server")]
    public void TestKeysAreNormalized(string? text, string canonical)
    {
        Assert.True(RiskIndexNodeKey.TryParse(text, out var key));
        Assert.Equal(canonical, key.ToString());
    }

    [Theory]
    [InlineData("e:10;e:11")]
    [InlineData("e:10;u")]
    [InlineData("c:devices;c:server")]
    [InlineData("k:6")]
    [InlineData("e:-1")]
    [InlineData("x:1")]
    [InlineData("c:spaceship")]
    [InlineData("s:v1-dev")]
    [InlineData("a:DEV")]
    [InlineData("e:")]
    public void TestKeysOutsideTheGrammarAreRefused(string text) => Assert.False(RiskIndexNodeKey.TryParse(text, out _));

    [Fact]
    public void TestAChildCarriesTheParentsFilters()
    {
        RiskIndexNodeKey.TryParse("c:devices;s:V1-DEV", out var filters);

        var child = RiskIndexNodeKey.ForEntity(12).WithFiltersOf(filters);

        Assert.Equal("e:12;c:devices;s:V1-DEV", child.ToString());
        Assert.Equal("e:12", child.EntityPart.ToString());
        Assert.Equal(RiskIndexTabGroup.Devices, child.TabGroup);
        Assert.Equal("e:12;c:server", RiskIndexNodeKey.ForEntity(12).WithTabGroup(RiskIndexTabGroup.Devices)
            .WithAssetClass(RiskIndexAssetClass.Server).ToString());
    }

    // --- redaction (S40 §11.3) -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(RiskIndexObjectKind.Scenario, RiskIndexModulePermissions.RiskManagement)]
    [InlineData(RiskIndexObjectKind.Host, RiskIndexModulePermissions.Hosts)]
    [InlineData(RiskIndexObjectKind.ExternalDomain, RiskIndexModulePermissions.Hosts)]
    [InlineData(RiskIndexObjectKind.Application, null)]
    [InlineData(RiskIndexObjectKind.TargetEntity, null)]
    public void TestEachKindIsOwnedByItsModule(RiskIndexObjectKind kind, string? permission) =>
        Assert.Equal(permission, RiskIndexRedactionPolicy.LabelPermission(kind));

    [Theory]
    [InlineData("EXP.V", RiskIndexModulePermissions.Vulnerabilities)]
    [InlineData("EXP.D", RiskIndexModulePermissions.Hosts)]
    [InlineData("AME.I", RiskIndexModulePermissions.Incidents)]
    [InlineData("CTL.A", RiskIndexModulePermissions.Assessments)]
    [InlineData("GOV.A", RiskIndexModulePermissions.RiskManagement)]
    public void TestEachSignalIsOwnedByItsModule(string factor, string permission) =>
        Assert.Equal(permission, RiskIndexRedactionPolicy.SignalPermission(factor));

    [Fact]
    public void TestALabelIsRedactedWithoutItsPermissionAndPassesForAnAdministrator()
    {
        var none = new HashSet<string>();

        var redacted = RiskIndexRedactionPolicy.Label(RiskIndexObjectKind.Scenario, "7", "Data breach", false, none);
        var admin = RiskIndexRedactionPolicy.Label(RiskIndexObjectKind.Scenario, "7", "Data breach", true, none);

        Assert.Equal(("Scenario #7", RiskIndexModulePermissions.RiskManagement), (redacted.Label, redacted.RequiresPermission));
        Assert.Equal(("Data breach", (string?)null), (admin.Label, admin.RequiresPermission));
        Assert.Equal("admin|hosts,riskmanagement",
            RiskIndexRedactionPolicy.PermissionKey(true, new HashSet<string> { "riskmanagement", "hosts", "risk_index_view" }));
    }
}

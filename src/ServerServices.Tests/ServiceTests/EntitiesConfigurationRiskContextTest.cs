using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Model.Entities;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// Entity-configuration version 2.7 (T242, S39 §5.2, §5.3, §10.2): <c>criticality</c> on process, application and the
/// two unit types, <c>internetFacing</c> on application, and the <c>activity</c> type under <c>businessProcess</c>.
/// Reads the real <c>EntitiesConfiguration.yaml</c> through <c>EntitiesService</c>, so the definitions asserted are
/// the ones the API serves and validates against.
/// </summary>
public class EntitiesConfigurationRiskContextTest : InMemoryServiceTestBase
{
    private readonly IEntitiesService _svc;

    public EntitiesConfigurationRiskContextTest()
    {
        _svc = GetService<IEntitiesService>();
    }

    private async Task<EntitiesConfiguration> Config() => await _svc.GetEntitiesConfigurationAsync();

    private static EntitiesPropertyDto Dto(string type, string value) => new() { Type = type, Value = value, Name = type };

    [Fact]
    public async Task TestTheConfigurationParsesAtVersion27()
    {
        var config = await Config();

        Assert.Equal("2.7", config.Version);
        Assert.Contains("activity", config.Definitions.Keys);
    }

    [Theory]
    [InlineData("businessProcess")]
    [InlineData("application")]
    [InlineData("organizationUnit")]
    [InlineData("subOrganizationUnit")]
    public async Task TestCriticalityIsAnOptionalSingleIntegerOn(string definition)
    {
        var criticality = (await Config()).Definitions[definition].Properties[RiskContextProperties.Criticality];

        Assert.Equal("Integer", criticality.Type);
        Assert.Equal("Criticality", criticality.Label);
        Assert.False(criticality.Multiple);
        Assert.True(criticality.Nullable);
    }

    [Fact]
    public async Task TestInternetFacingIsAnOptionalSingleBooleanOnApplicationOnly()
    {
        var config = await Config();

        var internetFacing = config.Definitions["application"].Properties[RiskContextProperties.InternetFacing];
        Assert.Equal("Boolean", internetFacing.Type);
        Assert.False(internetFacing.Multiple);
        Assert.True(internetFacing.Nullable);

        var others = config.Definitions
            .Where(d => d.Key != "application" && d.Value.Properties.ContainsKey(RiskContextProperties.InternetFacing))
            .Select(d => d.Key)
            .ToList();
        Assert.Empty(others);
    }

    [Fact]
    public async Task TestCriticalityIsOnlyOnTheFourTypesTheSpecificationNames()
    {
        var config = await Config();

        var carriers = config.Definitions
            .Where(d => d.Value.Properties.ContainsKey(RiskContextProperties.Criticality))
            .Select(d => d.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["application", "businessProcess", "organizationUnit", "subOrganizationUnit"], carriers);
    }

    [Fact]
    public async Task TestActivityIsAllowedOnlyUnderBusinessProcess()
    {
        var config = await Config();

        var parents = config.Definitions
            .Where(d => d.Value.AllowedChildren?.Contains("activity") == true)
            .Select(d => d.Key)
            .ToList();

        Assert.Equal(["businessProcess"], parents);
        Assert.False(config.Definitions["activity"].IsRoot);
        Assert.Empty(config.Definitions["activity"].AllowedChildren ?? []);
    }

    [Fact]
    public async Task TestActivityCarriesNeitherApplicationsNorRiskContext()
    {
        var properties = (await Config()).Definitions["activity"].Properties;

        // S39 §10.2: an activity has no `applications`, so its exposure reads "unavailable — requires M39".
        Assert.False(properties.ContainsKey("applications"));
        Assert.All(RiskContextProperties.Names, name => Assert.False(properties.ContainsKey(name)));
        Assert.False(properties["name"].Nullable);
    }

    [Fact]
    public async Task TestEveryRiskContextPropertyTheConfigurationDefinesIsKnownToTheWritePolicy()
    {
        // EntityWritePolicy keys the permission by property name: a definition whose name differs by a letter would
        // be writable without entity_risk_context.
        var config = await Config();

        foreach (var name in new[] { RiskContextProperties.Criticality, RiskContextProperties.InternetFacing })
        {
            Assert.Contains(config.Definitions, d => d.Value.Properties.ContainsKey(name));
            Assert.True(RiskContextProperties.IsRiskContext(name));
        }
    }

    [Fact]
    public async Task TestAnEntityStoredUnderTheEarlierVersionStillValidates()
    {
        // The new properties are nullable, so an application or process saved under 2.6 (no criticality, no
        // internetFacing) needs nothing re-entered. The stored definition_version is left as it was.
        await Config();

        _svc.ValidatePropertyList("application", [Dto("name", "Portal")]);
        _svc.ValidatePropertyList("organizationUnit", [Dto("name", "Unit"), Dto("mainOrganization", "Parent")]);
    }

    [Fact]
    public void TestTheNewPropertiesValidateByType()
    {
        _svc.ValidatePropertyList("application",
            [Dto("name", "Portal"), Dto(RiskContextProperties.Criticality, "5"), Dto(RiskContextProperties.InternetFacing, "true")]);

        Assert.Throws<Exception>(() => _svc.ValidatePropertyList("application",
            [Dto("name", "Portal"), Dto(RiskContextProperties.Criticality, "high")]));
        Assert.Throws<Exception>(() => _svc.ValidatePropertyList("application",
            [Dto("name", "Portal"), Dto(RiskContextProperties.InternetFacing, "sometimes")]));
    }

    [Fact]
    public void TestAnActivityRequiresANameAndRefusesACriticality()
    {
        _svc.ValidatePropertyList("activity", [Dto("name", "Reconcile payments"), Dto("isActive", "true")]);

        Assert.Throws<Exception>(() => _svc.ValidatePropertyList("activity", [Dto("isActive", "true")]));
        Assert.ThrowsAny<Exception>(() => _svc.ValidatePropertyList("activity",
            [Dto("name", "Reconcile payments"), Dto("isActive", "true"), Dto(RiskContextProperties.Criticality, "3")]));
    }
}

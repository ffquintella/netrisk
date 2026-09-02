using System.Collections.Generic;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.Integrations.IssueTrackers.Jira;
using Xunit;

namespace ServerServices.Tests.Track46;

/// <summary>
/// The Assets attribute projection (Track 4 milestone 4.6) — the mapping engine's core.
///
/// A pure function over a payload and a mapping, which is exactly why it is worth testing here rather
/// than through the import: every transform, the identity fallbacks and the "the register said
/// nothing versus the register said empty" distinction are decided in this one place, and each of
/// them is a way a customer's estate could be silently mis-imported.
/// </summary>
[TestSubject(typeof(AssetAttributeProjector))]
public class AssetAttributeProjectorTest
{
    private static AssetObjectPayload Payload(params (int Id, string Name, string[] Values)[] attributes)
    {
        var payload = new AssetObjectPayload { ObjectId = "101", ObjectKey = "ITSM-88" };

        foreach (var (id, name, values) in attributes)
        {
            payload.Attributes[id] = new List<string>(values);
            payload.AttributesByName[name] = new List<string>(values);
        }

        return payload;
    }

    private static JiraObjectAttributeMapping Map(int? sourceId, string sourceName, string target,
        JiraAttributeTransform transform = JiraAttributeTransform.None, string? constant = null) => new()
    {
        SourceAttributeId = sourceId,
        SourceAttributeName = sourceName,
        TargetField = target,
        Transform = transform,
        ConstantValue = constant
    };

    [Fact]
    public void ItProjectsTheFourFieldsThisMilestoneExistsToImport()
    {
        var payload = Payload(
            (1, "Name", ["srv-prod-01"]),
            (2, "Owner", ["Alice Silva"]),
            (3, "Environment", ["Production"]),
            (4, "Status", ["In Production"]));

        var projected = AssetAttributeProjector.Project(payload,
        [
            Map(1, "Name", MappableFields.Name),
            Map(2, "Owner", MappableFields.Owner),
            Map(3, "Environment", MappableFields.Environment),
            Map(4, "Status", MappableFields.Active, JiraAttributeTransform.TruthyBoolean)
        ]);

        Assert.Equal("srv-prod-01", projected.Name);
        Assert.Equal("Alice Silva", projected.Owner);
        Assert.Equal("Production", projected.Environment);
        Assert.True(projected.Active);
    }

    /// <summary>
    /// The vocabulary a CMDB actually uses for "yes".
    ///
    /// This is the case that would have retired an entire estate on first import: an Assets status
    /// attribute holds <c>Active</c> or <c>In Production</c> far more often than <c>true</c>, and a
    /// strict boolean parse reads every one of those as inactive.
    /// </summary>
    [Theory]
    [InlineData("true")]
    [InlineData("Yes")]
    [InlineData("1")]
    [InlineData("Active")]
    [InlineData("In Production")]
    [InlineData("In Service")]
    [InlineData("Operational")]
    [InlineData("Ativo")]
    public void ACmdbsWordForYesReadsAsActive(string value)
    {
        var projected = AssetAttributeProjector.Project(
            Payload((1, "Name", ["srv"]), (2, "Status", [value])),
            [Map(1, "Name", MappableFields.Name),
             Map(2, "Status", MappableFields.Active, JiraAttributeTransform.TruthyBoolean)]);

        Assert.True(projected.Active);
    }

    [Theory]
    [InlineData("false")]
    [InlineData("Decommissioned")]
    [InlineData("Retired")]
    [InlineData("")]
    public void AnythingElseReadsAsInactive(string value)
    {
        var projected = AssetAttributeProjector.Project(
            Payload((1, "Name", ["srv"]), (2, "Status", [value])),
            [Map(1, "Name", MappableFields.Name),
             Map(2, "Status", MappableFields.Active, JiraAttributeTransform.TruthyBoolean)]);

        // An empty value maps to false only because a mapping row *exists*; the distinction from "no
        // mapping at all" is the next test, and it is the one that matters.
        Assert.False(projected.Active ?? false);
    }

    /// <summary>
    /// No active-state mapping is not the same statement as an inactive object.
    ///
    /// A mapping without one must leave a host that somebody retired by hand retired, and must not
    /// activate one either. Null is that third state, and collapsing it to false would have the
    /// importer overwrite a human decision on every run.
    /// </summary>
    [Fact]
    public void AMappingWithNoActiveRowLeavesTheStateUnknown()
    {
        var projected = AssetAttributeProjector.Project(
            Payload((1, "Name", ["srv"])),
            [Map(1, "Name", MappableFields.Name)]);

        Assert.Null(projected.Active);
    }

    [Fact]
    public void TheAttributeNameIsTheFallbackWhenTheIdNoLongerExists()
    {
        var payload = new AssetObjectPayload { ObjectId = "1" };
        payload.AttributesByName["Hostname"] = ["web-02"];

        // The regression this guards: an Assets schema that is rebuilt keeps its attribute names and
        // issues new ids, so an id-only lookup leaves every mapping reading nothing — which presents
        // as "the import stopped working" with no error anywhere.
        var projected = AssetAttributeProjector.Project(payload,
            [Map(9999, "Hostname", MappableFields.Name)]);

        Assert.Equal("web-02", projected.Name);
    }

    [Fact]
    public void AConstantFillsAGapAndDoesNotOverrideTheRegister()
    {
        var projected = AssetAttributeProjector.Project(
            Payload((1, "Name", ["srv"]), (3, "Env", ["Homolog"])),
            [Map(1, "Name", MappableFields.Name),
             Map(3, "Env", MappableFields.Environment, constant: "Production"),
             Map(7, "Missing", MappableFields.Owner, constant: "Platform Team")]);

        // Present attribute wins; the constant only fills the absent one. The other way round would
        // make a constant a way to silently ignore the register.
        Assert.Equal("Homolog", projected.Environment);
        Assert.Equal("Platform Team", projected.Owner);
    }

    [Fact]
    public void AMultiValuedAttributeJoinsUnlessTheMappingAsksForTheFirst()
    {
        var payload = Payload((2, "Owners", ["Alice", "Bob"]));
        payload.Attributes[1] = ["srv"];
        payload.AttributesByName["Name"] = ["srv"];

        var joined = AssetAttributeProjector.Project(payload,
            [Map(1, "Name", MappableFields.Name), Map(2, "Owners", MappableFields.Owner)]);

        Assert.Equal("Alice, Bob", joined.Owner);

        var first = AssetAttributeProjector.Project(payload,
        [
            Map(1, "Name", MappableFields.Name),
            Map(2, "Owners", MappableFields.Owner, JiraAttributeTransform.FirstOfList)
        ]);

        Assert.Equal("Alice", first.Owner);
    }

    [Fact]
    public void TheObjectsOwnLabelIsTheLastResortForTheName()
    {
        var payload = new AssetObjectPayload { ObjectId = "1", Label = "srv-legacy-07" };

        // Without this, an object whose mapped name attribute happens to be empty would be reported
        // as "no name" for a row whose name a human can plainly see.
        var projected = AssetAttributeProjector.Project(payload,
            [Map(1, "Name", MappableFields.Name)]);

        Assert.Equal("srv-legacy-07", projected.Name);
    }

    [Theory]
    [InlineData(JiraAttributeTransform.Trim, "  web  ", "web")]
    [InlineData(JiraAttributeTransform.Upper, "web", "WEB")]
    [InlineData(JiraAttributeTransform.Lower, "WEB", "web")]
    [InlineData(JiraAttributeTransform.Integer, "3 - High", "3")]
    [InlineData(JiraAttributeTransform.Integer, "Tier 2", "2")]
    public void TransformsNormaliseWhatACmdbActuallyHolds(JiraAttributeTransform transform, string raw,
        string expected)
    {
        Assert.Equal(expected, AssetAttributeProjector.Apply(transform, raw));
    }

    /// <summary>
    /// A criticality written as text still imports.
    ///
    /// "3 - High" and "Tier 2" are how a register spells a number, and refusing them would mean the
    /// criticality field imports for nobody.
    /// </summary>
    [Fact]
    public void ATextualCriticalityStillReadsAsANumber()
    {
        var projected = AssetAttributeProjector.Project(
            Payload((1, "Name", ["srv"]), (5, "Criticality", ["4 - Very high"])),
            [Map(1, "Name", MappableFields.Name),
             Map(5, "Criticality", "Criticality", JiraAttributeTransform.Integer)]);

        Assert.Equal(4, projected.GetInt("Criticality"));
    }

    [Fact]
    public void AnUnparseableIntegerIsDroppedRatherThanStoredAsGarbage()
    {
        var projected = AssetAttributeProjector.Project(
            Payload((1, "Name", ["srv"]), (5, "Criticality", ["unknown"])),
            [Map(1, "Name", MappableFields.Name),
             Map(5, "Criticality", "Criticality", JiraAttributeTransform.Integer)]);

        Assert.Null(projected.GetInt("Criticality"));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using GUIClient.Tests.Resources;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Entities;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// The review's finding on the entity form (T241/T242): with the risk-context fields editable, an absent
/// <c>criticality</c> loaded as 0 and an absent <c>internetFacing</c> as false, and every save sent them — so renaming
/// an entity declared criticality 0 and not internet-facing (an audited change feeding M53's weights), and a scoped
/// <c>entity_risk_context</c> holder, whom the API refuses any risk-context change, got 403 on every save. A nullable
/// Integer or Boolean field now loads <em>not set</em> and saves nothing until the user sets it.
/// </summary>
[TestSubject(typeof(EntityFieldValues))]
public class EntityFieldValuesTest
{
    private static readonly IReadOnlyList<string> Nothing = [];

    [Fact]
    public void TestAnAbsentNullableCriticalityLoadsNotSetAndSavesNothing()
    {
        var loaded = EntityFieldValues.LoadInteger(Nothing, nullable: true, defaultValue: "");

        Assert.Null(loaded);
        Assert.Null(EntityFieldValues.SaveInteger(loaded));
    }

    [Fact]
    public void TestAnAbsentNullableInternetFacingLoadsNotSetAndSavesNothing()
    {
        var loaded = EntityFieldValues.LoadBoolean(Nothing, nullable: true, defaultValue: "False");

        Assert.Null(loaded);
        Assert.Null(EntityFieldValues.SaveBoolean(loaded));
    }

    /// <summary>
    /// Every nullable Integer and Boolean property of every shipped definition, on an entity that has none stored: a save
    /// that changes nothing else sends none of them, so the server's risk-context comparison sees no change and a scoped
    /// holder's save is not refused (<see cref="RiskContextProperties.Changed"/>, the rule the API applies).
    /// </summary>
    [Fact]
    public void TestAnUntouchedFormDeclaresNoRiskContextOnAnyDefinition()
    {
        var configuration = EntityConfigurationSchema.Load();

        foreach (var (definition, entity) in configuration.Definitions)
        {
            var saved = new List<(string Key, string? Value)>();

            foreach (var (key, type) in entity.Properties.Where(p => p.Value.Nullable))
            {
                var value = type.Type switch
                {
                    "Integer" => EntityFieldValues.SaveInteger(EntityFieldValues.LoadInteger(Nothing, true, type.DefaultValue)),
                    "Boolean" => EntityFieldValues.SaveBoolean(EntityFieldValues.LoadBoolean(Nothing, true, type.DefaultValue)),
                    _ => null
                };
                if (value is not null) saved.Add((key, value));
            }

            Assert.True(saved.Count == 0, $"{definition} saves {string.Join(", ", saved)} for fields nobody set.");
            Assert.Empty(RiskContextProperties.Changed([], saved));
        }
    }

    [Theory]
    [InlineData("3", "3")]
    [InlineData(" 5 ", "5")]
    public void TestAStoredIntegerRoundTripsUnchanged(string stored, string expected)
    {
        var saved = EntityFieldValues.SaveInteger(EntityFieldValues.LoadInteger([stored], nullable: true, defaultValue: ""));

        Assert.Equal(expected, saved);
        Assert.Empty(RiskContextProperties.Changed([(RiskContextProperties.Criticality, stored)],
            [(RiskContextProperties.Criticality, saved)]));
    }

    [Theory]
    [InlineData("True", "true")]
    [InlineData("false", "false")]
    public void TestAStoredBooleanRoundTripsUnchanged(string stored, string expected)
    {
        var saved = EntityFieldValues.SaveBoolean(EntityFieldValues.LoadBoolean([stored], nullable: true, defaultValue: "False"));

        Assert.Equal(expected, saved);
        Assert.Empty(RiskContextProperties.Changed([(RiskContextProperties.InternetFacing, stored)],
            [(RiskContextProperties.InternetFacing, saved)]));
    }

    /// <summary>Once the user sets a value it is saved, a deliberate false and a deliberate 0 included.</summary>
    [Fact]
    public void TestAValueTheUserSetsIsSaved()
    {
        Assert.Equal("0", EntityFieldValues.SaveInteger(0m));
        Assert.Equal("4", EntityFieldValues.SaveInteger(4m));
        Assert.Equal("false", EntityFieldValues.SaveBoolean(false));
        Assert.Equal("true", EntityFieldValues.SaveBoolean(true));
    }

    /// <summary>A non-nullable field still must hold a value, so it keeps the definition's default, as before.</summary>
    [Fact]
    public void TestANonNullableFieldKeepsItsDefault()
    {
        Assert.True(EntityFieldValues.LoadBoolean(Nothing, nullable: false, defaultValue: "True"));
        Assert.False(EntityFieldValues.LoadBoolean(Nothing, nullable: false, defaultValue: null));
        Assert.Equal(7m, EntityFieldValues.LoadInteger(Nothing, nullable: false, defaultValue: "7"));
        Assert.Equal(0m, EntityFieldValues.LoadInteger(Nothing, nullable: false, defaultValue: ""));
    }

    /// <summary>A stored value that does not parse counts as absent: not set on a nullable field, never a silent 0.</summary>
    [Fact]
    public void TestAnUnparseableStoredValueOnANullableFieldIsNotSet()
    {
        Assert.Null(EntityFieldValues.LoadInteger(["high"], nullable: true, defaultValue: ""));
        Assert.Null(EntityFieldValues.LoadBoolean(["maybe"], nullable: true, defaultValue: "False"));
    }
}

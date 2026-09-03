using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using ConsoleClient.Commands.Settings;
using Spectre.Console.Cli;
using Xunit;

namespace ConsoleClient.Tests.Commands;

/// <summary>
/// Structural guards over the CLI surface. Every command is dispatched by matching the
/// <c>&lt;operation&gt;</c> positional argument against a string inside the command body, so the
/// argument's presence, its position, and the Description that documents the accepted verbs are the
/// contract between the CLI and its users.
/// </summary>
public class CommandSettingsSurfaceTest
{
    private static IEnumerable<Type> SettingsTypes =>
        typeof(DatabaseSettings).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && t.Namespace == "ConsoleClient.Commands.Settings"
                        && typeof(CommandSettings).IsAssignableFrom(t));

    public static TheoryData<Type> AllSettingsTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in SettingsTypes) data.Add(type);
        return data;
    }

    [Fact]
    public void TestEverySettingsClassIsDiscovered()
    {
        var names = SettingsTypes.Select(t => t.Name).OrderBy(n => n).ToList();

        Assert.Equal(
            new[]
            {
                nameof(CalculationSettings), nameof(CiSettings), nameof(DatabaseSettings),
                nameof(KeysSettings), nameof(RegistrationSettings), nameof(SettingsSettings),
                nameof(TechnologySettings), nameof(UserSettings), nameof(WebsiteSettings)
            },
            names);
    }

    [Theory]
    [MemberData(nameof(AllSettingsTypes))]
    public void TestOperationIsTheFirstRequiredArgument(Type settingsType)
    {
        var operation = settingsType.GetProperty("Operation");

        Assert.NotNull(operation);
        Assert.Equal(typeof(string), operation.PropertyType);

        var argument = operation.GetCustomAttribute<CommandArgumentAttribute>();

        Assert.NotNull(argument);
        Assert.Equal(0, argument.Position);
        Assert.True(argument.IsRequired, $"{settingsType.Name}.Operation must be a required argument.");
    }

    [Theory]
    [MemberData(nameof(AllSettingsTypes))]
    public void TestOperationDefaultsToEmptyRatherThanNull(Type settingsType)
    {
        // Asserted rather than suppressed: a settings type with no public parameterless constructor,
        // or no Operation property, is a real failure of the surface this test exists to police, and
        // a null-forgiving `!` would report it as an unhelpful NullReferenceException instead.
        var settings = Activator.CreateInstance(settingsType) as CommandSettings;
        Assert.NotNull(settings);

        var property = settingsType.GetProperty("Operation");
        Assert.NotNull(property);

        var operation = property.GetValue(settings);

        Assert.Equal(string.Empty, operation);
    }

    [Theory]
    [MemberData(nameof(AllSettingsTypes))]
    public void TestOperationDocumentsItsAcceptedVerbs(Type settingsType)
    {
        var description = settingsType.GetProperty("Operation")!.GetCustomAttribute<DescriptionAttribute>();

        Assert.NotNull(description);
        Assert.False(string.IsNullOrWhiteSpace(description.Description),
            $"{settingsType.Name}.Operation needs a Description listing the verbs it accepts.");
    }

    [Theory]
    [MemberData(nameof(AllSettingsTypes))]
    public void TestPositionalArgumentsAreContiguousFromZero(Type settingsType)
    {
        var positions = settingsType.GetProperties()
            .Select(p => p.GetCustomAttribute<CommandArgumentAttribute>())
            .Where(a => a != null)
            .Select(a => a!.Position)
            .OrderBy(p => p)
            .ToList();

        Assert.Equal(Enumerable.Range(0, positions.Count), positions);
    }

    [Theory]
    [MemberData(nameof(AllSettingsTypes))]
    public void TestNoOptionIsDeclaredTwice(Type settingsType)
    {
        var options = settingsType.GetProperties()
            .Select(p => p.GetCustomAttribute<CommandOptionAttribute>())
            .Where(a => a != null)
            .SelectMany(a => a!.LongNames)
            .ToList();

        Assert.Equal(options.Count, options.Distinct().Count());
    }

    [Fact]
    public void TestDatabaseSettingsExposesTheSchemaUpgradeOptions()
    {
        // The Track 6 upgrade-schema flow is driven entirely by these flags; losing one silently
        // turns a guarded apply into an unguarded one.
        var options = typeof(DatabaseSettings).GetProperties()
            .Select(p => p.GetCustomAttribute<CommandOptionAttribute>())
            .Where(a => a != null)
            .SelectMany(a => a!.LongNames)
            .ToList();

        Assert.Contains("phase", options);
        Assert.Contains("env", options);
        Assert.Contains("check", options);
        Assert.Contains("dry-run", options);
        Assert.Contains("yes", options);
        Assert.Contains("output", options);
    }

    [Fact]
    public void TestDestructiveSchemaFlagsDefaultToOff()
    {
        var settings = new DatabaseSettings();

        Assert.False(settings.Yes);
        Assert.False(settings.Check);
        Assert.False(settings.DryRun);
        Assert.Null(settings.Phase);
        Assert.Null(settings.Environment);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Headless.XUnit;
using GUIClient.UiTests.Infrastructure;
using Xunit;
using Xunit.v3;

namespace GUIClient.UiTests.Guards;

/// <summary>
/// S54 §8 A4: lock 2 of §3.2 (every UI test skips unless <c>NETRISK_UI_TESTS=1</c>) holds only if every
/// test here is declared with <see cref="UiFactAttribute"/> or <see cref="UiTheoryAttribute"/>. A bare
/// <c>[Fact]</c>, <c>[Theory]</c>, <c>[AvaloniaFact]</c> or <c>[AvaloniaTheory]</c> would run
/// unconditionally, so this scan fails on one.
///
/// These two tests are the deliberate exception to the rule they enforce: they are reflection over
/// this assembly — no Avalonia, no window — so they carry a plain <c>[Fact]</c> and run even without the
/// opt-in ("sem a trava", S54 §8). Running the executable without the variable therefore reports these
/// two as passed and every UI test as skipped.
/// </summary>
public class UiTestAttributeGuardTest
{
    [Fact]
    public void TestEveryTestInThisProjectIsDeclaredWithUiFactOrUiTheory()
    {
        var types = typeof(UiFactAttribute).Assembly.GetTypes()
            .Where(t => t != typeof(UiTestAttributeGuardTest) && t.DeclaringType != typeof(UiTestAttributeGuardTest));

        var offenders = UnguardedTests(types);

        Assert.True(offenders.Count == 0,
            "These tests bypass the NETRISK_UI_TESTS opt-in (S54 §3.2). Declare them with [UiFact] or " +
            "[UiTheory] instead:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>The scan itself, against a fixture holding one test of each kind.</summary>
    [Fact]
    public void TestTheScanFlagsBareFactsAndAvaloniaFactsButNotUiFacts()
    {
        var offenders = UnguardedTests([typeof(Fixture)]);

        Assert.Equal(
            ["Fixture.BareAvaloniaFact", "Fixture.BareAvaloniaTheory", "Fixture.BareFact", "Fixture.BareTheory"],
            offenders.Order(StringComparer.Ordinal));
    }

    /// <summary>Every method carrying an xUnit test attribute other than the two project ones.</summary>
    internal static List<string> UnguardedTests(IEnumerable<Type> types) =>
        types
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                          BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes(inherit: true).Any(a => a is IFactAttribute))
            .Where(m => !m.GetCustomAttributes(inherit: true).Any(a => a is UiFactAttribute or UiTheoryAttribute))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

    /// <summary>Abstract, so xUnit never runs it; it exists only for the scan above to look at.</summary>
    public abstract class Fixture
    {
        [Fact]
        public void BareFact() { }

        [Theory]
        [InlineData(1)]
        public void BareTheory(int value) => _ = value;

        [AvaloniaFact]
        public void BareAvaloniaFact() { }

        [AvaloniaTheory]
        [InlineData(1)]
        public void BareAvaloniaTheory(int value) => _ = value;

        [UiFact]
        public void Guarded() { }

        [UiTheory]
        [InlineData(1)]
        public void GuardedTheory(int value) => _ = value;
    }
}

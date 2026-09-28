using System;
using System.Collections.Generic;
using GUIClient.Tools;
using JetBrains.Annotations;
using Model.Secrets;
using Xunit;

namespace GUIClient.Tests.Tools;

/// <summary>
/// One control a vault plugin contributed to a host screen, while an operator is filling it in.
///
/// The rules here are the same ones the server applies in <c>PluginFieldSpecProjection.Validate</c>,
/// and they are written twice deliberately: here so the operator sees the problem beside the
/// control while the form is open, there because the form is not the only caller of the API. The
/// server's copy decides; this one is what stops a credential field being bound to something the
/// vault will refuse at 3am.
/// </summary>
[TestSubject(typeof(PluginFieldState))]
public class PluginFieldStateTest
{
    /// <summary>
    /// Stands in for the resx lookup: the key back with one placeholder, so a message is both
    /// identifiable and formattable. One placeholder and not two because most of these messages take
    /// only the label — a surplus argument is ignored, a missing one throws.
    /// </summary>
    private static string Localize(string key) => key + ": {0}";

    private static VaultFieldSpecView Spec(
        VaultFieldKind kind = VaultFieldKind.Text,
        bool required = false,
        VaultFieldFormat format = VaultFieldFormat.Any,
        int maxLength = 128,
        bool allowCustom = false) =>
        new()
        {
            Key = "environment",
            Label = "Environment",
            Kind = kind,
            Required = required,
            Format = format,
            MaxLength = maxLength,
            AllowCustomValue = allowCustom
        };

    private static PluginFieldState Build(VaultFieldSpecView spec,
        IReadOnlyDictionary<string, string>? current = null) =>
        Assert.Single(PluginFieldState.Build([spec], current));

    [Fact]
    public void StartsFromTheStoredValue()
    {
        var field = Build(Spec(), new Dictionary<string, string> { ["environment"] = "hml" });

        Assert.Equal("hml", field.Value);
    }

    [Fact]
    public void FallsBackToTheDeclaredDefault()
    {
        var spec = Spec();
        spec.DefaultValue = "prd";

        Assert.Equal("prd", Build(spec).Value);
    }

    /// <summary>
    /// A blank control means "not set". Storing an empty string would make a plugin's lookup
    /// succeed with nothing in it, which is worse than the key being absent.
    /// </summary>
    [Fact]
    public void ABlankValueIsNotStored()
    {
        var field = Build(Spec());
        field.Value = "   ";

        Assert.Empty(PluginFieldState.Values([field]));
    }

    [Fact]
    public void TrimsWhatItStores()
    {
        var field = Build(Spec());
        field.Value = " hml ";

        Assert.Equal(new Dictionary<string, string> { ["environment"] = "hml" },
            PluginFieldState.Values([field]));
    }

    // --- the kinds ----------------------------------------------------------------------------

    [Fact]
    public void ATogglesValueIsTheTextTrueOrFalse()
    {
        var field = Build(Spec(VaultFieldKind.Toggle));

        Assert.False(field.BoolValue);

        field.BoolValue = true;

        Assert.Equal("true", field.Value);
        Assert.True(field.BoolValue);
    }

    [Fact]
    public void ANumbersValueIsInvariantText()
    {
        var field = Build(Spec(VaultFieldKind.Number));
        field.NumberValue = 42;

        Assert.Equal("42", field.Value);
        Assert.Equal(42, field.NumberValue);
    }

    [Fact]
    public void AChoiceMatchesItsSelectionToTheOfferedOptions()
    {
        var field = Build(Spec(VaultFieldKind.Choice));

        field.Options =
        [
            new VaultFieldOptionView { Value = "hml", Label = "Homologation" },
            new VaultFieldOptionView { Value = "prd", Label = "Production" }
        ];

        field.Value = "prd";

        Assert.Equal("Production", field.SelectedOption!.Label);

        field.SelectedOption = field.Options[0];

        Assert.Equal("hml", field.Value);
    }

    /// <summary>
    /// A value no longer on offer is dropped on a closed list. Keeping it would let the screen
    /// commit something the operator cannot see in the combo, which is how a field ends up bound to
    /// a value nobody chose.
    /// </summary>
    [Fact]
    public void AClosedChoiceDropsAValueTheNewOptionsDoNotContain()
    {
        var field = Build(Spec(VaultFieldKind.Choice),
            new Dictionary<string, string> { ["environment"] = "retired" });

        field.Options = [new VaultFieldOptionView { Value = "hml", Label = "hml" }];

        Assert.Empty(field.Value);
    }

    /// <summary>
    /// An open list is a suggestion box for a vault that cannot enumerate what it accepts, so a
    /// typed value survives a refreshed list — and the plugin rejects a bad one on the read path.
    /// </summary>
    [Fact]
    public void AnOpenChoiceKeepsAValueTheOptionsDoNotContain()
    {
        var field = Build(Spec(VaultFieldKind.Choice, allowCustom: true),
            new Dictionary<string, string> { ["environment"] = "typed" });

        field.Options = [new VaultFieldOptionView { Value = "hml", Label = "hml" }];

        Assert.Equal("typed", field.Value);
    }

    // --- validation ---------------------------------------------------------------------------

    [Fact]
    public void AnEmptyOptionalFieldIsValid()
    {
        var field = Build(Spec());

        Assert.True(field.Validate(Localize));
        Assert.False(field.HasError);
    }

    /// <summary>
    /// The case the whole feature exists for: a vault that refuses every read which does not name
    /// an environment, said while the operator is choosing rather than by a sync job at 3am.
    /// </summary>
    [Fact]
    public void AnEmptyRequiredFieldIsNot()
    {
        var field = Build(Spec(required: true));

        Assert.False(field.Validate(Localize));
        Assert.Contains("VaultPluginFieldRequiredMSG", field.Error);
        Assert.Contains("Environment", field.Error);
    }

    [Theory]
    [InlineData(VaultFieldKind.Toggle, VaultFieldFormat.Any, "maybe")]
    [InlineData(VaultFieldKind.Number, VaultFieldFormat.Any, "twelve")]
    [InlineData(VaultFieldKind.Text, VaultFieldFormat.NoWhitespace, "two words")]
    [InlineData(VaultFieldKind.Text, VaultFieldFormat.Identifier, "teams/netrisk")]
    public void ReportsAValueTheDeclarationDoesNotAllow(VaultFieldKind kind, VaultFieldFormat format,
        string value)
    {
        var field = Build(Spec(kind, format: format));
        field.Value = value;

        Assert.False(field.Validate(Localize));
        Assert.True(field.HasError);
    }

    [Fact]
    public void ReportsAValueLongerThanDeclared()
    {
        var field = Build(Spec(maxLength: 4));
        field.Value = "12345";

        Assert.False(field.Validate(Localize));
        Assert.Contains("VaultPluginFieldTooLongMSG", field.Error);
    }

    [Fact]
    public void ReportsAClosedChoiceValueThatWasNotOffered()
    {
        var field = Build(Spec(VaultFieldKind.Choice));
        field.Options = [new VaultFieldOptionView { Value = "hml", Label = "hml" }];

        // Set after the options, so the closed-list drop does not clear it first.
        field.Value = "made-up";

        Assert.False(field.Validate(Localize));
        Assert.Contains("VaultPluginFieldNotOfferedMSG", field.Error);
    }

    /// <summary>An error is about the value that produced it, so changing the value retracts it.</summary>
    [Fact]
    public void ChangingTheValueClearsTheError()
    {
        var field = Build(Spec(required: true));
        field.Validate(Localize);

        Assert.True(field.HasError);

        field.Value = "hml";

        Assert.False(field.HasError);
    }

    /// <summary>
    /// Every message the operator reads is host copy with the plugin's label substituted in, which
    /// is what keeps plugin text out of the host's three .resx files.
    /// </summary>
    [Fact]
    public void EveryMessageIsAHostStringWithThePluginsLabelInIt()
    {
        var seen = new List<string>();

        var field = Build(Spec(required: true));
        field.Validate(key => { seen.Add(key); return "{0}"; });

        Assert.Equal(["VaultPluginFieldRequiredMSG"], seen);
        Assert.Equal("Environment", field.Error);
    }

    [Fact]
    public void BuildsNothingFromNothing()
    {
        Assert.Empty(PluginFieldState.Build(null, null));
        Assert.Empty(PluginFieldState.Build([], null));
    }
}

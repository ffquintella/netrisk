using System.Collections.Generic;
using System.Globalization;
using Contracts.Ui;
using JetBrains.Annotations;
using Model.Secrets;
using ServerServices.Secrets;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// The boundary between what a plugin declared and what the host is willing to draw.
///
/// Everything on the far side of it was written by somebody else and loaded into this process: a
/// label is third-party text about to appear in an operator's window, a key is about to become part
/// of a string stored in a credential column, and a field count is about to become rows in a
/// dialog. So the properties worth pinning down are the two failure modes — a structural problem
/// drops the declaration whole, an immoderate one is clamped — and the fact that neither can reach
/// the screen.
/// </summary>
[TestSubject(typeof(PluginFieldSpecProjection))]
public class PluginFieldSpecProjectionTest
{
    private static PluginFieldSpec Field(string key = "environment", string label = "Environment") =>
        new() { Key = key, Label = label };

    [Fact]
    public void ProjectsADeclaration()
    {
        var projection = PluginFieldSpecProjection.Project(
            [new PluginFieldSpec
            {
                Key = "environment",
                Label = "Environment",
                Help = "Which environment to read.",
                Kind = PluginFieldKind.Choice,
                Required = true,
                AllowCustomValue = true,
                OptionsDependOnSecret = true
            }], null);

        Assert.Null(projection.Problem);

        var field = Assert.Single(projection.Fields);
        Assert.Equal("environment", field.Key);
        Assert.Equal("Environment", field.Label);
        Assert.Equal(VaultFieldKind.Choice, field.Kind);
        Assert.True(field.Required);
        Assert.True(field.AllowCustomValue);
        Assert.True(field.OptionsDependOnSecret);
    }

    [Fact]
    public void DeclaringNothingIsNotAProblem()
    {
        Assert.Null(PluginFieldSpecProjection.Project(null, null).Problem);
        Assert.Empty(PluginFieldSpecProjection.Project([], null).Fields);
    }

    /// <summary>
    /// A key becomes part of a reference stored in a credential column, so it is a column name in
    /// all but name — and one the reference format has to be able to write back out.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("Environment")]
    [InlineData("_environment")]
    [InlineData("env ironment")]
    [InlineData("env/ironment")]
    [InlineData("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee")]
    public void RejectsAnUnusableKey(string key)
    {
        var projection = PluginFieldSpecProjection.Project([Field(key)], null);

        Assert.NotNull(projection.Problem);
        Assert.Empty(projection.Fields);
    }

    [Fact]
    public void RejectsADuplicateKey()
    {
        var projection = PluginFieldSpecProjection.Project([Field(), Field()], null);

        Assert.Contains("twice", projection.Problem);
    }

    [Fact]
    public void RejectsAFieldWithNoLabel()
    {
        Assert.NotNull(PluginFieldSpecProjection.Project([Field(label: "  ")], null).Problem);
    }

    [Fact]
    public void RejectsAControlKindThisHostCannotRender()
    {
        var projection = PluginFieldSpecProjection.Project(
            [new PluginFieldSpec { Key = "x", Label = "X", Kind = (PluginFieldKind)99 }], null);

        Assert.NotNull(projection.Problem);
    }

    /// <summary>
    /// A screen is a dialog and a form column. A plugin that needs more than this is asking for a
    /// screen of its own, and rendering the first eight of a longer declaration would hide that.
    /// </summary>
    [Fact]
    public void RejectsMoreFieldsThanTheHostWillRender()
    {
        var declared = new List<PluginFieldSpec>();

        for (var i = 0; i <= SecretVaultDefaults.MaxPluginFieldsPerScreen; i++)
            declared.Add(Field($"field{i}", $"Field {i}"));

        Assert.NotNull(PluginFieldSpecProjection.Project(declared, null).Problem);
    }

    /// <summary>
    /// Immoderate rather than malformed: truncated and clamped, because dropping the control would
    /// cost the operator a setting over a long sentence.
    /// </summary>
    [Fact]
    public void TruncatesAndClampsRatherThanRejecting()
    {
        var projection = PluginFieldSpecProjection.Project(
            [new PluginFieldSpec
            {
                Key = "environment",
                Label = new string('a', 500),
                Help = new string('b', 900),
                MaxLength = 1_000_000
            }], null);

        var field = Assert.Single(projection.Fields);

        Assert.Equal(SecretVaultDefaults.MaxFieldLabelLength, field.Label.Length);
        Assert.Equal(SecretVaultDefaults.MaxFieldHelpLength, field.Help!.Length);
        Assert.Equal(SecretVaultDefaults.MaxFieldValueLength, field.MaxLength);
    }

    /// <summary>
    /// A control character in a label is a way to draw outside the control it was declared for.
    /// </summary>
    [Fact]
    public void StripsControlCharactersFromWhatItWillRender()
    {
        var projection = PluginFieldSpecProjection.Project(
            [new PluginFieldSpec { Key = "environment", Label = "Envi\r\nronment\u0007" }], null);

        Assert.Equal("Environment", Assert.Single(projection.Fields).Label);
    }

    /// <summary>
    /// Plugin text does not go through the host's three .resx files — a plugin cannot add a key to
    /// them, and a lookup that missed would render as the key name. So it carries its own.
    /// </summary>
    [Fact]
    public void PrefersTheTranslationForTheCulture()
    {
        var declared = new PluginFieldSpec
        {
            Key = "environment",
            Label = "Environment",
            LabelTranslations = new Dictionary<string, string> { ["pt-BR"] = "Ambiente" }
        };

        Assert.Equal("Ambiente",
            PluginFieldSpecProjection.Project([declared], new CultureInfo("pt-BR")).Fields[0].Label);

        // No translation for the culture is the plugin's own words, not an empty label.
        Assert.Equal("Environment",
            PluginFieldSpecProjection.Project([declared], new CultureInfo("fr-FR")).Fields[0].Label);
    }

    [Fact]
    public void FallsBackFromASpecificCultureToItsLanguage()
    {
        var declared = new PluginFieldSpec
        {
            Key = "environment",
            Label = "Environment",
            LabelTranslations = new Dictionary<string, string> { ["pt"] = "Ambiente" }
        };

        Assert.Equal("Ambiente",
            PluginFieldSpecProjection.Project([declared], new CultureInfo("pt-PT")).Fields[0].Label);
    }

    // --- options ---------------------------------------------------------------------------

    [Fact]
    public void ProjectsOptionsAndDropsTheUnusableOnes()
    {
        var options = PluginFieldSpecProjection.ProjectOptions(
        [
            new PluginFieldOption { Value = "hml", Label = "Homologation" },
            new PluginFieldOption { Value = "   ", Label = "Blank" },
            new PluginFieldOption { Value = "hml", Label = "Duplicate" },
            new PluginFieldOption { Value = "prd", Label = "  " }
        ], null, out var truncated);

        Assert.False(truncated);
        Assert.Equal(["hml", "prd"], options.ConvertAll(o => o.Value));

        // An option with no label reads as its value, which is better than an empty row.
        Assert.Equal("prd", options[1].Label);
    }

    [Fact]
    public void CutsAnOptionListThatIsTooLongRatherThanRefusingIt()
    {
        var declared = new List<PluginFieldOption>();

        for (var i = 0; i < SecretVaultDefaults.MaxFieldOptions + 10; i++)
            declared.Add(new PluginFieldOption { Value = $"env{i}", Label = $"env{i}" });

        var options = PluginFieldSpecProjection.ProjectOptions(declared, null, out var truncated);

        Assert.True(truncated);
        Assert.Equal(SecretVaultDefaults.MaxFieldOptions, options.Count);
    }

    // --- validation ------------------------------------------------------------------------

    private static List<VaultFieldSpecView> Specs(params VaultFieldSpecView[] specs) => [.. specs];

    [Fact]
    public void AcceptsValuesThatMatchTheDeclaration()
    {
        var specs = Specs(new VaultFieldSpecView { Key = "environment", Label = "Environment" });

        Assert.Null(PluginFieldSpecProjection.Validate(specs,
            new Dictionary<string, string> { ["environment"] = "hml" }));
    }

    [Fact]
    public void ReportsAMissingRequiredValue()
    {
        var specs = Specs(new VaultFieldSpecView
        {
            Key = "environment", Label = "Environment", Required = true
        });

        Assert.Contains("Environment", PluginFieldSpecProjection.Validate(specs, null));
        Assert.Null(PluginFieldSpecProjection.Validate(
            Specs(new VaultFieldSpecView { Key = "environment", Label = "Environment" }), null));
    }

    [Theory]
    [InlineData(VaultFieldKind.Toggle, VaultFieldFormat.Any, "maybe")]
    [InlineData(VaultFieldKind.Number, VaultFieldFormat.Any, "twelve")]
    [InlineData(VaultFieldKind.Text, VaultFieldFormat.NoWhitespace, "two words")]
    [InlineData(VaultFieldKind.Text, VaultFieldFormat.Identifier, "teams/netrisk")]
    public void ReportsAValueTheDeclarationDoesNotAllow(VaultFieldKind kind, VaultFieldFormat format,
        string value)
    {
        var specs = Specs(new VaultFieldSpecView
        {
            Key = "field", Label = "Field", Kind = kind, Format = format
        });

        Assert.NotNull(PluginFieldSpecProjection.Validate(specs,
            new Dictionary<string, string> { ["field"] = value }));
    }

    [Fact]
    public void ReportsAValueLongerThanDeclared()
    {
        var specs = Specs(new VaultFieldSpecView { Key = "field", Label = "Field", MaxLength = 4 });

        Assert.NotNull(PluginFieldSpecProjection.Validate(specs,
            new Dictionary<string, string> { ["field"] = "12345" }));
    }

    /// <summary>
    /// A reference stored while the plugin declared a field that a later version dropped must keep
    /// resolving: the dictionary belongs to the plugin, and the plugin decides an unknown key means
    /// nothing.
    /// </summary>
    [Fact]
    public void LeavesAValueWithNoDeclarationAlone()
    {
        var specs = Specs(new VaultFieldSpecView { Key = "environment", Label = "Environment" });

        Assert.Null(PluginFieldSpecProjection.Validate(specs,
            new Dictionary<string, string> { ["retired"] = "whatever" }));
    }
}

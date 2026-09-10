using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// Covers <see cref="UiStandardLinter"/> — the rule engine behind the <c>LintUi</c> Nuke target.
/// The rules are stated in docs/ui-standard.md; the tests here pin each one, both directions
/// (flags a breach / stays quiet on the compliant form).
/// </summary>
public class UiStandardLinterTest
{
    private const string Path = "src/GUIClient/Views/Sample.axaml";

    private static IReadOnlyList<UiStandardViolation> Lint(string body) =>
        UiStandardLinter.Lint(Path, $"""
            <UserControl xmlns="https://github.com/avaloniaui">
            {body}
            </UserControl>
            """);

    private static IReadOnlyList<UiStandardViolation> Rule(string body, string rule) =>
        Lint(body).Where(v => v.Rule == rule).ToList();

    // -----------------------------------------------------------------------------------
    // R6 — unclassed buttons, and the multi-line regression that motivated the rewrite.
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Regression: the previous line-by-line linter matched <c>&lt;Button</c> and
    /// <c>Classes="…"</c> on the *same* line, so this — the dominant formatting in the
    /// codebase — was reported as an R6 breach. It is compliant and must not be flagged.
    /// </summary>
    [Fact]
    public void R6_DoesNotFlag_ButtonWhoseClassesAttributeIsOnAFollowingLine()
    {
        var violations = Rule("""
            <Button Name="BtSave"
                    Classes="dialog1"
                    Command="{Binding BtSaveClicked}">
                <TextBlock Text="{Binding StrSave}"/>
            </Button>
            """, "R6");

        Assert.Empty(violations);
    }

    [Fact]
    public void R6_DoesNotFlag_ClassesSeveralLinesBelowWithAnInterveningBinding()
    {
        var violations = Rule("""
            <Button Name="BtCancel"
                    Margin="5 0"
                    IsCancel="True"
                    ToolTip.Tip="{Binding ValidationContext.Text}"
                    Classes="dialog2"
                    Command="{Binding BtCancelClicked}"/>
            """, "R6");

        Assert.Empty(violations);
    }

    [Fact]
    public void R6_Flags_ButtonWithoutAnyClassAttribute()
    {
        var violations = Rule("""
            <Button Content="Manage Templates"
                    Command="{Binding ManageTemplatesCommand}"/>
            """, "R6");

        var violation = Assert.Single(violations);
        Assert.Equal(2, violation.Line);
        Assert.Equal(Path, violation.File);
        Assert.Contains("Unclassed Button", violation.Message);
    }

    [Fact]
    public void R6_ReportsTheLineTheButtonOpensOn_NotTheLineOfItsLastAttribute()
    {
        var violations = Rule("""
            <StackPanel>
                <Button Name="BtGenerate"
                        Margin="0 5 10 0"
                        Command="{Binding BtGenerateClicked}"/>
            </StackPanel>
            """, "R6");

        var violation = Assert.Single(violations);
        Assert.Equal(3, violation.Line);
    }

    [Fact]
    public void R6_IgnoresPropertyElements()
    {
        // <Button.Styles> is a property element, not a control.
        var violations = Rule("""
            <Button Classes="operation">
                <Button.Styles>
                    <Style Selector="Button"/>
                </Button.Styles>
            </Button>
            """, "R6");

        Assert.Empty(violations);
    }

    [Fact]
    public void R6_IgnoresClosingTags()
    {
        Assert.Empty(Rule("<Button Classes=\"dialog1\">x</Button>", "R6"));
    }

    [Fact]
    public void R6_AcceptsAnExplicitControlTheme()
    {
        // A Theme= is an equally explicit opt-in to a defined visual, so it satisfies §4.1.
        Assert.Empty(Rule("<Button Theme=\"{StaticResource TransparentButton}\"/>", "R6"));
    }

    // -----------------------------------------------------------------------------------
    // R1 — hard-coded hex colors (§2.6).
    // -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Background")]
    [InlineData("Foreground")]
    [InlineData("BorderBrush")]
    public void R1_Flags_HexLiteralInAnyBrushAttribute(string attribute)
    {
        var violations = Rule($"<Border {attribute}=\"#B7791F\"/>", "R1");

        var violation = Assert.Single(violations);
        Assert.Contains($"{attribute}=\"#B7791F\"", violation.Message);
    }

    [Fact]
    public void R1_Flags_EightDigitHexWithAlpha()
    {
        Assert.Single(Rule("<Border Background=\"#3370A0FF\"/>", "R1"));
    }

    [Fact]
    public void R1_Flags_AHexAttributeThatIsWrappedOntoItsOwnLine()
    {
        var violations = Rule("""
            <Border Margin="4"
                    Padding="6"
                    Background="#282928"/>
            """, "R1");

        var violation = Assert.Single(violations);
        Assert.Equal(4, violation.Line);
    }

    [Fact]
    public void R1_DoesNotFlag_ThemeResourceOrClassReferences()
    {
        Assert.Empty(Rule("""
            <Border Classes="panel"
                    Background="{DynamicResource SurfacePanel1}"/>
            """, "R1"));
    }

    // -----------------------------------------------------------------------------------
    // R4 — named Avalonia brushes as status colors (§2.6).
    // -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Green")]
    [InlineData("Red")]
    [InlineData("Orange")]
    [InlineData("Azure")]
    public void R4_Flags_ForbiddenNamedBrush(string brush)
    {
        var violations = Rule($"<TextBlock Foreground=\"{brush}\"/>", "R4");

        var violation = Assert.Single(violations);
        Assert.Contains(brush, violation.Message);
    }

    [Fact]
    public void R4_DoesNotFlag_TheNeutralBrushesTheStandardStillAllows()
    {
        // SlateBlue / DarkCyan / DarkSlateBlue are documented accent tokens (§2.4).
        Assert.Empty(Rule("<Button Classes=\"type2\" Background=\"SlateBlue\"/>", "R4"));
        Assert.Empty(Rule("<Border Background=\"Transparent\"/>", "R4"));
    }

    // -----------------------------------------------------------------------------------
    // R5 — user-facing copy must come from a Str* resource binding (§3.2).
    // -----------------------------------------------------------------------------------

    [Fact]
    public void R5_Flags_LiteralTextBlockText()
    {
        var violations = Rule("<TextBlock Text=\"FQDN:\"/>", "R5");

        Assert.Contains("FQDN:", Assert.Single(violations).Message);
    }

    [Fact]
    public void R5_Flags_LiteralButtonContent()
    {
        Assert.Single(Rule("<Button Classes=\"dialog1\" Content=\"Save\"/>", "R5"));
    }

    [Fact]
    public void R5_Flags_LiteralWindowTitle()
    {
        var violations = UiStandardLinter
            .Lint(Path, """
                <Window xmlns="https://github.com/avaloniaui"
                        Title="ReportsWindow">
                </Window>
                """)
            .Where(v => v.Rule == "R5")
            .ToList();

        Assert.Equal(2, Assert.Single(violations).Line);
    }

    [Fact]
    public void R5_Flags_LiteralToolTipAndHeaderAndWatermark()
    {
        Assert.Single(Rule("<Button Classes=\"operation\" ToolTip.Tip=\"Export\"/>", "R5"));
        Assert.Single(Rule("<TabItem Header=\"IRP - Create\"/>", "R5"));
        Assert.Single(Rule("<NativeMenuItem Header=\"IRP - Create\"/>", "R5"));
        Assert.Single(Rule("<TextBox Watermark=\"Search hosts\"/>", "R5"));
    }

    [Fact]
    public void R5_DoesNotFlag_ResourceBindings()
    {
        Assert.Empty(Rule("<TextBlock Text=\"{Binding StrFqdn}\"/>", "R5"));
        Assert.Empty(Rule("<TextBlock Text=\"{DynamicResource Foo}\"/>", "R5"));
        Assert.Empty(Rule("<TextBlock Text=\"{CompiledBinding Name}\"/>", "R5"));
    }

    [Fact]
    public void R5_DoesNotFlag_NonCopyValues()
    {
        // Symbols, numbers and single letters are not translatable copy.
        Assert.Empty(Rule("<TextBlock Text=\"-\"/>", "R5"));
        Assert.Empty(Rule("<TextBlock Text=\"0.00\"/>", "R5"));
        Assert.Empty(Rule("<TextBlock Text=\"*\"/>", "R5"));
    }

    [Fact]
    public void R5_DoesNotFlag_TextOnAnElementThatIsNotUserFacingCopy()
    {
        // Text= on a TextBox is a two-way data value, not a label.
        Assert.Empty(Rule("<TextBox Text=\"placeholder value\"/>", "R5"));
    }

    // -----------------------------------------------------------------------------------
    // Waivers.
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Waiver_SuppressesTheNamedRuleOnTheFollowingElement()
    {
        var violations = Lint("""
            <!-- ui-lint-waive R6: chart pager is templated by the plot control -->
            <Button Command="{Binding NextPage}"/>
            """);

        Assert.Empty(violations);
    }

    [Fact]
    public void Waiver_SuppressesOnlyTheRulesItNames()
    {
        var violations = Lint("""
            <!-- ui-lint-waive R6: legacy toolbar -->
            <Button Content="Save"/>
            """);

        var violation = Assert.Single(violations);
        Assert.Equal("R5", violation.Rule);
    }

    [Fact]
    public void Waiver_AcceptsSeveralRuleIds()
    {
        Assert.Empty(Lint("""
            <!-- ui-lint-waive R5, R6: vendor sample markup kept verbatim -->
            <Button Content="Save"/>
            """));
    }

    [Fact]
    public void Waiver_AppliesToOneElementOnly()
    {
        var violations = Lint("""
            <!-- ui-lint-waive R6: first button only -->
            <Button Command="{Binding A}"/>
            <Button Command="{Binding B}"/>
            """);

        var violation = Assert.Single(violations);
        Assert.Equal("R6", violation.Rule);
        Assert.Equal(4, violation.Line);
    }

    [Fact]
    public void Waiver_WithoutAReason_IsItselfAViolation()
    {
        var violations = Lint("""
            <!-- ui-lint-waive R6 -->
            <Button Command="{Binding A}"/>
            """);

        // The waiver is rejected, so it suppresses nothing: R0 for the bare waiver plus the
        // original R6.
        Assert.Equal(new[] { "R0", "R6" }, violations.Select(v => v.Rule).Order().ToArray());
        Assert.Contains("carries no reason", violations.Single(v => v.Rule == "R0").Message);
    }

    [Fact]
    public void Waiver_WithAnEmptyReason_IsItselfAViolation()
    {
        var violations = Lint("""
            <!-- ui-lint-waive R6:    -->
            <Button Command="{Binding A}"/>
            """);

        Assert.Contains(violations, v => v.Rule == "R0");
        Assert.Contains(violations, v => v.Rule == "R6");
    }

    [Fact]
    public void OrdinaryComments_AreNotWaivers()
    {
        var violations = Lint("""
            <!-- action row -->
            <Button Command="{Binding A}"/>
            """);

        Assert.Equal("R6", Assert.Single(violations).Rule);
    }

    [Fact]
    public void Comments_DoNotHideMarkupThatFollowsThem()
    {
        var violations = Lint("""
            <!-- <Button Content="Save"/> -->
            <Button Classes="dialog1" Content="Cancel"/>
            """);

        // The commented-out button is invisible; the live one still trips R5.
        var violation = Assert.Single(violations);
        Assert.Equal("R5", violation.Rule);
        Assert.Contains("Cancel", violation.Message);
    }

    // -----------------------------------------------------------------------------------
    // Scanner robustness.
    // -----------------------------------------------------------------------------------

    [Fact]
    public void Scanner_IgnoresAngleBracketsInsideAttributeValues()
    {
        var violations = Lint("""
            <TextBlock Text="{Binding Count, StringFormat='&gt; {0}'}"/>
            <Button Command="{Binding A}"/>
            """);

        Assert.Equal("R6", Assert.Single(violations).Rule);
    }

    [Fact]
    public void Scanner_HandlesNamespacePrefixedButtons()
    {
        Assert.Single(Rule("<controls:Button Command=\"{Binding A}\"/>", "R6"));
    }

    [Fact]
    public void Lint_EmptyDocument_ReportsNothing()
    {
        Assert.Empty(UiStandardLinter.Lint(Path, string.Empty));
    }

    [Fact]
    public void Lint_OrdersFindingsByLine()
    {
        var violations = Lint("""
            <Button Command="{Binding A}"/>
            <Border Background="#282928"/>
            """);

        Assert.Equal(new[] { 2, 3 }, violations.Select(v => v.Line).ToArray());
    }

    [Fact]
    public void CountByRule_SummarisesFindings()
    {
        var counts = UiStandardLinter.CountByRule(Lint("""
            <Button Content="Save"/>
            <Border Background="#282928"/>
            """));

        Assert.Equal(1, counts["R1"]);
        Assert.Equal(1, counts["R5"]);
        Assert.Equal(1, counts["R6"]);
        Assert.Equal("R1=1, R5=1, R6=1", UiStandardLinter.DescribeCounts(Lint("""
            <Button Content="Save"/>
            <Border Background="#282928"/>
            """)));
    }

    // -----------------------------------------------------------------------------------
    // The gate itself: the real views must be clean. This is the same evidence the
    // `LintUi` target produces, asserted from `dotnet test`.
    // -----------------------------------------------------------------------------------

    [Fact]
    public void EveryGuiClientView_IsCompliantWithTheUiStandard()
    {
        var violations = UiStandardLinter.Lint(GuiClientViews()
            .Select(f => (RelativePath(f), File.ReadAllText(f))));

        Assert.True(
            violations.Count == 0,
            "src/GUIClient/Views is expected to be free of UI standard violations. Found:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(v => v.ToString())));
    }

    [Fact]
    public void TheViewTreeIsActuallyBeingScanned()
    {
        // Guards the test above from passing because it found no files.
        Assert.True(GuiClientViews().Count > 50);
    }

    private static IReadOnlyList<string> GuiClientViews() =>
        Directory.GetFiles(
                System.IO.Path.Combine(RepositoryPaths.RepositoryRoot, "src", "GUIClient", "Views"),
                "*.axaml",
                SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

    private static string RelativePath(string absolute) =>
        System.IO.Path.GetRelativePath(RepositoryPaths.RepositoryRoot, absolute)
            .Replace('\\', '/');
}

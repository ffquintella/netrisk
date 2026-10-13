using System.Globalization;
using Avalonia;
using Avalonia.Media;
using GUIClient.Converters;
using GUIClient.UiTests.Infrastructure;
using Xunit;

namespace GUIClient.UiTests.Converters;

/// <summary>
/// T324 — <see cref="StatusToColourConverter"/> colours the risk status on <c>RiskView</c>. With no risk selected it
/// returned <c>""</c> for a <c>TextBlock.Foreground</c> (an <see cref="IBrush"/>), so every start-up and every cleared
/// selection logged a failed conversion; an empty status threw. "No status" now leaves the foreground to the theme.
///
/// Here rather than in GUIClient.Tests because the converter's output is an Avalonia brush; like every test in this
/// project it runs only on demand or at release (S54 §3). The symptom on the real view — the compiled binding in
/// <c>RiskView.axaml</c> logging the failed conversion — is covered by <c>ViewSmokeTest.TestMainWindowShellLoadsAndNavigates</c>,
/// which tolerated it as a known defect until this fix.
/// </summary>
public class StatusToColourConverterTest
{
    private static readonly StatusToColourConverter Converter = new();

    [UiTheory]
    [InlineData(null)]
    [InlineData("")]
    public void TestNoStatusLeavesTheForegroundToTheTheme(string? status)
    {
        var result = Converter.Convert(status, typeof(IBrush), null, CultureInfo.InvariantCulture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }

    [UiTheory]
    [InlineData("New", "#FF90EE90")]
    [InlineData("Management Review", "#FFFFA500")]
    // T329: the status the register actually stores for a reviewed risk; it used to fall through to the Closed colour.
    [InlineData("Mgmt Reviewed", "#FFFFA500")]
    [InlineData("Mitigation Planned", "#FFFFA07A")]
    [InlineData("Closed", "#FF40E0D0")]
    public void TestAStatusKeepsItsColour(string status, string expected)
    {
        var brush = Assert.IsType<SolidColorBrush>(
            Converter.Convert(status, typeof(IBrush), null, CultureInfo.InvariantCulture));

        Assert.Equal(Color.Parse(expected), brush.Color);
    }
}

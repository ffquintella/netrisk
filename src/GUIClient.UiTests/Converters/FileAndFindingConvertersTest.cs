using System;
using System.Globalization;
using Avalonia;
using GUIClient.Converters;
using GUIClient.UiTests.Infrastructure;
using Material.Icons;
using Xunit;

namespace GUIClient.UiTests.Converters;

/// <summary>
/// Two converters of the risk screen's detail, at the type level. Here rather than in GUIClient.Tests because their
/// results are Avalonia values; the on-screen symptoms are covered by <c>Flows/Risks/RiskLifecycleFlowTest</c> (file
/// icons) and <c>Flows/Risks/RiskDetailFlowTest</c> (an unassigned finding). Like every test in this project they run
/// only on demand or at release (S54 §3).
/// </summary>
public class FileAndFindingConvertersTest
{
    /// <summary>
    /// T333 — <c>MaterialIcon.Kind</c> is a <c>MaterialIconKind?</c>, and <see cref="TypeToIconConverter"/> tested
    /// <c>IsAssignableTo(typeof(MaterialIconKind))</c>, so it answered every file list with a failed binding and no icon.
    /// </summary>
    [UiTheory]
    [InlineData(typeof(MaterialIconKind?))]
    [InlineData(typeof(MaterialIconKind))]
    [InlineData(typeof(object))]
    public void TestAFileTypeGetsItsIconOnEveryIconTarget(Type target)
    {
        var converter = new TypeToIconConverter();

        Assert.Equal(MaterialIconKind.FilePdfBox, converter.Convert("application/pdf", target, null, CultureInfo.InvariantCulture));
        Assert.Equal(MaterialIconKind.Image, converter.Convert("image/png", target, null, CultureInfo.InvariantCulture));
    }

    /// <summary>T331 — a finding nobody has been assigned has no analyst: an empty cell, not an exception.</summary>
    [UiFact]
    public void TestAFindingWithNoAnalystLeavesTheCellEmpty()
    {
        var result = new AnalystIdToAnalystNameConverter().Convert(null, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Same(AvaloniaProperty.UnsetValue, result);
    }
}

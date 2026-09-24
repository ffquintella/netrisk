using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace GUIClient.Tests.Views;

/// <summary>
/// The vulnerability register's alternating row background.
///
/// This one styling decision is spread over three files that nothing else ties together, which is
/// why it needs a test of its own. <see cref="StyleClassReferenceTest"/> catches a class a view
/// names and no style defines, but the banding class is never written in a view: the rows are
/// virtualized and <c>TreeDataGridRow.RowIndex</c> is a plain CLR property, so there is no
/// selector and no binding that can express "odd row", and the code-behind toggles the class in
/// <c>TreeDataGrid.RowPrepared</c> instead. Renaming the class in either place, or dropping the
/// subscription, leaves a grid that simply is not banded — nothing throws and nothing fails to
/// compile.
///
/// Source-text scanning, for the same reason as the rest of this folder: GUIClient.Tests does not
/// reference GUIClient, because that would pull all of Avalonia into a headless run.
/// </summary>
public class VulnerabilitiesGridBandingTests
{
    private const string BandingToken = "NrSurfaceRowAlternate";

    [Fact]
    public void TheBandingTokenIsDefinedInTheTokenSheet()
    {
        Assert.Contains($"x:Key=\"{BandingToken}\"", File.ReadAllText(TokenSheet()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRowStyleUsesTheBandingTokenAndNamesTheClassTheCodeBehindApplies()
    {
        var styles = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), "Styles", "WindowStyles.axaml"));

        var style = Regex.Match(styles,
            @"<Style\s+Selector=""TreeDataGridRow\.(?<class>[A-Za-z0-9_\-]+)""\s*>(?<body>.*?)</Style>",
            RegexOptions.Singleline);

        Assert.True(style.Success,
            "No style targets TreeDataGridRow with a class. The vulnerability register's row " +
            "banding is applied by that class and renders as nothing without it.");

        Assert.Contains($"{{DynamicResource {BandingToken}}}", style.Groups["body"].Value, StringComparison.Ordinal);

        var codeBehind = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), "Views", "VulnerabilitiesView.axaml.cs"));

        Assert.Contains($"AlternateRowClass = \"{style.Groups["class"].Value}\"", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void TheViewSubscribesToRowPreparedAndBandsByRowIndex()
    {
        var codeBehind = File.ReadAllText(Path.Combine(GuiClientSourceRoot(), "Views", "VulnerabilitiesView.axaml.cs"));

        Assert.Contains("RowPrepared += ", codeBehind, StringComparison.Ordinal);

        // The parity test is the whole behaviour: banding every row, or banding by anything other
        // than the index the row is being realized at, is the failure this guards.
        Assert.Matches(@"Classes\.Set\(AlternateRowClass,\s*e\.RowIndex\s*%\s*2\s*==\s*1\)", codeBehind);
    }

    private static string TokenSheet() => Path.Combine(GuiClientSourceRoot(), "Styles", "Tokens.axaml");

    private static string GuiClientSourceRoot() => Path.Combine(RepositoryRoot(), "src", "GUIClient");

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "netrisk.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not find src/netrisk.sln walking up from {AppContext.BaseDirectory}.");
    }
}

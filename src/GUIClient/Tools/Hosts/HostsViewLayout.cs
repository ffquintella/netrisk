using System;
using System.Globalization;

namespace GUIClient.Tools.Hosts;

/// <summary>
/// The two pieces of Hosts-view layout remembered per user (S38 §3.1, §3.3): the host list's width
/// and the detail tab last open. Stored as strings in the client's mutable configuration, so both
/// directions are here — a value written by an older build, or edited by hand, must come back as
/// something the layout can use rather than as an exception or an off-screen pane.
/// </summary>
public static class HostsViewLayout
{
    public const string LeftPaneWidthKey = "hostsView.leftPaneWidth";
    public const string SelectedTabKey = "hostsView.selectedTab";

    /// <summary>The list pane's bounds; the view's ColumnDefinition carries the same two numbers.</summary>
    public const double MinLeftPaneWidth = 220;
    public const double MaxLeftPaneWidth = 520;
    public const double DefaultLeftPaneWidth = 280;

    /// <summary>Vulnerabilities, Overview, Services, History, Comments — in that order.</summary>
    public const int TabCount = 5;

    public static double ClampWidth(double width) =>
        double.IsFinite(width) ? Math.Clamp(width, MinLeftPaneWidth, MaxLeftPaneWidth) : DefaultLeftPaneWidth;

    public static double ParseWidth(string? stored) =>
        double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
            ? ClampWidth(width)
            : DefaultLeftPaneWidth;

    /// <summary>Whole pixels, invariant culture, so a pt-BR decimal comma never reaches the file.</summary>
    public static string FormatWidth(double width) =>
        Math.Round(ClampWidth(width)).ToString(CultureInfo.InvariantCulture);

    /// <summary>The stored tab index, or the first tab (Vulnerabilities) when it is missing or out of range.</summary>
    public static int ParseTab(string? stored) =>
        int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
        && index is >= 0 and < TabCount
            ? index
            : 0;
}

using System.Linq;

namespace GUIClient.Tools.Hosts;

/// <summary>
/// The header card's muted lines (S38 §3.2): <c>IP · FQDN · OS</c> and <c>Team · Owner · Risk</c>.
/// A blank part is left out rather than shown as a dangling separator, so a host with no FQDN reads
/// <c>10.0.0.1 · Windows</c>, not <c>10.0.0.1 ·  · Windows</c>.
/// </summary>
public static class HostSummaryLine
{
    public const string Separator = " · ";

    public static string Join(params string?[] parts) =>
        string.Join(Separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary><c>Label: value</c>, or null when there is no value, so <see cref="Join"/> drops it.</summary>
    public static string? Labelled(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : label + ": " + value.Trim();
}

using GUIClient.Models;

namespace GUIClient.Navigation;

/// <summary>
/// A record to open in a shell module (S40 §10.3): the GUI's only navigation destination type. <see cref="SubView"/> narrows
/// the module — in Vulnerabilities, <see cref="HostSubView"/> or <see cref="EntitySubView"/> picks the filter applied.
/// </summary>
public sealed record NavigationTarget(AvaliableViews View, int RecordId, string? SubView = null)
{
    public const string HostSubView = "host";
    public const string EntitySubView = "entity";
}

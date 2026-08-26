namespace GUIClient.ViewModels.Reports;

/// <summary>
/// One entry in the evidence pack's entity picker (Track 8 milestone 8.4.2). A null
/// <see cref="EntityId"/> is the "all entities" entry, which is a wider disclosure than any single
/// entity and is why the endpoint behind it is admin-only.
/// </summary>
public class ReportEntityOption
{
    public string Name { get; init; } = "";

    public int? EntityId { get; init; }

    public override string ToString() => Name;
}

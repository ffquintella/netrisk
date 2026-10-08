namespace GUIClient.Tools.Track9;

/// <summary>
/// Pure routing rules shared by the governance hosts. Keeping these decisions outside the Avalonia
/// view-models makes the selected scope and lookup targets deterministic and headlessly testable.
/// </summary>
public static class Track9GovernanceRouting
{
    public static int? AppetiteIdForEditor(
        int? selectedAppetiteId,
        int? selectedAppetiteEntityId,
        int? editorEntityId) =>
        selectedAppetiteId is not null && selectedAppetiteEntityId == editorEntityId
            ? selectedAppetiteId
            : null;

    public static bool TryCreateTaskRoute(
        int? selectedMitigationId,
        int? selectedOwnerId,
        out MitigationTaskRoute route)
    {
        if (selectedMitigationId is not > 0)
        {
            route = default;
            return false;
        }

        route = new MitigationTaskRoute(selectedMitigationId.Value, selectedOwnerId);
        return true;
    }
}

public readonly record struct MitigationTaskRoute(int MitigationId, int? OwnerId);

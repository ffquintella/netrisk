using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Parameters;

public class EntityDialogParameter: NavigationParameterBase
{
    public string? Title { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public Entity? Parent { get; set; }
}
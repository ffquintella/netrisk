using DAL.Entities;

namespace GUIClient.ViewModels.Dialogs.Results;

public class EntityDialogResult: IntegerDialogResult
{
    public string Name { get; set; }
    public string Type { get; set; }
    public Entity? Parent { get; set; }
}
namespace GUIClient.ViewModels.Dialogs.Parameters;

public class StringDialogParameter: NavigationParameterBase
{
    public string? Title { get; set; }
    public string? FieldName { get; set; }
    public string? Value { get; set; }
}
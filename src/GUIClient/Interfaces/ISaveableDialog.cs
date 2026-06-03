using System.Windows.Input;

namespace GUIClient.Interfaces;

/// <summary>
/// Implemented by dialog view-models that expose a primary "save/commit" action.
/// <see cref="GUIClient.ViewModels.Dialogs.DialogWindowBase{TResult}"/> uses this to wire
/// the <c>Ctrl/Cmd+S</c> keyboard accelerator to the dialog's save command without each
/// dialog having to declare the binding in XAML.
/// </summary>
public interface ISaveableDialog
{
    /// <summary>The command invoked when the user presses the save accelerator. May be null when saving is unavailable.</summary>
    ICommand? SaveCommand { get; }
}

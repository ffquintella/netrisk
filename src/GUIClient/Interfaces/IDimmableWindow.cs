namespace GUIClient.Interfaces;

/// <summary>
/// Implemented by windows that can dim themselves behind a modal child. Only windows that
/// actually own a dim overlay implement this, which is what lets <c>DialogService</c> dim the
/// real launching window instead of always dimming MainWindow (IX-1).
/// </summary>
public interface IDimmableWindow
{
    void ShowOverlay();

    void HideOverlay();
}

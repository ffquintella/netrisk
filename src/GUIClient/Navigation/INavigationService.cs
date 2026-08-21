using Avalonia.Controls;
using GUIClient.Models;

namespace GUIClient.Navigation;

/// <summary>
/// The single route into the shell's view stack and its auxiliary windows (IX-7).
///
/// Before this, view-models navigated by casting a <c>Window</c> handed to them through a
/// <c>CommandParameter</c> that walked the visual tree, or by grepping a global window list.
/// Both are forbidden: a view-model asks the service, and the service knows the shell.
/// </summary>
public interface INavigationService
{
    /// <summary>Switches the shell to <paramref name="view"/>.</summary>
    void NavigateTo(AvaliableViews view);

    /// <summary>
    /// Shows an auxiliary window (IX-1: modeless, parented to MainWindow, singleton). If one is
    /// already open it is activated rather than duplicated.
    /// </summary>
    /// <param name="dataContextFactory">Builds the view-model, only when a new window is needed.</param>
    void ShowAuxiliaryWindow<TWindow>(System.Func<object> dataContextFactory)
        where TWindow : Window, new();

    /// <summary>Shows a modal window owned by the active window.</summary>
    void ShowModalWindow<TWindow>(System.Func<object> dataContextFactory)
        where TWindow : Window, new();
}

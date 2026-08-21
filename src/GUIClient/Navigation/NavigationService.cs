using System;
using System.Collections.Generic;
using Avalonia.Controls;
using GUIClient.Models;
using GUIClient.Tools;
using GUIClient.ViewModels;

namespace GUIClient.Navigation;

/// <inheritdoc cref="INavigationService" />
public sealed class NavigationService : INavigationService
{
    private readonly IMainWindowProvider _windowProvider;

    /// <summary>
    /// The one live instance of each auxiliary window type. IX-1 requires auxiliary windows to be
    /// singletons; Reports and Notifications previously opened a fresh unparented window per click.
    /// </summary>
    private readonly Dictionary<Type, Window> _auxiliaryWindows = new();

    public NavigationService(IMainWindowProvider windowProvider)
    {
        _windowProvider = windowProvider;
    }

    public void NavigateTo(AvaliableViews view)
    {
        if (_windowProvider.GetMainWindow().DataContext is MainWindowViewModel shell)
        {
            shell.NavigateTo(view);
        }
    }

    public void ShowAuxiliaryWindow<TWindow>(Func<object> dataContextFactory)
        where TWindow : Window, new()
    {
        if (_auxiliaryWindows.TryGetValue(typeof(TWindow), out var existing))
        {
            existing.Activate();
            return;
        }

        var window = new TWindow { DataContext = dataContextFactory() };

        _auxiliaryWindows[typeof(TWindow)] = window;
        window.Closed += (_, _) => _auxiliaryWindows.Remove(typeof(TWindow));

        // Parented, so it stays with the shell and inherits its position on screen.
        window.Show(_windowProvider.GetMainWindow());
    }

    public void ShowModalWindow<TWindow>(Func<object> dataContextFactory)
        where TWindow : Window, new()
    {
        var window = new TWindow
        {
            DataContext = dataContextFactory(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        window.ShowDialog(_windowProvider.GetActiveWindow());
    }
}

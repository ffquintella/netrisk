using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using GUIClient.Models;
using GUIClient.Notifications;
using GUIClient.Tools;
using GUIClient.Tools.Navigation;
using GUIClient.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GUIClient.Navigation;

/// <inheritdoc cref="INavigationService" />
public sealed class NavigationService : INavigationService
{
    private readonly IMainWindowProvider _windowProvider;
    private readonly IServiceProvider? _services;

    /// <summary>
    /// The one live instance of each auxiliary window type. IX-1 requires auxiliary windows to be
    /// singletons; Reports and Notifications previously opened a fresh unparented window per click.
    /// </summary>
    private readonly Dictionary<Type, Window> _auxiliaryWindows = new();

    /// <param name="services">Where the record navigation finds the user, the modules and the toasts; the app's by default.</param>
    public NavigationService(IMainWindowProvider windowProvider, IServiceProvider? services = null)
    {
        _windowProvider = windowProvider;
        _services = services;
    }

    private IServiceProvider Services => _services ?? Program.ServiceProvider;

    public void NavigateTo(AvaliableViews view)
    {
        if (_windowProvider.GetMainWindow().DataContext is MainWindowViewModel shell)
        {
            shell.NavigateTo(view);
        }
    }

    public void NavigateTo(NavigationTarget target) => _ = NavigateToAsync(target);

    /// <summary>
    /// S40 §10.3: the navigation bar's rule decides (defence in depth behind the disabled button); the shell shows the
    /// module and the module — found through <see cref="SelectableModules"/>, never the visual tree (IX-7) — selects the
    /// record. Incidents behind face verification are refused: only the navigation bar runs that check.
    /// </summary>
    private async Task NavigateToAsync(NavigationTarget target)
    {
        try
        {
            var user = Services.GetRequiredService<IAuthenticationService>().AuthenticatedUserInfo;
            var faceId = target.View == AvaliableViews.Incidents &&
                         await Services.GetRequiredService<PluginManager>().IsFaceIdEnabledAsync();
            var toasts = Services.GetRequiredService<INotificationService>();
            switch (RecordNavigation.Decide(target, user, faceId))
            {
                case RecordNavigationDecision.NotAllowed:
                    Log.Warning("Refusing to open {View} record {Id}: the module is not allowed", target.View, target.RecordId);
                    toasts.Warning(string.Format(CultureInfo.CurrentCulture, ViewModelBase.Localizer[RecordNavigation.NotAllowedKey],
                        ViewModelBase.Localizer[ModuleAccess.NameKey(target.View)].Value));
                    return;
                case RecordNavigationDecision.RequiresFaceVerification:
                    toasts.Warning(ViewModelBase.Localizer[RecordNavigation.FaceVerificationKey]);
                    return;
            }

            NavigateTo(target.View);
            if (Services.GetRequiredService<SelectableModules>().Find(target.View) is { } module)
                await module.SelectRecordAsync(target.RecordId, target.SubView);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not open {View} record {Id}", target.View, target.RecordId);
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

using System;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using ClientServices.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GUIClient.Helpers;

/// <summary>
/// Owns the application's system-tray presence – the Windows notification-area icon and
/// the macOS menu-bar extra (both surfaced through Avalonia's cross-platform
/// <see cref="TrayIcon"/>). Provides a quick-status context menu and, on Windows,
/// minimise-to-tray behaviour.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly Application _app;
    private readonly Window _mainWindow;
    private readonly NativeMenuItem _statusItem;
    private readonly NativeMenuItem _toggleItem;
    private TrayIcon? _trayIcon;
    private Timer? _statusTimer;
    private bool _disposed;

    public TrayIconManager(Application app, Window mainWindow)
    {
        _app = app;
        _mainWindow = mainWindow;
        _statusItem = new NativeMenuItem { Header = "NetRisk", IsEnabled = false };
        _toggleItem = new NativeMenuItem { Header = "Hide to Tray" };
    }

    public void Initialize()
    {
        try
        {
            _trayIcon = new TrayIcon
            {
                Icon = LoadIcon(),
                ToolTipText = "NetRisk",
                IsVisible = true,
                Menu = BuildMenu()
            };

            // Single click / activation restores the main window.
            _trayIcon.Clicked += (_, _) => ShowMainWindow();

            TrayIcon.SetIcons(_app, new TrayIcons { _trayIcon });

            // Windows: collapse to the tray when the window is minimised.
            if (PlatformInfo.IsWindows)
            {
                _mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
            }

            // Refresh the quick-status preview every 15s (cheap, local reads only).
            _statusTimer = new Timer(_ => RefreshStatus(), null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
        }
        catch (Exception ex)
        {
            // The tray is a nicety; never let it take the app down (e.g. headless Linux).
            Log.Warning(ex, "System tray initialisation failed; continuing without a tray icon.");
        }
    }

    private NativeMenu BuildMenu()
    {
        var open = new NativeMenuItem { Header = "Open NetRisk" };
        open.Click += (_, _) => ShowMainWindow();

        _toggleItem.Click += (_, _) => ToggleMainWindow();

        var exit = new NativeMenuItem { Header = "Exit" };
        exit.Click += (_, _) => Environment.Exit(0);

        return new NativeMenu
        {
            Items =
            {
                _statusItem,
                new NativeMenuItemSeparator(),
                open,
                _toggleItem,
                new NativeMenuItemSeparator(),
                exit
            }
        };
    }

    /// <summary>Builds the one-line "quick status" shown at the top of the tray menu.</summary>
    private void RefreshStatus()
    {
        try
        {
            var auth = Program.ServiceProvider.GetService<IAuthenticationService>();
            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";

            string status;
            if (auth is { IsAuthenticated: true })
            {
                var user = auth.AuthenticatedUserInfo?.UserName
                           ?? auth.AuthenticatedUserInfo?.UserAccount
                           ?? "user";
                status = $"NetRisk {version} — signed in as {user}";
            }
            else
            {
                status = $"NetRisk {version} — not signed in";
            }

            // NativeMenu mutations must happen on the UI thread.
            Dispatcher.UIThread.Post(() => _statusItem.Header = status);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Unable to refresh tray status preview.");
        }
    }

    private void OnMainWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && _mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.Hide();
            _toggleItem.Header = "Open NetRisk";
        }
    }

    private void ToggleMainWindow()
    {
        if (_mainWindow.IsVisible && _mainWindow.WindowState != WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Minimized;
        }
        else
        {
            ShowMainWindow();
        }
    }

    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _toggleItem.Header = "Hide to Tray";
        });
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            return new WindowIcon(AssetLoader.Open(new Uri("avares://GUIClient/Assets/NetRisk.ico")));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _statusTimer?.Dispose();
        if (PlatformInfo.IsWindows)
        {
            _mainWindow.PropertyChanged -= OnMainWindowPropertyChanged;
        }
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }
    }
}

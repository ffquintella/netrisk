using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using ClientServices.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GUIClient.Behaviors;

/// <summary>
/// Saves and restores a window's size, position and maximised state (IX-7: "MainWindow and
/// auxiliary windows persist and restore geometry (size/position/monitor, clamped to visible
/// bounds)"). Geometry is stored per window type in the client's mutable configuration.
///
/// Restoration is clamped to a screen that actually exists, so a window saved on a monitor that
/// is no longer attached does not come back off-screen.
/// </summary>
public static class WindowGeometryPersistence
{
    /// <summary>Starts persisting <paramref name="window"/>'s geometry, and restores it now.</summary>
    public static void Attach(Window window)
    {
        var key = "windowGeometry." + window.GetType().Name;

        Restore(window, key);

        // Persist on close rather than on every move/resize: this writes to the client's config
        // store, and a write per mouse-move would be absurd.
        window.Closing += (_, _) => Save(window, key);
    }

    private static void Restore(Window window, string key)
    {
        try
        {
            var raw = Configuration?.GetConfigurationValue(key);
            if (string.IsNullOrWhiteSpace(raw)) return;

            // "width;height;x;y;maximised"
            var parts = raw.Split(';');
            if (parts.Length < 5) return;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) return;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)) return;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)) return;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)) return;
            var maximised = parts[4] == "1";

            if (width > 0) window.Width = Math.Max(width, double.IsNaN(window.MinWidth) ? 0 : window.MinWidth);
            if (height > 0) window.Height = Math.Max(height, double.IsNaN(window.MinHeight) ? 0 : window.MinHeight);

            var position = new PixelPoint(x, y);
            if (IsOnAVisibleScreen(window, position))
            {
                window.Position = position;
            }

            if (maximised) window.WindowState = WindowState.Maximized;
        }
        catch (Exception ex)
        {
            // Geometry is a convenience; a bad stored value must never stop a window opening.
            Log.Warning("Could not restore geometry for {Window}: {Message}", key, ex.Message);
        }
    }

    private static void Save(Window window, string key)
    {
        try
        {
            var maximised = window.WindowState == WindowState.Maximized;

            // Save the restored (non-maximised) bounds, so un-maximising lands somewhere sensible.
            var width = maximised ? window.MinWidth : window.Bounds.Width;
            var height = maximised ? window.MinHeight : window.Bounds.Height;

            var value = string.Join(';',
                width.ToString(CultureInfo.InvariantCulture),
                height.ToString(CultureInfo.InvariantCulture),
                window.Position.X.ToString(CultureInfo.InvariantCulture),
                window.Position.Y.ToString(CultureInfo.InvariantCulture),
                maximised ? "1" : "0");

            Configuration?.SetConfigurationValue(key, value);
        }
        catch (Exception ex)
        {
            Log.Warning("Could not save geometry for {Window}: {Message}", key, ex.Message);
        }
    }

    /// <summary>True when <paramref name="position"/> falls inside one of the attached screens.</summary>
    private static bool IsOnAVisibleScreen(Window window, PixelPoint position)
    {
        var screens = window.Screens;
        if (screens == null) return false;

        foreach (var screen in screens.All)
        {
            if (screen.Bounds.Contains(position)) return true;
        }

        return false;
    }

    private static IMutableConfigurationService? Configuration =>
        Program.ServiceProvider.GetService<IMutableConfigurationService>();
}

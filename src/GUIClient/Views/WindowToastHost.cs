using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace GUIClient.Views;

/// <summary>
/// Puts a <see cref="NotificationHost"/> over a window that does not declare one in XAML.
///
/// The main window declares its own host in its grid, so it keeps it below the modal dim overlay.
/// Every other window — the auxiliary windows (Administration, Reports, …) and the modal dialogs —
/// gets one here, because a toast raised from one of them used to land on the main window's single
/// host, behind whatever the user was looking at.
/// </summary>
internal static class WindowToastHost
{
    public static void Attach(Window window)
    {
        var layer = OverlayLayer.GetOverlayLayer(window);
        if (layer is null) return;

        var host = new NotificationHost();

        // The overlay layer is a Canvas, so the host is sized to it explicitly rather than trusting
        // it to stretch; the stack inside the host does the bottom-right placement.
        layer.GetObservable(Visual.BoundsProperty).Subscribe(new BoundsSizer(host));

        layer.Children.Add(host);
    }

    private sealed class BoundsSizer : IObserver<Rect>
    {
        private readonly Control _control;

        public BoundsSizer(Control control) => _control = control;

        public void OnNext(Rect value)
        {
            _control.Width = value.Width;
            _control.Height = value.Height;
        }

        public void OnCompleted() { }

        public void OnError(Exception error) { }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace GUIClient.Behaviors;

/// <summary>
/// Focuses a control as soon as it becomes visible.
///
/// IX-8 requires Ctrl+F to "reveal the search row, focus it". The reveal is a view-model flag
/// bound to <c>IsVisible</c>; this attached property supplies the focus half, so every search
/// box gets the same behaviour without each view hand-rolling it in code-behind.
/// </summary>
public static class FocusOnVisible
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(FocusOnVisible));

    static FocusOnVisible()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.NewValue is true)
            {
                control.PropertyChanged += OnControlPropertyChanged;
            }
            else
            {
                control.PropertyChanged -= OnControlPropertyChanged;
            }
        });
    }

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property != Visual.IsVisibleProperty) return;
        if (sender is not Control control) return;
        if (args.NewValue is not true) return;

        // The control is not yet laid out at the moment IsVisible flips, so focus on the next tick.
        Dispatcher.UIThread.Post(() => control.Focus(), DispatcherPriority.Input);
    }
}

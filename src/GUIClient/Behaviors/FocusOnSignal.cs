using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace GUIClient.Behaviors;

/// <summary>
/// Focuses a control whenever a bound counter changes.
///
/// <see cref="FocusOnVisible"/> covers a search row that Ctrl+F reveals. A search box that is always
/// visible (the Hosts view, S38 §3.1) never changes visibility, so it needs a different trigger: the
/// view model bumps a number from the Ctrl+F command and this focuses the box and selects its text,
/// so the next keystroke replaces the old search. No code-behind, and no view model reaching into a
/// control.
/// </summary>
public static class FocusOnSignal
{
    public static readonly AttachedProperty<int> SignalProperty =
        AvaloniaProperty.RegisterAttached<Control, int>("Signal", typeof(FocusOnSignal));

    static FocusOnSignal()
    {
        SignalProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            // The initial binding pushes the starting value; that is not a request.
            if (args.OldValue is not int previous || args.NewValue is not int current || current == previous) return;
            if (previous == 0 && current == 0) return;

            Dispatcher.UIThread.Post(() =>
            {
                control.Focus(NavigationMethod.Tab);
                if (control is TextBox box) box.SelectAll();
            }, DispatcherPriority.Input);
        });
    }

    public static void SetSignal(Control control, int value) => control.SetValue(SignalProperty, value);

    public static int GetSignal(Control control) => control.GetValue(SignalProperty);
}

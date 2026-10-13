using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GUIClient.Behaviors;

/// <summary>
/// Focuses a control as soon as it becomes visible.
///
/// IX-8 requires Ctrl+F to "reveal the search row, focus it". The reveal is a view-model flag
/// bound to <c>IsVisible</c>; this attached property supplies the focus half, so every search
/// box gets the same behaviour without each view hand-rolling it in code-behind.
///
/// "Visible" means <em>effectively</em> visible: every view that uses this binds the flag on the
/// search row's panel, not on the text box inside it, so the box's own <c>IsVisible</c> never
/// changes. Watching only that property — as this did until T330 — meant the caret never arrived.
/// So any <c>IsVisible</c> that turns true on the control or on one of its ancestors counts, and
/// the focus goes once the control is effectively visible.
/// </summary>
public static class FocusOnVisible
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Enabled", typeof(FocusOnVisible));

    // The controls that opted in. A handful at most (one search box per screen), and weak so a
    // closed screen is not kept alive by its search box being listed here.
    private static readonly List<WeakReference<Control>> Watched = new();

    static FocusOnVisible()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            Watched.RemoveAll(w => !w.TryGetTarget(out var watched) || ReferenceEquals(watched, control));
            if (args.NewValue is true) Watched.Add(new WeakReference<Control>(control));
        });

        Visual.IsVisibleProperty.Changed.AddClassHandler<Visual>(OnIsVisibleChanged);
    }

    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);

    private static void OnIsVisibleChanged(Visual visual, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true || Watched.Count == 0) return;

        foreach (var weak in Watched.ToArray())
        {
            if (!weak.TryGetTarget(out var control)) continue;
            if (!ReferenceEquals(control, visual) && !visual.IsVisualAncestorOf(control)) continue;

            // The control is not yet laid out at the moment IsVisible flips, so focus on the next tick.
            Dispatcher.UIThread.Post(() =>
            {
                if (control.IsEffectivelyVisible) control.Focus();
            }, DispatcherPriority.Input);
        }
    }
}

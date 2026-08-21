using System;
using Avalonia.Controls;
using Avalonia.Input;
using GUIClient.Behaviors;

namespace GUIClient.Views;

/// <summary>
/// Base for the app's plain (non-dialog) windows: the auxiliary windows of IX-1 and the small
/// standalone windows. Supplies the two things the standard requires of every window and that
/// each of these used to lack:
///
/// <list type="bullet">
///   <item><description>IX-8 — <c>Esc</c> closes the window.</description></item>
///   <item><description>IX-7 — size, position and maximised state persist across runs.</description></item>
/// </list>
///
/// Modal editor and utility dialogs do <b>not</b> use this — they derive from
/// <c>DialogWindowBase&lt;TResult&gt;</c>, which gives them Esc, Ctrl+S and typed results.
/// </summary>
public class AuxiliaryWindowBase : Window
{
    protected AuxiliaryWindowBase()
    {
        Opened += OnOpenedInternal;
    }

    /// <summary>
    /// Set to false by windows that must not be dismissed with Esc — a progress window the user
    /// should not be able to abandon mid-operation, for instance.
    /// </summary>
    protected bool CloseOnEscape { get; init; } = true;

    /// <summary>Set to false by windows whose size is fixed and meaningless to persist.</summary>
    protected bool PersistGeometry { get; init; } = true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (CloseOnEscape && e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnOpenedInternal(object? sender, EventArgs e)
    {
        Opened -= OnOpenedInternal;

        if (PersistGeometry) WindowGeometryPersistence.Attach(this);
    }
}

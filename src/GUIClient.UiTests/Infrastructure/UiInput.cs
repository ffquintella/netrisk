using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace GUIClient.UiTests.Infrastructure;

/// <summary>
/// Simulated user input (S54 §5.1 item 3): every interaction goes through the headless platform's
/// mouse and keyboard, the way a person's would — hit-testing, focus, key handling and text input —
/// never through the view-model. A control that is covered, disabled or collapsed does not receive
/// the click, exactly as on screen.
/// </summary>
public static class UiInput
{
    /// <summary>Left-clicks the centre of <paramref name="control"/>.</summary>
    public static void Click(Control control)
    {
        var topLevel = TopLevelOf(control);
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)
                     ?? throw new InvalidOperationException($"{control.GetType().Name} is not laid out in its window.");

        topLevel.MouseMove(centre);
        topLevel.MouseDown(centre, MouseButton.Left);
        topLevel.MouseUp(centre, MouseButton.Left);
        UiHost.Settle(topLevel);
    }

    /// <summary>
    /// Scrolls <paramref name="control"/> into view, as a person scrolls to a button below the fold, and then
    /// left-clicks it. <see cref="Click"/> alone misses a control a scroll viewer has moved out of the window.
    /// </summary>
    public static void ScrollToAndClick(Control control)
    {
        control.BringIntoView();
        UiHost.Settle(TopLevelOf(control));
        Click(control);
    }

    /// <summary>Presses and releases one key on the window that has focus.</summary>
    public static void Press(TopLevel topLevel, Key key, PhysicalKey physicalKey,
        RawInputModifiers modifiers = RawInputModifiers.None, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            topLevel.KeyPress(key, modifiers, physicalKey, null);

            // A key can close its own window (Esc on a dialog); there is then nothing to release on.
            if (topLevel.PlatformImpl is null) break;
            topLevel.KeyRelease(key, modifiers, physicalKey, null);
        }

        UiHost.Settle(topLevel.PlatformImpl is null ? null : topLevel);
    }

    /// <summary>Types <paramref name="text"/> into whatever has keyboard focus.</summary>
    public static void Type(TopLevel topLevel, string text)
    {
        topLevel.KeyTextInput(text);
        UiHost.Settle(topLevel);
    }

    /// <summary>
    /// Clicks into <paramref name="textBox"/> and deletes its content with the keyboard
    /// (End, then one Backspace per character).
    /// </summary>
    public static void ClearByKeyboard(TextBox textBox)
    {
        var topLevel = TopLevelOf(textBox);
        Click(textBox);
        Press(topLevel, Key.End, PhysicalKey.End);
        Press(topLevel, Key.Back, PhysicalKey.Backspace, times: textBox.Text?.Length ?? 0);
    }

    /// <summary>
    /// Picks <paramref name="itemText"/> in <paramref name="combo"/> the way a person does: a click opens the
    /// drop-down, the list scrolls to the item, a click on the item chooses it. The item is matched by the text its
    /// template shows.
    /// </summary>
    public static async Task ChooseAsync(ComboBox combo, string itemText)
    {
        Click(combo);
        await UiHost.WaitUntilAsync(() => combo.IsDropDownOpen, "the drop-down to open");

        ComboBoxItem? item = null;
        for (var index = 0; index < combo.ItemCount && item is null; index++)
        {
            combo.ScrollIntoView(index);
            UiHost.Settle();
            item = combo.GetRealizedContainers().OfType<ComboBoxItem>()
                .FirstOrDefault(i => TextOf(i) == itemText && i.IsEffectivelyVisible);
        }

        if (item is null)
            throw new InvalidOperationException($"The drop-down has no \"{itemText}\" item; it has: " +
                                                string.Join(" | ", combo.GetRealizedContainers().Select(TextOf)));

        Click(item);
        await UiHost.WaitUntilAsync(() => !combo.IsDropDownOpen, "the drop-down to close on the choice");
    }

    /// <summary>The text a templated item shows: its text blocks, joined.</summary>
    public static string TextOf(Visual item) =>
        string.Concat(item.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));

    private static TopLevel TopLevelOf(Visual visual) =>
        TopLevel.GetTopLevel(visual)
        ?? throw new InvalidOperationException($"{visual.GetType().Name} is not in a shown window.");
}

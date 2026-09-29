using System;
using System.Collections.Generic;

namespace GUIClient.Tools;

/// <summary>
/// The item a ComboBox should show for a stored value. Bind <c>SelectedItem</c> to this rather than
/// <c>SelectedValue</c>: Avalonia does not re-resolve <c>SelectedValue</c> when a cleared list is
/// refilled, so the field goes blank; a refilled list holds new instances, which always re-select.
/// Free of Avalonia so <c>GUIClient.Tests</c> can compile it directly.
/// </summary>
public static class ComboSelection
{
    /// <summary>The first option whose key equals <paramref name="value"/>, or null when none does.</summary>
    public static T? ItemFor<T, TKey>(IEnumerable<T> options, Func<T, TKey> key, TKey value)
        where T : class
    {
        foreach (var option in options)
            if (EqualityComparer<TKey>.Default.Equals(key(option), value))
                return option;

        return null;
    }
}

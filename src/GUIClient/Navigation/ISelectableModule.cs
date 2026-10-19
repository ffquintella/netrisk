using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GUIClient.Models;

namespace GUIClient.Navigation;

/// <summary>
/// A shell module that can select one of its records (S40 §10.3, IX-7): it asks its service for the record and selects it,
/// or applies the sub-view's filter. A record out of scope or missing is refused with a toast and nothing selected.
/// </summary>
public interface ISelectableModule
{
    /// <returns>Whether the record was selected.</returns>
    Task<bool> SelectRecordAsync(int id, string? subView);
}

/// <summary>
/// Where <c>NavigationService</c> finds the module behind a view without walking the visual tree (IX-7): each
/// module view-model registers itself when its view creates it. Weak references, so a discarded module is not kept alive.
/// </summary>
public sealed class SelectableModules
{
    private readonly Dictionary<AvaliableViews, WeakReference<ISelectableModule>> _modules = new();
    private readonly object _lock = new();

    public void Register(AvaliableViews view, ISelectableModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        lock (_lock) _modules[view] = new WeakReference<ISelectableModule>(module);
    }

    public ISelectableModule? Find(AvaliableViews view)
    {
        lock (_lock)
            return _modules.TryGetValue(view, out var reference) && reference.TryGetTarget(out var module) ? module : null;
    }
}

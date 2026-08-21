using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GUIClient.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient.Tools;

/// <summary>
/// Resolves the platform file-picker for the window the user is actually working in.
///
/// View-models used to reach the picker through a <c>Window</c> reference they were handed at
/// construction, which is one of the reasons they held onto windows at all. IX-7 forbids that:
/// a view-model should not know or look up its window. This asks the window provider for the
/// active window instead, so open/save pickers still parent correctly without any view-model
/// owning a window.
/// </summary>
public static class StorageProviderAccessor
{
    public static IStorageProvider? Current
    {
        get
        {
            var provider = Program.ServiceProvider.GetService<IMainWindowProvider>();

            var window = provider?.GetActiveWindow();

            return window is null ? null : TopLevel.GetTopLevel(window)?.StorageProvider;
        }
    }
}

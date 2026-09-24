using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.Notifications;

namespace GUIClient.Views;

/// <summary>
/// Renders the transient notification stack in the bottom-right of the window it lives in. Hit-test
/// transparent, so it never blocks the content it floats over.
///
/// The host registers itself with <see cref="NotificationService"/> for its own window, which is
/// what lets a toast raised from the Administration window (or any dialog) appear there instead of
/// behind it on the main window.
/// </summary>
public partial class NotificationHost : UserControl
{
    private NotificationService? _service;
    private Window? _window;

    public NotificationHost()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Null in the XAML previewer, where no container has been built.
        if (Program.ServiceProvider is null) return;

        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null) return;

        _service = (NotificationService) Program.ServiceProvider.GetService(typeof(NotificationService))!;
        this.FindControl<ItemsControl>("ToastsCtrl")!.ItemsSource = _service.AttachHost(_window);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_service is not null && _window is not null) _service.DetachHost(_window);

        _service = null;
        _window = null;

        base.OnDetachedFromVisualTree(e);
    }
}

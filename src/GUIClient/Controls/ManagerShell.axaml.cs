using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Controls;

/// <summary>
/// The shared skeleton for manager windows (IX-5 archetype C): a titled header, a master list
/// with a control bar beneath it, a splitter, and a read-only detail pane, plus a footer.
///
/// The report-template and report-schedule managers were near-identical copies of this layout;
/// they now supply only their list, control bar and detail pane through the content slots, so the
/// archetype exists in one place and future managers inherit it rather than re-deriving it.
/// </summary>
public partial class ManagerShell : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ManagerShell, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> ListHeaderProperty =
        AvaloniaProperty.Register<ManagerShell, string>(nameof(ListHeader), string.Empty);

    public static readonly StyledProperty<object?> ListContentProperty =
        AvaloniaProperty.Register<ManagerShell, object?>(nameof(ListContent));

    public static readonly StyledProperty<object?> ControlBarContentProperty =
        AvaloniaProperty.Register<ManagerShell, object?>(nameof(ControlBarContent));

    public static readonly StyledProperty<object?> DetailContentProperty =
        AvaloniaProperty.Register<ManagerShell, object?>(nameof(DetailContent));

    public ManagerShell()
    {
        InitializeComponent();
    }

    /// <summary>Shown in the header and repeated in the footer.</summary>
    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Heading above the master list ("Templates", "Schedules", …).</summary>
    public string ListHeader
    {
        get => GetValue(ListHeaderProperty);
        set => SetValue(ListHeaderProperty, value);
    }

    /// <summary>The master list itself.</summary>
    public object? ListContent
    {
        get => GetValue(ListContentProperty);
        set => SetValue(ListContentProperty, value);
    }

    /// <summary>The control bar beneath the list. Delete belongs last and visually separated.</summary>
    public object? ControlBarContent
    {
        get => GetValue(ControlBarContentProperty);
        set => SetValue(ControlBarContentProperty, value);
    }

    /// <summary>The read-only detail pane for the selected item.</summary>
    public object? DetailContent
    {
        get => GetValue(DetailContentProperty);
        set => SetValue(DetailContentProperty, value);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

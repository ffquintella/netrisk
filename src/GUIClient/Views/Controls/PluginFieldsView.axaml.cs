using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views.Controls;

/// <summary>
/// Renders the controls a secret-vault plugin contributed to a screen.
///
/// <para>A control rather than the same forty lines of XAML in both the picker and the connection
/// editor. It takes the list and nothing else — no commands, no per-field bindings — which is what
/// makes it extractable where the vault-picker row (five styled properties and two forwarded
/// commands per instance) was not.</para>
///
/// <para>The items are <see cref="Tools.PluginFieldState"/>, which carries its own validation and no
/// Avalonia types, so the rules an operator sees enforced here are the ones
/// <c>GUIClient.Tests</c> can compile and test directly.</para>
/// </summary>
public partial class PluginFieldsView : UserControl
{
    public static readonly StyledProperty<IEnumerable?> FieldsProperty =
        AvaloniaProperty.Register<PluginFieldsView, IEnumerable?>(nameof(Fields));

    /// <summary>The screen's plugin fields, in the order the plugin declared them.</summary>
    public IEnumerable? Fields
    {
        get => GetValue(FieldsProperty);
        set => SetValue(FieldsProperty, value);
    }

    public PluginFieldsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

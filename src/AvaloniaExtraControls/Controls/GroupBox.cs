using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace AvaloniaExtraControls.Controls;

/// <summary>
/// A titled container: a bordered surface with a header band above its content.
/// </summary>
/// <remarks>
/// Replaces <c>Aura.UI.Controls.GroupBox</c>. The template deliberately carries no colours of its
/// own — every brush is a <c>TemplateBinding</c>, so the consuming application's token layer
/// (<c>Styles/WindowStyles.axaml</c> in GUIClient) decides what a <c>GroupBox</c> looks like.
/// </remarks>
public class GroupBox : HeaderedContentControl
{
    public static readonly StyledProperty<double> HeaderFontSizeProperty =
        AvaloniaProperty.Register<GroupBox, double>(nameof(HeaderFontSize), 13d);

    /// <summary>Font size of the header band. Separate from <see cref="TemplatedControl.FontSize"/>,
    /// which applies to the content.</summary>
    public double HeaderFontSize
    {
        get => GetValue(HeaderFontSizeProperty);
        set => SetValue(HeaderFontSizeProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(GroupBox);
}

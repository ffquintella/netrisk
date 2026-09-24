using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AvaloniaExtraControls.Controls;

/// <summary>Where the badge bubble sits relative to the badged content.</summary>
public enum BadgePosition
{
    Left,
    Right,
    Top,
    Bottom
}

/// <summary>
/// Overlays a small bubble (<see cref="BadgeContent"/>) on a corner of its content — the unread
/// count on the notification bell, for instance.
/// </summary>
/// <remarks>
/// Replaces <c>Aura.UI.Controls.Badge</c>. Like <see cref="GroupBox"/> it carries no colours; the
/// bubble paints itself with <see cref="Avalonia.Controls.Primitives.TemplatedControl.BorderBrush"/>
/// so the token layer owns the palette.
/// </remarks>
public class Badge : ContentControl
{
    public static readonly StyledProperty<object?> BadgeContentProperty =
        AvaloniaProperty.Register<Badge, object?>(nameof(BadgeContent));

    public static readonly StyledProperty<BadgePosition> BadgePositionProperty =
        AvaloniaProperty.Register<Badge, BadgePosition>(nameof(BadgePosition), BadgePosition.Right);

    public static readonly StyledProperty<bool> IsBadgeVisibleProperty =
        AvaloniaProperty.Register<Badge, bool>(nameof(IsBadgeVisible), true);

    /// <summary>Content of the bubble. Usually a count.</summary>
    public object? BadgeContent
    {
        get => GetValue(BadgeContentProperty);
        set => SetValue(BadgeContentProperty, value);
    }

    public BadgePosition BadgePosition
    {
        get => GetValue(BadgePositionProperty);
        set => SetValue(BadgePositionProperty, value);
    }

    /// <summary>Hides the bubble without hiding the badged content.</summary>
    public bool IsBadgeVisible
    {
        get => GetValue(IsBadgeVisibleProperty);
        set => SetValue(IsBadgeVisibleProperty, value);
    }

    /// <summary>Horizontal alignment the bubble takes for the current <see cref="BadgePosition"/>.</summary>
    public HorizontalAlignment BadgeHorizontalAlignment => BadgePosition switch
    {
        BadgePosition.Left => HorizontalAlignment.Left,
        BadgePosition.Right => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center
    };

    /// <summary>Vertical alignment the bubble takes for the current <see cref="BadgePosition"/>.</summary>
    public VerticalAlignment BadgeVerticalAlignment => BadgePosition switch
    {
        BadgePosition.Bottom => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Top
    };

    protected override Type StyleKeyOverride => typeof(Badge);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == BadgePositionProperty)
        {
            RaisePropertyChanged(BadgeHorizontalAlignmentProperty, default, BadgeHorizontalAlignment);
            RaisePropertyChanged(BadgeVerticalAlignmentProperty, default, BadgeVerticalAlignment);
        }
    }

    public static readonly DirectProperty<Badge, HorizontalAlignment> BadgeHorizontalAlignmentProperty =
        AvaloniaProperty.RegisterDirect<Badge, HorizontalAlignment>(
            nameof(BadgeHorizontalAlignment), o => o.BadgeHorizontalAlignment);

    public static readonly DirectProperty<Badge, VerticalAlignment> BadgeVerticalAlignmentProperty =
        AvaloniaProperty.RegisterDirect<Badge, VerticalAlignment>(
            nameof(BadgeVerticalAlignment), o => o.BadgeVerticalAlignment);
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace AvaloniaExtraControls.MultiSelect;

public class MultiSelect : TemplatedControl
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<MultiSelect>();

    public IBrush? Background
    {
        get { return GetValue(BackgroundProperty); }
        set { SetValue(BackgroundProperty, value); }
    }
    
    public MultiSelect()
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        
     
    }
}
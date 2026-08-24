using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views.Admin;

public partial class EntityAccessView : UserControl
{
    public EntityAccessView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

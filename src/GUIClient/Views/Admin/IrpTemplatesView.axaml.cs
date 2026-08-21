using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views.Admin;

public partial class IrpTemplatesView : UserControl
{
    public IrpTemplatesView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

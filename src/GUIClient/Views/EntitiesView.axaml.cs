using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class EntitiesView : UserControl
{
    public EntitiesView()
    {
        DataContext = new EntitiesViewModel();
        
        InitializeComponent();
    }
}
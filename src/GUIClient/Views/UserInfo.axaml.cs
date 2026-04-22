using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GUIClient.Views;

public partial class UserInfo : Window
{
    public UserInfo()
    {

        //DataContext = new UserInfoViewModel();
        
        InitializeComponent();
#if DEBUG
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
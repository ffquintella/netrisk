using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;

namespace GUIClient.Views;

public partial class LoadConfigurationWindow : Window
{
    public string ServerUrl { get; set; } = "";
    public LoadConfigurationWindow()
    {
        DataContext = this;
        InitializeComponent();
    }
    
    public void OnSave()
    {
        ServerUrl = ServerUrl.Trim();
        if (string.IsNullOrEmpty(ServerUrl))
        {
            var msgError = MessageBoxManager.GetMessageBoxStandard(
                new MessageBoxStandardParams
                {
                    ContentTitle = "ERRO",
                    ContentMessage = "Please enter a valid URL",
                    Icon = MsBox.Avalonia.Enums.Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            msgError.ShowAsync();
            return;
        }
        Close(ServerUrl);
    }
}
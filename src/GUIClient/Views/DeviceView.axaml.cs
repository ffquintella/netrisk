using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Splat;

namespace GUIClient.Views;

public partial class DeviceView : UserControl
{
    
    public DeviceView()
    {
        //DataContext = new DeviceViewModel(GetService<IClientService>());
        //((DeviceViewModel)DataContext).Initialize();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GUIClient.ViewModels;
using Model.Configuration;
using Splat;

namespace GUIClient.Views;

public partial class NavigationBar : UserControl
{
    
    public static readonly StyledProperty<Window> ParentWindowProperty =
        AvaloniaProperty.Register<NavigationBar, Window>(nameof(MainWindow));

    public Window ParentWindow
    {
        get { return GetValue(ParentWindowProperty); }
        set { SetValue(ParentWindowProperty, value); }
    }
    public NavigationBar()
    {
        DataContext = new NavigationBarViewModel(GetService<ServerConfiguration>());
        
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    private static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}
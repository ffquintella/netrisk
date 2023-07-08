using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Splat;

namespace GUIClient.Views;

public partial class EditRiskWindow : Window
{
    public EditRiskWindow()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        
        /*var sourceComboBox = this.Find<ComboBox>("sourceComboBox");
        sourceComboBox.ItemsSource = (DataContext as EditRiskViewModel).RiskSources;*/

    }
    
    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}
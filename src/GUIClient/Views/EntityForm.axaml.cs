using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using AvaloniaExtraControls.MultiSelect;
using DAL.Entities;
using Model.Entities;

namespace GUIClient.Views;

public partial class EntityForm : UserControl
{

    public EntityForm(Entity entity, EntitiesConfiguration configuration): this()
    {
        var definition = configuration.Definitions[entity.DefinitionName];
        CreateForm(entity, definition);
    }
    
    private void CreateForm(Entity entity, EntityDefinition definition)
    {
        var form = new StackPanel();
        foreach (var (key, value) in definition.Properties)
        {
            CreateControl(ref form, value, entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == key)?.Value);
        }

        this.Content = form;
    }

    private void CreateControl(ref StackPanel panel, EntityType type, string? value)
    {
        if (type.Type != "Boolean")
        {

            var label = new TextBlock { Text = type.Label };
            panel.Children.Add(label);
        }

        switch (type.Type)
        {
            case "String":
                var tb = new TextBox();
                if (type.DefaultValue == null) type.DefaultValue = "";
                tb.Text = value ?? type.DefaultValue;
                panel.Children.Add(tb);
                break;
            case "Integer":
                var ci = new NumericUpDown();
                if (type.DefaultValue == null) type.DefaultValue = "0";
                ci.Value = int.Parse(value ?? type.DefaultValue);
                panel.Children.Add(ci);
                break;
            case "Boolean":
                var cb = new CheckBox();
                cb.Content = type.Label;
                if (type.DefaultValue == null) type.DefaultValue = "false";
                cb.IsChecked = bool.Parse(value ?? type.DefaultValue);
                panel.Children.Add(cb);
                break;
            default:
                if (type.Type.StartsWith("Definition"))
                {
                     var definition = type.Type.Split("(")[1].Split(")")[0];

                     if (type.Multiple)
                     {
                         var ms = new MultiSelect();
                         
                         ms.AvailableItems = new List<string> {"a", "b", "c"};
                         panel.Children.Add(ms);
                     }
                     else
                     {
                         var combo = new ComboBox();
                         combo.ItemsSource = new List<string> {"a", "b", "c"};
                         panel.Children.Add(combo);
                     }

                     break;
                }
                var textBox = new TextBox();
                panel.Children.Add(textBox);
                break;
                
        }

    }
    
    public EntityForm()
    {
        InitializeComponent();
    }
}
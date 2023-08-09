using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
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
            CreateControl(ref form, value);
        }

        this.Content = form;
    }

    private void CreateControl(ref StackPanel panel, EntityType type)
    {
        var label = new TextBlock {Text = type.Label};
        panel.Children.Add(label);
        
        var textBox = new TextBox();
        panel.Children.Add(textBox);
    }
    
    public EntityForm()
    {
        InitializeComponent();
    }
}
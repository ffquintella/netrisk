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
            var label = new TextBlock {Text = key};
            var textBox = new TextBox();
            form.Children.Add(label);
            form.Children.Add(textBox);
        }

        this.Content = form;
        //var uc = this.GetControl<UserControl>("EntityForm");
        //uc.Content = form;
    }
    
    public EntityForm()
    {
        InitializeComponent();
    }
}
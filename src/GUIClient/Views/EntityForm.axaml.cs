using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DAL.Entities;
using Model.Entities;

namespace GUIClient.Views;

public partial class EntityForm : UserControl
{

    public EntityForm(Entity entity, EntitiesConfiguration configuration): this()
    {
        
    }
    
    public EntityForm()
    {
        InitializeComponent();
    }
}
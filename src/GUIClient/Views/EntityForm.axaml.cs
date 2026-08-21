using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DAL.Entities;
using GUIClient.Models.Events;
using GUIClient.ViewModels.Entities;
using Model.Entities;

namespace GUIClient.Views;

/// <summary>
/// Hosts the entity form. All it does now is bind a <see cref="EntityFormViewModel"/> —
/// the form itself is declared in XAML over the entity's property definitions (IX-9).
/// </summary>
public partial class EntityForm : UserControl
{
    private readonly EntityFormViewModel _viewModel = new();

    public EntityForm()
    {
        DataContext = _viewModel;
        _viewModel.EntitySaved += (sender, args) => EntitySaved?.Invoke(sender, args);

        InitializeComponent();
    }

    public EntityForm(Entity entity, EntitiesConfiguration configuration) : this()
    {
        _viewModel.Load(entity, configuration);
    }

    /// <summary>Raised when the form persists the entity, so the entity tree can update.</summary>
    public event EventHandler<EntitySavedEventHandlerArgs>? EntitySaved;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

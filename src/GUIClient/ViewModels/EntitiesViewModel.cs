using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Views;
using Model.Entities;
using ReactiveUI;
using System.Reactive;
using DynamicData;
using GUIClient.Models.Entity;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using System.Collections.Generic;


namespace GUIClient.ViewModels;

public class EntitiesViewModel: ViewModelBase
{
    #region LANGUAGE STRINGS
    
    public string StrEntities { get; }
    public string StrEntity { get; }
    

    #endregion

    #region PROPERTIES

    private ObservableCollection<Entity>? _entities;
    public ObservableCollection<Entity> Entities
    {
        get => _entities ??= new ObservableCollection<Entity>();
        set => this.RaiseAndSetIfChanged(ref _entities, value);
    }
    
    private ObservableCollection<TreeNode>? _nodes;
    public ObservableCollection<TreeNode>? Nodes
    {
        get => _nodes;
        set => this.RaiseAndSetIfChanged(ref _nodes, value);
    }
    
    private TreeNode? _selectedNode;
    public TreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (value == null) return;
            CreateEntityForm(value.EntityId);
            this.RaiseAndSetIfChanged(ref _selectedNode, value);
        }
    }
    
    public ReactiveCommand<Unit, Unit> BtAddEntClicked { get; }
    public ReactiveCommand<Unit, Unit> BtEditEntClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteEntClicked { get; }

    #endregion

    #region PRIVATE FIELDS
    
    private readonly IAuthenticationService _autenticationService;
    private readonly IEntitiesService _entitiesService;
    private readonly IDialogService _dialogService;

    private EntitiesConfiguration? _entitiesConfiguration;
    private UserControl? _view;
    
    private Panel? _entityPanel = null;
    
    #endregion

    public EntitiesViewModel(UserControl view): this()
    {
        _view = view;
    }

    public EntitiesViewModel()
    {
        StrEntities = Localizer["Entities"];
        StrEntity = Localizer["Entity"];
        
        BtAddEntClicked = ReactiveCommand.Create(ExecuteAddEntity);
        BtEditEntClicked = ReactiveCommand.Create(ExecuteEditEntity);
        BtDeleteEntClicked = ReactiveCommand.Create(ExecuteDeleteEntity);
        
        _autenticationService = GetService<IAuthenticationService>();
        _entitiesService = GetService<IEntitiesService>();
        _dialogService = GetService<IDialogService>();
        
        _autenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };
        
    }

    private void Initialize()
    {
        if (_view == null) throw new Exception("View is null");
        _entityPanel = _view.FindControl<Panel>("EntityPanel");
        LoadData();
    }

    private async void ExecuteEditEntity()
    {
        if (SelectedNode == null)
        {
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["PleaseSelectAnItemMSG"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
                            
            msgBox1.ShowAsync();
            return;
        }
        
    }

    private async void ExecuteDeleteEntity()
    {
        if (SelectedNode == null)
        {
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["PleaseSelectAnItemMSG"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            msgBox1.ShowAsync();
            return;
        }

        var msgBoxConfirm = MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Confirmation"],
                ContentMessage = Localizer["AreYouSureYouWantToDeleteMSG"],
                Icon = Icon.Question,
                ButtonDefinitions = ButtonEnum.YesNo,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

        var result = await msgBoxConfirm.ShowAsync();

        if (result == ButtonResult.Yes)
        {
            //_entitiesService.Delete(SelectedNode.EntityId);
            
            var ent = Entities.FirstOrDefault(e => e.Id == SelectedNode.EntityId);
            if (ent != null)
            {
                DeleteNode(ent.Id);
                //Entities.Remove(ent);
            }

            SelectedNode = null;
        }
        
    }
    
    private void DeleteNode(int entityId)
    {
        //Let´s find the node
        var rootNode = Nodes?.FirstOrDefault(nd => nd.EntityId == entityId);

        //Now let´s delete the children 
        if (rootNode != null)
        {
            foreach (var subNode in rootNode.SubNodes!)
            {
                DeleteNode(subNode.EntityId);
            }
            var ent = Entities.FirstOrDefault(e => e.Id == entityId);
            _entitiesService.Delete(rootNode.EntityId);

            var idx = Nodes!.IndexOf(rootNode);
            Nodes[idx].IsVisible = false;
            
            // 
            
            SelectedNode = null;
            
            //Entities.Remove(ent);
            
        }


    }

    private async void ExecuteAddEntity()
    {
        var result = await _dialogService.ShowDialogAsync<EntityDialogResult>(nameof(CreateEntityDialogViewModel));
        
        if(result == null) return;
        
        Logger.Debug("Add entity dialog result: {@Result}", result.Result);
        if(result.Result == 0) return;
        
        Logger.Debug("Creating new entity named: {@Entity}", result.Name);

        int? parentId = null;
        if(result.Parent != null && result.Parent.DefinitionName != "---") parentId = result.Parent.Id;

        var properties = await _entitiesService.GetMandatoryPropertiesAsync(result.Type);
        
        var nameIndex = properties.FindIndex(p => p.Type == "name");
        
        properties[nameIndex].Value = result.Name;

        foreach (var property in properties)
        {
            property.Name += result.Name;
        }
        //properties[nameIndex].Name += result.Name;
        
        var entityDto = new EntityDto()
        {
            Id = 0,
            Parent = parentId,
            DefinitionName = result.Type,
            Status = "active",
            EntitiesProperties = properties
        };

        try
        {

            var entity = _entitiesService.CreateEntity(entityDto);
           
            Entities.Add(entity);

            var node = new TreeNode(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
                entity.Id, CreateChildNodes(Entities.ToList(), entity.Id));
            
            if(entity.Parent != null)
                Nodes.FirstOrDefault(n => n.EntityId == entity.Parent)?.SubNodes!.Add(node);
            else Nodes.Add(node);
            
            SelectedNode = node;
            
        }catch(Exception ex)
        {
            Logger.Error("Error creating entity: {Message}", ex.Message);
            
            var msgOk = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorCreatingEntityMSG"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgOk.ShowAsync();
           
        }
        

    }


    
    private void c_EntitySaved(object? sender, EntitySavedEventHandlerArgs e)
    {
        int index = Entities.ToList().FindIndex(en => en.Id == e.Entity.Id);
        
        Entities[index] = e.Entity;
        
    }
    
    private void CreateEntityForm(int entityId)
    {
        if (_entityPanel == null) throw new Exception("Entity panel is null");
        if (_entitiesConfiguration == null) throw new Exception("Entities configuration is null");
        
        //var entity = await _entitiesService.GetAsync(entityId);
        var entity = Entities.FirstOrDefault(e => e.Id == entityId);
        if (entity == null) return;
        
        var entityForm = new EntityForm(entity, _entitiesConfiguration);
        entityForm.EntitySaved += c_EntitySaved;
        
        _entityPanel.Children.Clear();
        _entityPanel.Children.Add(entityForm);
    }

    private async void LoadTree(List<Entity> rootEntities)
    {
        var nodes = new ObservableCollection<TreeNode>();
        
        foreach (var entity in rootEntities)
        {
            nodes.Add(new TreeNode(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
                entity.Id,
                CreateChildNodes(Entities.ToList(), entity.Id)));
        }

        Nodes = nodes;
    }

    private async void LoadData()
    {
        
        if(_entitiesConfiguration == null)
            _entitiesConfiguration = await _entitiesService.GetEntitiesConfigurationAsync();
        
        var allEntities = _entitiesService.GetAll();
        Entities = new ObservableCollection<Entity>(allEntities);
        
        var rootEntities = allEntities.Where(e => e.Parent == null).ToList();

        LoadTree(rootEntities);

        
    }

    private ObservableCollection<TreeNode> CreateChildNodes(List<Entity> entities, int rootId)
    {
        var children = entities.Where(e => e.Parent == rootId).ToList();
        var nodes = new ObservableCollection<TreeNode>();
        foreach (var child in children)
        {
            nodes.Add(new TreeNode(child.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
                child.Id,
                CreateChildNodes(entities, child.Id)));
        }

        return nodes;
    }
}
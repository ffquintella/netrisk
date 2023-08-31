using System;
using System.Collections;
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
using System.Runtime.InteropServices;
using Avalonia.Controls.Presenters;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs.Parameters;


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
            if (value != null) CreateEntityForm(value.EntityId);
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

        var entity = Entities.FirstOrDefault(ent => ent.Id == SelectedNode.EntityId);

        var parameter = new EntityDialogParameter()
        {
            Title = Localizer["EditEntity"],
            Name = entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
            Type = entity.DefinitionName,
            Parent = entity.Parent != null ? Entities.FirstOrDefault(e => e.Id == entity.Parent) : null
        };
        
        var dialogEdit = await _dialogService.ShowDialogAsync<EntityDialogResult, EntityDialogParameter>(nameof(EditEntityDialogViewModel), parameter);
        
        if(dialogEdit == null) return;
        
        Logger.Debug("Edit entity dialog result: {@Result}", dialogEdit.Result);
        
        if(dialogEdit.Result == 0) return;

        //var new_node = new TreeNode(dialogEdit.Name, SelectedNode.EntityId, SelectedNode.SubNodes);

        //TODO: SAVE to the database then reload the tree --- then remove the code below
        
        var nodes_copy = Nodes;
        UpdateNode(SelectedNode.EntityId, ref nodes_copy, dialogEdit.Name, dialogEdit.Parent?.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value);

        Nodes = new ObservableCollection<TreeNode>(nodes_copy);

    }

    private bool UpdateNode(int entityId, ref ObservableCollection<TreeNode> nodes, string name, string parent)
    {
        var node = nodes.FirstOrDefault(nd => nd.EntityId == entityId);
        if (node != null)
        {
            var nodeIdx = nodes.IndexOf(node);
            nodes[nodeIdx].Title = name;
            return true;
        }

        foreach (var subNode in nodes)
        {
            var subNodes = subNode.SubNodes;

            if (UpdateNode(entityId, ref subNodes, name, parent))
            {
                subNode.SubNodes = subNodes;
                return true;
            }

            
        }
        return false;
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
            
            var ent = Entities.FirstOrDefault(e => e.Id == SelectedNode.EntityId);
            if (ent != null)
            {
                DeleteNode(ent.Id);
            }

            _entityPanel!.Children.Clear();
            SelectedNode = null;
        }
        
    }

    private TreeNode? FindRootNode(int entityId, ObservableCollection<TreeNode> nodes)
    {
        var rootNode = nodes.FirstOrDefault(nd => nd.EntityId == entityId);
        if (rootNode != null) return rootNode;

        foreach (var node in nodes)
        {
            var resultNode = FindRootNode(entityId, node.SubNodes!);
            if (resultNode != null) return resultNode;
        }

        return null;
    }
    
    private void DeleteNode(int entityId)
    {
        //Let´s find the node
        //var rootNode = Nodes?.FirstOrDefault(nd => nd.EntityId == entityId);
        var rootNode = FindRootNode(entityId, Nodes!);

        //Now let´s delete the children 
        if (rootNode != null)
        {
            foreach (var subNode in rootNode.SubNodes!)
            {
                DeleteNode(subNode.EntityId);
            }
            var ent = Entities.FirstOrDefault(e => e.Id == entityId);
            _entitiesService.Delete(rootNode.EntityId);
            
            var nodes_copy = Nodes!.ToList();
            
            nodes_copy.Remove(rootNode);
            
            Nodes = new ObservableCollection<TreeNode>(nodes_copy);
          
            SelectedNode = null;
            
            Entities.Remove(ent);
            
        }


    }

    private async void ExecuteAddEntity()
    {
        var result = await _dialogService.ShowDialogAsync<EntityDialogResult>(nameof(EditEntityDialogViewModel));
        
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
            if (property.Type != "Name" && property.Value == "")
            {
                if (result.Parent == null)
                {
                    Logger.Error("Error creating entity: mandatory parent not present");
                    throw new Exception("Error creating entity: mandatory parent not present");
                }
                property.Value = result.Parent!.Id.ToString();
            }
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
            if (entity == null) throw new Exception("Error creating entity");
           
            Entities.Add(entity);
            var icon = _entitiesConfiguration!.Definitions[entity.DefinitionName].GetIcon();
            var node = new TreeNode(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
                entity.Id, icon, CreateChildNodes(Entities.ToList(), entity.Id));

            if (entity.Parent != null)
            {
                var nodes = Nodes;
                AddSubNode(ref nodes!, node, entity.Parent.Value);
                Nodes = new ObservableCollection<TreeNode>(nodes!);
            }
            else Nodes!.Add(node);
            
            SelectedNode = node;


            var treeView = _view.FindControl<TreeView>("EntitiesTree");
            treeView.ItemsSource = Nodes;
            ExpandNodes(node, treeView.GetRealizedTreeContainers());

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

    private void ExpandNodes(TreeNode destinationNode, IEnumerable<Control> controls = null)
    {

        foreach (TreeViewItem tvItem in controls)
        {
            if(IsNodeParent(tvItem.ItemsSource, destinationNode))
            {
                tvItem.IsExpanded = true;
            }

            ExpandNodes(destinationNode, tvItem.GetRealizedContainers());
        }
       

        
        //var treeViewItem = (TreeViewItem)treeView.ItemContainerGenerator.Index.ContainerFromItem(treeView.SelectedItem);

    }

    private bool IsNodeParent(IEnumerable? nodes, TreeNode searchNode)
    {
        foreach (TreeNode node in nodes)
        {
            if(node.EntityId == searchNode.EntityId) return true;
            if (IsNodeParent(node.SubNodes!, searchNode)) return true;
        }
        
        return false;
    }

    private void AddSubNode(ref ObservableCollection<TreeNode> nodes, TreeNode subNode, int parentId)
    {
        var parent = nodes.FirstOrDefault(nd => nd.EntityId == parentId);
        if (parent != null)
        {
            parent.SubNodes!.Add(subNode);
            return;
        }

        foreach (var nd in nodes)
        {
            var subNodes = nd.SubNodes;
            AddSubNode(ref subNodes, subNode, parentId);
            nd.SubNodes = subNodes;
        }
    }
    
    private void c_EntitySaved(object? sender, EntitySavedEventHandlerArgs e)
    {
        int index = Entities.ToList().FindIndex(en => en.Id == e.Entity.Id);
        
        Entities[index] = e.Entity;
        
        var nodesCopy = Nodes;

        var parent = e.Entity.Parent;
        string parentStr = "";
        if(parent != null) parentStr = parent.Value.ToString();
        
        UpdateNode(e.Entity.Id, ref nodesCopy, e.Entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value, parentStr);

        Nodes = new ObservableCollection<TreeNode>(nodesCopy);
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
            var icon = _entitiesConfiguration.Definitions[entity.DefinitionName].GetIcon();
            nodes.Add(new TreeNode(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
                entity.Id,
                icon,
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
            var icon = _entitiesConfiguration.Definitions[child.DefinitionName].GetIcon();
            nodes.Add(new TreeNode(child.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
                child.Id,
                icon,
                CreateChildNodes(entities, child.Id)));
        }

        return nodes;
    }
}
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Views;
using Model.Entities;
using ReactiveUI;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using GUIClient.Models.Events;
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
    
    private bool _isSearchVisible;
    
    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        set => this.RaiseAndSetIfChanged(ref _isSearchVisible, value);
    }
    
    private string _searchText = "";
    
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }


    #endregion
    
    #region COMMANDS
    public ReactiveCommand<Unit, Unit> BtAddEntClicked { get; }
    public ReactiveCommand<Unit, Unit> BtEditEntClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteEntClicked { get; }
    public ReactiveCommand<Unit, Unit> BtShowSearchClicked { get; }
    public ReactiveCommand<Unit, Unit> BtExecuteSearchClicked { get; }
    public ReactiveCommand<Unit, Unit> BtReloadClicked { get; }
    
    #endregion

    #region PRIVATE FIELDS
    
    private readonly IAuthenticationService _autenticationService;
    private readonly IEntitiesService _entitiesService;
    private readonly IDialogService _dialogService;

    private EntitiesConfiguration? _entitiesConfiguration;
    private UserControl? _parentWindow;
    
    private Panel? _entityPanel = null;
    
    private Dictionary<int,bool> _expandedNodes = new();
    
    #endregion
    
    #region CONSTRUCTOR

    public EntitiesViewModel(UserControl parentWindow): this()
    {
        //if (view == null) throw new Exception("View is null");
        _parentWindow = parentWindow;
    }

    public EntitiesViewModel()
    {
        StrEntities = Localizer["Entities"];
        StrEntity = Localizer["Entity"];
        
        BtAddEntClicked = ReactiveCommand.CreateFromTask(ExecuteAddEntity);
        BtEditEntClicked = ReactiveCommand.CreateFromTask(ExecuteEditEntity);
        BtDeleteEntClicked = ReactiveCommand.CreateFromTask(ExecuteDeleteEntity);
        BtShowSearchClicked = ReactiveCommand.CreateFromTask(ExecuteShowSearch);
        BtExecuteSearchClicked = ReactiveCommand.CreateFromTask(ExecuteSearch);
        BtReloadClicked = ReactiveCommand.CreateFromTask(Reload);
        
        _autenticationService = GetService<IAuthenticationService>();
        _entitiesService = GetService<IEntitiesService>();
        _dialogService = GetService<IDialogService>();
        
        _autenticationService.AuthenticationSucceeded += (_, _) =>
        {
            _ = InitializeAsync();
        };
        
    }
    
    #endregion

    #region METHODS


    private async Task Reload()
    {
        _entitiesService.ClearCache();
        await LoadDataAsync();
    }

    private async Task InitializeAsync()
    {
        if (_parentWindow == null) throw new Exception("View is null");
        _entityPanel = _parentWindow.FindControl<Panel>("EntityPanel");
        
        await LoadDataAsync();

    }

    private async Task ExecuteEditEntity()
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
                            
            _ = msgBox1.ShowAsync();
            return;
        }

        var entity = Entities.FirstOrDefault(ent => ent.Id == SelectedNode.EntityId);

        if (entity == null)
        {
            Logger.Error("Unexpected error on entity selection");
            throw new Exception("Unexpected error on entity selection");
        }

        var parameter = new EntityDialogParameter()
        {
            Title = Localizer["EditEntity"],
            Name = entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
            Type = entity.DefinitionName,
            IsNew = false,
            Parent = entity.Parent != null ? Entities.FirstOrDefault(e => e.Id == entity.Parent) : null
        };
        
        var dialogEdit = await _dialogService.ShowDialogAsync<EntityDialogResult, EntityDialogParameter>(nameof(EditEntityDialogViewModel), parameter);
        
        if(dialogEdit == null) return;
        
        Logger.Debug("Edit entity dialog result: {@Result}", dialogEdit.Result);
        
        if(dialogEdit.Result == 0) return;
      
        var nodesCopy = Nodes;

        if (nodesCopy == null)
        {
            Logger.Error("Nodes is null and it should not be");
            throw new Exception("Nodes is null");
        }

        if (dialogEdit.Name == null)
        {
            Logger.Error("Name is null and it should not be");
            throw new Exception("Name is null");
        }

        var parent = dialogEdit.Parent?.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value;

        Entity? parentEntity = null;
        if (parent == null)
        {
            parent = "---";
        }
        else
        {
            parentEntity = Entities.FirstOrDefault(e => e.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value == parent);
        }

        var properties = new List<EntitiesPropertyDto>();

        foreach (var property in entity.EntitiesProperties)
        {
            if(property.Type == "name")
            {
                property.Value = dialogEdit.Name;
            }
            properties.Add(new EntitiesPropertyDto()
            {
                Id = property.Id,
                Name = property.Name,
                Type = property.Type,
                Value = property.Value
            });
        }
        
        var updated = new EntityDto()
        {
            Id = entity.Id,
            DefinitionName = entity.DefinitionName,
            Parent = parentEntity?.Id,
            Status = entity.Status,
            EntitiesProperties = properties
        };

        _ = _entitiesService.SaveEntityAsync(updated);
 

        var idx = Entities.ToList().FindIndex(e => e.Id == SelectedNode.EntityId);
        
        Entities[idx].Parent = parentEntity?.Id;
        
        _entityPanel!.Children.Clear();
        SelectedNode = null;
        SaveExpansionStatus();
        _entitiesService.ClearCache();
        await LoadDataAsync();
        ApplyExpansionStatus();


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
            
            if(subNodes == null) continue;

            if (!UpdateNode(entityId, ref subNodes, name, parent)) continue;
            subNode.SubNodes = subNodes;
            return true;


        }
        return false;
    }

    private async Task ExecuteDeleteEntity()
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

            _ = msgBox1.ShowAsync();
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
            SaveExpansionStatus();
            _entitiesService.ClearCache();
            await LoadDataAsync();
            ApplyExpansionStatus();
        }
        
    }

    private TreeNode? FindLeafNode(int entityId, ObservableCollection<TreeNode> nodes)
    {
        var rootNode = nodes.FirstOrDefault(nd => nd.EntityId == entityId);
        if (rootNode != null) return rootNode;

        foreach (var node in nodes)
        {
            var resultNode = FindLeafNode(entityId, node.SubNodes!);
            if (resultNode != null) return resultNode;
        }

        return null;
    }
    
    private void DeleteNode(int entityId)
    {
        //Let´s find the node
        //var rootNode = Nodes?.FirstOrDefault(nd => nd.EntityId == entityId);
        var rootNode = FindLeafNode(entityId, Nodes!);

        //Now let´s delete the children 
        if (rootNode != null)
        {
            foreach (var subNode in rootNode.SubNodes!)
            {
                DeleteNode(subNode.EntityId);
            }
            var ent = Entities.FirstOrDefault(e => e.Id == entityId);
            _entitiesService.Delete(rootNode.EntityId);
            
            
            var node = Nodes!.FirstOrDefault(nd => nd.EntityId == entityId);

            if (node != null)
            {
                node.IsVisible = false;
                Nodes!.Remove(node);
                
            }
            
            SelectedNode = null;
            
            if(ent != null) Entities.Remove(ent);
            
        }
    }

    private async Task ExecuteAddEntity()
    {
        
        SaveExpansionStatus();
        
        var result = await _dialogService.ShowDialogAsync<EntityDialogResult>(nameof(EditEntityDialogViewModel));
        
        if(result == null) return;
        
        Logger.Debug("Add entity dialog result: {@Result}", result.Result);
        if(result.Result == 0) return;
        
        Logger.Debug("Creating new entity named: {@Entity}", result.Name);

        int? parentId = null;
        if(result.Parent != null && result.Parent.DefinitionName != "---") parentId = result.Parent.Id;

        if (result.Type == null)
        {
            Logger.Error("Error creating entity: type is null");
            throw new Exception("Error creating entity: type is null");
        }
        var properties = await _entitiesService.GetMandatoryPropertiesAsync(result.Type);
        
        var nameIndex = properties.FindIndex(p => p.Type == "name");
        
        if (result.Name == null)
        {
            Logger.Error("Error creating entity: Name is null");
            throw new Exception("Error creating entity: Name is null");
        }
        
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
                Nodes = new ObservableCollection<TreeNode>(nodes);
            }
            else Nodes!.Add(node);
            
            SelectedNode = node;
            ApplyExpansionStatus();

            if (_parentWindow == null)
            {
                Logger.Error("View is null");
                throw new Exception("View is null");
            }
            
            var treeView = _parentWindow.FindControl<TreeView>("EntitiesTree");
            //treeView.ItemsSource = Nodes;
            
            if(treeView == null)
            {
                Logger.Error("TreeView is null");
                throw new Exception("TreeView is null");
            }
            
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
    
    private void SaveExpansionStatus()
    {
        if (_parentWindow == null) throw new Exception("View is null");
        var treeView = _parentWindow.FindControl<TreeView>("EntitiesTree");
        if(treeView == null) throw new Exception("TreeView is null");
     
        _expandedNodes.Clear();
        
        var nodes = treeView.GetRealizedTreeContainers();
        foreach (var node in nodes)
        {
            var tvItem = (TreeViewItem) node;
            if(tvItem.IsExpanded)
            {
                var nodeId = ((TreeNode) tvItem.DataContext!).EntityId;
                _expandedNodes[nodeId] = true;
            }
        }
    }

    private void ApplyExpansionStatus()
    {
        if (_parentWindow == null) throw new Exception("View is null");
        var treeView = _parentWindow.FindControl<TreeView>("EntitiesTree");
        if(treeView == null) throw new Exception("TreeView is null");
        var nodes = treeView.GetRealizedTreeContainers();

        foreach (var node in nodes)
        {
            var tvItem = (TreeViewItem) node;
            var nodeId =  ((TreeNode) tvItem.DataContext!).EntityId;
            if(_expandedNodes.ContainsKey(nodeId))
            {
                tvItem.IsExpanded = true;
            }
        }
        
    }

    private void ExpandNodes(TreeNode destinationNode, IEnumerable<Control> controls)
    {

        var treeView = _parentWindow!.FindControl<TreeView>("EntitiesTree");
        if(treeView == null) throw new Exception("TreeView is null");
        
        foreach (var control in controls)
        {
            var tvItem = (TreeViewItem) control;
            if(IsNodeParent(tvItem.ItemsSource, destinationNode))
            {
                //tvItem.IsExpanded = true;

                treeView.ExpandSubTree(tvItem);

                var inControls = tvItem.GetRealizedContainers();
                
                ExpandNodes(destinationNode, inControls);
            }

            
        }

    }

    private bool IsNodeParent(IEnumerable? nodes, TreeNode searchNode)
    {
        if(nodes == null) return false;
        
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
            if (subNodes == null) continue;
            AddSubNode(ref subNodes, subNode, parentId);
            nd.SubNodes = subNodes;
        }
    }
    
    private void c_EntitySaved(object? sender, EntitySavedEventHandlerArgs e)
    {
        if(e.Entity == null) return;
        
        int index = Entities.ToList().FindIndex(en => en.Id == e.Entity.Id);
        
        Entities[index] = e.Entity;
        
        var nodesCopy = Nodes;
        
        if(nodesCopy == null)
        {
            Logger.Error("Nodes is null and it should not be");
            throw new Exception("Nodes is null");
        }

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

    private void LoadTree(List<Entity> rootEntities)
    {
        var nodes = new ObservableCollection<TreeNode>();
        
        foreach (var entity in rootEntities)
        {
            var icon = _entitiesConfiguration!.Definitions[entity.DefinitionName].GetIcon();
            nodes.Add(new TreeNode(entity.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
                entity.Id,
                icon,
                CreateChildNodes(Entities.ToList(), entity.Id)));
        }

        Nodes = new ObservableCollection<TreeNode>(nodes.OrderBy(nd => nd.Title));
    }

    private async Task LoadDataAsync()
    {
        
        if(_entitiesConfiguration == null)
            _entitiesConfiguration = await _entitiesService.GetEntitiesConfigurationAsync();
        
        var allEntities = await _entitiesService.GetAllAsync();
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
            var icon = _entitiesConfiguration!.Definitions[child.DefinitionName].GetIcon();
            nodes.Add(new TreeNode(child.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value,
                child.Id,
                icon,
                CreateChildNodes(entities, child.Id)));
        }

        return  new ObservableCollection<TreeNode>(nodes.OrderBy(tn => tn.Title));
    }

    private Task ExecuteShowSearch()
    {
        IsSearchVisible = !IsSearchVisible;
        return Task.CompletedTask;
    }
    
    private async Task ExecuteSearch()
    {
        //if(Entities == null) return;
        
        var entity = await Entities.ToAsyncEnumerable().FirstOrDefaultAsync(e => e.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")!.Value == SearchText);
        
        if(entity == null)
        {
            var msgBox = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["EntityNotFoundMSG"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox.ShowAsync();
            return;
        }
        
        //var node = Nodes?.FirstOrDefault(nd => nd.EntityId == entity.Id);
        
        var node = FindLeafNode(entity.Id, Nodes!);
        
        if(node == null)throw new Exception("Node is null");
        
        var treeView = _parentWindow!.FindControl<TreeView>("EntitiesTree");
        if(treeView == null) throw new Exception("TreeView is null");

        var controls = treeView.GetRealizedTreeContainers();
        
        ExpandNodes(node, controls);
        
        SelectedNode = node;
        
    }

    #endregion
    
}
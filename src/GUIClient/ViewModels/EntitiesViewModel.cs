using System.Collections.ObjectModel;
using Avalonia.Collections;
using Avalonia.Controls.Generators;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClientServices.Interfaces;
using GUIClient.Models;
using Model.Entities;
using ReactiveUI;


namespace GUIClient.ViewModels;

public class EntitiesViewModel: ViewModelBase
{
    #region LANGUAGE STRINGS
    
    public string StrEntities { get; }
    

    #endregion

    #region PROPERTIES

    private ObservableCollection<TreeNode>? _nodes;
    public ObservableCollection<TreeNode>? Nodes
    {
        get => _nodes;
        set => this.RaiseAndSetIfChanged(ref _nodes, value);
    }
    

    #endregion

    #region PRIVATE FIELDS
    
    private readonly IAuthenticationService _autenticationService;
    private readonly IEntitiesService _entitiesService;

    private EntitiesConfiguration? _entitiesConfiguration;
    
    #endregion
    
    public EntitiesViewModel()
    {
        StrEntities = Localizer["Entities"];
        
        _autenticationService = GetService<IAuthenticationService>();
        _entitiesService = GetService<IEntitiesService>();
        
        _autenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };
        
    }

    private void Initialize()
    {
        LoadTree();
    }
    
    private async void LoadTree()
    {
        
        if(_entitiesConfiguration == null)
            _entitiesConfiguration = await _entitiesService.GetEntitiesConfigurationAsync();
        
        Nodes = new ObservableCollection<TreeNode>
        {                
            new TreeNode("Animals", new ObservableCollection<TreeNode>
            {
                new TreeNode("Mammals", new ObservableCollection<TreeNode>
                {
                    new TreeNode("Lion"), new TreeNode("Cat"), new TreeNode("Zebra")
                })
            })
        };
    }
}
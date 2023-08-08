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


namespace GUIClient.ViewModels;

public class EntitiesViewModel: ViewModelBase
{
    #region LANGUAGE STRINGS
    
    public string StrEntities { get; }
    

    #endregion

    #region PROPERTIES

    public ObservableCollection<TreeNode> Nodes { get; set; }
    

    #endregion

    #region PRIVATE FIELDS
    
    private readonly IAuthenticationService _autenticationService;
    

    #endregion
    
    public EntitiesViewModel()
    {
        StrEntities = Localizer["Entities"];
        
        
        _autenticationService = GetService<IAuthenticationService>();
        
        _autenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };
        
    }

    private void Initialize()
    {
        LoadTree();
    }
    
    private void LoadTree()
    {
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
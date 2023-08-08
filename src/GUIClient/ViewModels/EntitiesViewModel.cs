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


namespace GUIClient.ViewModels;

public class EntitiesViewModel: ViewModelBase
{
    #region LANGUAGE STRINGS
    
    public string StrEntities { get; }
    

    #endregion

    #region PROPERTIES

    //public ObservableCollection<Node> Items { get; }
    //public ObservableCollection<Node> SelectedItems { get; }
    

    #endregion

    #region PRIVATE FIELDS

    

    #endregion
    
    public EntitiesViewModel()
    {
        StrEntities = Localizer["Entities"];
    }
}
using System.Collections.ObjectModel;
using Contracts;
using ReactiveUI;

namespace GUIClient.ViewModels.Admin;

public class PluginsViewModel: ViewModelBase
{
    #region LANGUAGE
        private string StrTitle { get;  } = Localizer["Plugins"];
    #endregion
    
    #region PROPERTIES
        private ObservableCollection<INetriskModelPlugin> _pluginsList = new();
        
        public ObservableCollection<INetriskModelPlugin> PluginsList
        {
            get => _pluginsList;
            set => this.RaiseAndSetIfChanged(ref _pluginsList, value);
        }
        
    #endregion
}
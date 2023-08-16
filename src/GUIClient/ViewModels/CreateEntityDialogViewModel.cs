using System.Collections.Generic;
using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DynamicData;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Entities;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class CreateEntityDialogViewModel: DialogViewModelBase<IntegerDialogResult>
{
    #region LANGUAGE

        public string StrTitle { get; set; }
        public string StrName { get; set; }
        public string StrParent{ get; set; }
        public string StrSave{ get; set; }
        public string StrCancel{ get; set; }
        public string StrType { get; set; }

    #endregion

    #region PROPERTIES

        public IntegerDialogResult Result { get; set; }
        public string Name { get; set; }

        private ObservableCollection<string>? _entityTypes;
        //public ObservableCollection<string> EntityTypes { get; set; }
        
        public ObservableCollection<string>? EntityTypes
        {
            get => _entityTypes ??= new ObservableCollection<string>();
            set => this.RaiseAndSetIfChanged(ref _entityTypes, value);
        }
        
        
        private string? _selectedEntityType;
        public string? SelectedEntityType
        {
            get => _selectedEntityType;
            set => this.RaiseAndSetIfChanged(ref _selectedEntityType, value);
        }


    #endregion
    
    #region PRIVATE FIELDS

    private IEntitiesService _entitiesService;
    private EntitiesConfiguration? _entitiesConfiguration;
    
    #endregion
    
    public CreateEntityDialogViewModel() : base()
    {
        StrTitle = Localizer["CreateEntity"];
        StrName = Localizer["Name"] + ":";
        StrParent = Localizer["Parent"] + ":";
        StrSave = Localizer["Save"];
        StrCancel = Localizer["Cancel"];
        StrType = Localizer["Type"] + ":";
            
        this.Result = new IntegerDialogResult();

        _entitiesService = GetService<IEntitiesService>();

        LoadEntitesTypes();
    }

    private async void LoadEntitesTypes()
    {
        if (_entitiesConfiguration == null)
            _entitiesConfiguration = await _entitiesService.GetEntitiesConfigurationAsync();

        var etl = new List<string> {"---"};
        EntityTypes = new ObservableCollection<string>(etl);
        
        foreach ( var defs  in _entitiesConfiguration.Definitions)
        {
            EntityTypes.Add(defs.Key);
        }
        
    }
}
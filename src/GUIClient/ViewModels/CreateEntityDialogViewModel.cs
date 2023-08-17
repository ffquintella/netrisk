using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using DynamicData;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Entities;
using ReactiveUI;
using System.Reactive;

namespace GUIClient.ViewModels;

public class CreateEntityDialogViewModel: DialogViewModelBase<EntityDialogResult>
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

        public EntityDialogResult Result { get; set; }
        
        

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
            set
            {
                if(value == null) return;
                var allowedChildren = _entitiesConfiguration!.Definitions[value].AllowedChildren;
                if(allowedChildren == null) allowedChildren = new List<string>();
                if(allowedChildren.Count == 0)
                    allowedChildren.Add("---");
                
                FilteredEntities = new ObservableCollection<Entity>(Entities!.Where(e => allowedChildren.Contains(e.DefinitionName)));
                this.RaiseAndSetIfChanged(ref _selectedEntityType, value);
            }
        }

        private ObservableCollection<Entity>? _entities;
        public ObservableCollection<Entity>? Entities
        {
            get => _entities;
            set => this.RaiseAndSetIfChanged(ref _entities, value);
        }
        
        private ObservableCollection<Entity>? _filteredEntities;
        public ObservableCollection<Entity>? FilteredEntities
        {
            get => _filteredEntities;
            set => this.RaiseAndSetIfChanged(ref _filteredEntities, value);
        }

        private string? _selectedEntity;
        public string? SelectedEntity
        {
            get => _selectedEntity;
            set
            {
                if (value == null) return;
                this.RaiseAndSetIfChanged(ref _selectedEntity, value);   
            }
        }

        private string? _name = "";
        public string? Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
        public ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
        
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
            
        this.Result = new EntityDialogResult();

        _entitiesService = GetService<IEntitiesService>();
        
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);

        LoadEntitesTypes();
        LoadEntities();
    }

    private void ExecuteSave()
    {
        Result.Result = 1;
        Result.Name = Name;
        Result.Type = SelectedEntityType;
        Result.Parent = Entities!.FirstOrDefault(e => e.EntitiesProperties.Any(ep => ep.Type == "name" && ep.Value == SelectedEntity));
        Close(Result);
    }
    
    private void ExecuteCancel()
    {
        Result.Result = 0;
        Close(Result);
    }
    private async void LoadEntities()
    {
        if (Entities == null)
        {
            await Task.Run(() =>
            {
                Entities = new ObservableCollection<Entity>(_entitiesService.GetAll());
            });
        }
    }
    
    private async void LoadEntitesTypes()
    {
        if (_entitiesConfiguration == null)
            _entitiesConfiguration = await _entitiesService.GetEntitiesConfigurationAsync();

        //var etl = new List<string> {"---"};
        EntityTypes = new ObservableCollection<string>();
        
        foreach ( var defs  in _entitiesConfiguration.Definitions)
        {
            EntityTypes.Add(defs.Key);
        }
        
    }
}
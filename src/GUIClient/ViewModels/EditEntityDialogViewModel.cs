using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DynamicData;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Entities;
using ReactiveUI;
using System.Reactive;
using System.Threading;
using GUIClient.ViewModels.Dialogs.Parameters;
using ReactiveUI.Validation.Extensions;

namespace GUIClient.ViewModels;

public class EditEntityDialogViewModel: ParameterizedDialogViewModelBaseAsync<EntityDialogResult, EntityDialogParameter>
{
    #region LANGUAGE

        //public string StrTitle { get; set; }
        
        private string? _strTitle;
        
        public string? StrTitle
        {
            get => _strTitle;
            set => this.RaiseAndSetIfChanged(ref _strTitle, value);
        }
        public string StrName { get; set; }
        public string StrParent{ get; set; }
        public string StrSave{ get; set; }
        public string StrCancel{ get; set; }
        public string StrType { get; set; }

    #endregion

    #region PROPERTIES

        private bool _isNew = true;
        
        public bool IsNew
        {
            get => _isNew;
            set => this.RaiseAndSetIfChanged(ref _isNew, value);
        }
        
        public EntityDialogResult Result { get; set; }
        
        private Dictionary<string,string> _entityTypesTranslations = new();

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

                if (!_entityTypesTranslations.ContainsKey(value)) return;
                
                FilteredEntities = new ObservableCollection<Entity>(Entities!.AsParallel().Where(e =>
                {
                    if(e.DefinitionName == "---") return true;
                    var allowedChildren = _entitiesConfiguration!.Definitions[e.DefinitionName].AllowedChildren;
                    return allowedChildren != null && allowedChildren.Contains(_entityTypesTranslations[value]);
                    
                }).OrderBy(e => e.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")?.Value).ToList());
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

        private bool _saveEnabled;
    
        public bool SaveEnabled
        {
            get => _saveEnabled;
            set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
        }
        
        public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
        public ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
        
    #endregion
    
    #region PRIVATE FIELDS

    private IEntitiesService _entitiesService;
    private EntitiesConfiguration? _entitiesConfiguration;
    

    
    #endregion
    
    public EditEntityDialogViewModel()
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

        _= LoadEntitesTypesAsync();
        _= LoadEntitiesAsync();
        
        this.ValidationRule(
            viewModel => viewModel.Name, 
            name => name is {Length: > 0},
            Localizer["PleaseEnterAValueMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedEntityType, 
            etype => etype != null,
            Localizer["PleaseSelectOneMSG"]);

        this.IsValid()
            .Subscribe( x =>
            {
                SaveEnabled = x;
            });
    }


    
    private void ExecuteSave()
    {
        if (Name is {Length: > 0} && SelectedEntityType != null)
        {
            Result.Result = 1;
            Result.Name = Name;
            Result.Type = _entityTypesTranslations[SelectedEntityType];
            Result.Parent = Entities!.FirstOrDefault(e => e.EntitiesProperties.Any(ep => ep.Type == "name" && ep.Value == SelectedEntity));
            Close(Result);
        }
    }
    
    private void ExecuteCancel()
    {
        Result.Result = 0;
        Close(Result);
    }
    private async Task LoadEntitiesAsync()
    {
        if (Entities == null)
        {
            
            var blank = new List<EntitiesProperty> { new EntitiesProperty()
            {
                Id = -1,
                Name = "---",
                Type = "name",
                Value = "---",
                Entity = -1
            } };

            Entities = new ObservableCollection<Entity>
            {
                new Entity()
                {
                    DefinitionName = "---",
                    EntitiesProperties = blank
                }
            };

            Entities.AddRange(new ObservableCollection<Entity>(await _entitiesService.GetAllAsync() ));
            
        }
    }
    
    private async Task LoadEntitesTypesAsync()
    {
        if (_entitiesConfiguration == null)
            _entitiesConfiguration = await _entitiesService.GetEntitiesConfigurationAsync();

        if (EntityTypes is { Count: > 0 }) return;
        

        EntityTypes = new ObservableCollection<string>();
        _entityTypesTranslations = new Dictionary<string, string>();
        
        foreach ( var defs  in _entitiesConfiguration.Definitions)
        {
            var locStrValue = Localizer[defs.Key];
            string strValue = locStrValue.Value;
            if (locStrValue.ResourceNotFound || strValue == "") strValue = defs.Key;
            EntityTypes.Add(strValue);
            _entityTypesTranslations.Add(strValue, defs.Key);
        }
        
    }

    public override async Task ActivateAsync(EntityDialogParameter parameter, CancellationToken cancellationToken = default)
    {

        await LoadEntitiesAsync();
        await LoadEntitesTypesAsync();
        StrTitle = parameter.Title;
        Name = parameter.Name;
        IsNew = parameter.IsNew;
        SelectedEntityType = _entityTypesTranslations.FirstOrDefault(et => et.Value == parameter.Type).Key;
        SelectedEntity = parameter.Parent?.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")?.Value;
            
    }
}
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
using System.Reactive.Linq;
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

        public EntityDialogResult Result { get; set; }
        
        private Dictionary<string,string> entityTypesTranslations = new();

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
                
                FilteredEntities = new ObservableCollection<Entity>(Entities!.Where(e =>
                {
                    if(e.DefinitionName == "---") return true;
                    var allowedChildren = _entitiesConfiguration!.Definitions[e.DefinitionName].AllowedChildren;
                    return allowedChildren != null && allowedChildren.Contains(entityTypesTranslations[value]);
                    
                }));
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
    
    public EditEntityDialogViewModel() : base()
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
        
        this.ValidationRule(
            viewModel => viewModel.Name, 
            name => name is {Length: > 0},
            Localizer["PleaseEnterAValueMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedEntityType, 
            etype => etype != null,
            Localizer["PleaseSelectOneMSG"]);
    }

    private async void ExecuteSave()
    {
        if (Name is {Length: > 0} && SelectedEntityType != null)
        {
            Result.Result = 1;
            Result.Name = Name;
            Result.Type = entityTypesTranslations[SelectedEntityType];
            Result.Parent = Entities!.FirstOrDefault(e => e.EntitiesProperties.Any(ep => ep.Type == "name" && ep.Value == SelectedEntity));
            Close(Result);
        }
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

                Entities.AddRange(new ObservableCollection<Entity>(_entitiesService.GetAll()));

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
            var locStrValue = Localizer[defs.Key];
            string strValue = locStrValue.Value;
            if (locStrValue.ResourceNotFound || strValue == "") strValue = defs.Key;
            EntityTypes.Add(strValue);
            entityTypesTranslations.Add(strValue, defs.Key);
        }
        
    }

    public override Task ActivateAsync(EntityDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            LoadEntities();
            LoadEntitesTypes();
            StrTitle = parameter.Title;
            Name = parameter.Name;
            SelectedEntityType = parameter.Type;
            SelectedEntity = parameter.Parent?.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")?.Value;

        });
        
    }
}
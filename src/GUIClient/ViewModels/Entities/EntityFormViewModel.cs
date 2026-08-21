using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaExtraControls.Models;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models.Events;
using Model.DTO;
using Model.Entities;
using ReactiveUI;
using Tools.String;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Entities;

/// <summary>
/// The entity form's view-model. Replaces the imperative form construction that used to live in
/// <c>EntityForm.axaml.cs</c> (IX-9), and closes the IX-4 gaps that came with it: the form now has
/// a Cancel alongside Save, tracks dirty state, and <b>enforces</b> its validation rather than
/// showing an inline error and saving anyway.
/// </summary>
public class EntityFormViewModel : ViewModelBase
{
    #region LANGUAGE

    public new string StrSave => Localizer["Save"];
    public new string StrCancel => Localizer["Cancel"];
    public string StrHasErrors => Localizer["EntityFormHasErrorsMSG"];

    #endregion

    #region PROPERTIES

    private ObservableCollection<EntityFieldViewModel> _fields = new();
    public ObservableCollection<EntityFieldViewModel> Fields
    {
        get => _fields;
        private set => this.RaiseAndSetIfChanged(ref _fields, value);
    }

    private bool _saveEnabled;

    /// <summary>True only when every field is satisfied and something has actually changed.</summary>
    public bool SaveEnabled
    {
        get => _saveEnabled;
        private set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
    }

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    /// <summary>Aggregated field errors, for the inline summary above the action row (IX-4).</summary>
    public string ErrorSummary => string.Join(Environment.NewLine,
        Fields.Where(f => f.HasError).Select(f => $"{f.Label}: {f.Error}").Distinct());

    public bool HasErrors => Fields.Any(f => f.HasError);

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelCommand { get; }

    #endregion

    #region EVENTS

    public event EventHandler<EntitySavedEventHandlerArgs>? EntitySaved;

    #endregion

    #region FIELDS

    private readonly IEntitiesService _entitiesService = GetService<IEntitiesService>();
    private Entity? _entity;
    private EntityDefinition? _definition;

    #endregion

    #region CONSTRUCTOR

    public EntityFormViewModel()
    {
        SaveCommand = ReactiveCommand.CreateFromTask(ExecuteSaveAsync,
            this.WhenAnyValue(x => x.SaveEnabled));
        CancelCommand = ReactiveCommand.Create(ExecuteCancel);
    }

    #endregion

    #region METHODS

    /// <summary>Builds the field list for <paramref name="entity"/> from its definition.</summary>
    public void Load(Entity entity, EntitiesConfiguration configuration)
    {
        _entity = entity;
        _definition = configuration.Definitions[entity.DefinitionName];

        var strAvailable = Localizer["Available"];
        var strSelected = Localizer["Selected"];
        var required = Localizer["PleaseEnterAValueMSG"];

        var fields = new ObservableCollection<EntityFieldViewModel>();

        foreach (var (key, type) in _definition.Properties)
        {
            var values = entity.EntitiesProperties.Where(ep => ep.Type == key).ToList();

            var field = BuildField(key, type, values);
            field.StrAvailable = strAvailable;
            field.StrSelected = strSelected;
            field.RequiredMessage = required;
            field.ValidityChanged = OnFieldChanged;
            field.Revalidate();

            fields.Add(field);
        }

        Fields = fields;
        IsDirty = false;
        Recompute();
    }

    private EntityFieldViewModel BuildField(string key, EntityType type, List<EntitiesProperty> values)
    {
        var label = LocalizedLabel(type.Label);
        var propertyId = values.Count > 0 ? values[0].Id : 0;

        if (type.Type.StartsWith("Definition"))
        {
            var kind = type.Multiple
                ? EntityFieldViewModel.FieldKind.MultiSelect
                : EntityFieldViewModel.FieldKind.SingleSelect;

            var field = new EntityFieldViewModel(key, label, type, kind, propertyId);

            var definitionName = LabelIdParser.ExtractParenthesizedValue(type.Type);
            if (definitionName == null) return field;

            var options = _entitiesService.GetAll(definitionName)
                .Select(e => new SelectEntity(
                    e.Id.ToString(),
                    e.EntitiesProperties.FirstOrDefault(ep => ep.Type == "name")?.Value ?? e.Id.ToString()))
                .OrderBy(o => o.Label)
                .ToList();

            if (type.Multiple)
            {
                var selectedKeys = values.Select(v => v.Value).ToHashSet();
                field.SelectedItems = options.Where(o => selectedKeys.Contains(o.Key)).ToList();
                field.AvailableItems = options.Where(o => !selectedKeys.Contains(o.Key)).ToList();
            }
            else
            {
                field.Options = options;
                if (values.Count > 0)
                {
                    field.SelectedOption = options.FirstOrDefault(o => o.Key == values[0].Value);
                }
            }

            return field;
        }

        switch (type.Type)
        {
            case "Integer":
            {
                var field = new EntityFieldViewModel(key, label, type,
                    EntityFieldViewModel.FieldKind.Integer, propertyId);
                var raw = values.Count > 0 ? values[0].Value : type.DefaultValue ?? "0";
                field.IntegerValue = decimal.TryParse(raw, out var parsed) ? parsed : 0;
                return field;
            }
            case "Boolean":
            {
                var field = new EntityFieldViewModel(key, label, type,
                    EntityFieldViewModel.FieldKind.Boolean, propertyId);
                var raw = values.Count > 0 ? values[0].Value : type.DefaultValue ?? "false";
                field.BooleanValue = bool.TryParse(raw, out var parsed) && parsed;
                return field;
            }
            default:
            {
                var field = new EntityFieldViewModel(key, label, type,
                    EntityFieldViewModel.FieldKind.Text, propertyId);
                field.TextValue = values.Count > 0 ? values[0].Value : type.DefaultValue ?? "";
                return field;
            }
        }
    }

    /// <summary>Falls back to the raw definition label when the resource is missing.</summary>
    private static string LocalizedLabel(string label)
    {
        var localized = Localizer[label];
        return localized.ResourceNotFound || string.IsNullOrEmpty(localized.Value) ? label : localized.Value;
    }

    private void OnFieldChanged()
    {
        IsDirty = true;
        Recompute();
    }

    private void Recompute()
    {
        SaveEnabled = Fields.All(f => f.IsValid) && IsDirty;

        this.RaisePropertyChanged(nameof(ErrorSummary));
        this.RaisePropertyChanged(nameof(HasErrors));
    }

    private async Task ExecuteSaveAsync()
    {
        if (_entity == null || _definition == null) return;

        // IX-4: enforced, not merely displayed. The old form showed an inline error and saved anyway.
        foreach (var field in Fields) field.Revalidate();
        Recompute();

        if (Fields.Any(f => !f.IsValid)) return;

        var dto = new EntityDto
        {
            Id = _entity.Id,
            DefinitionName = _entity.DefinitionName,
            Status = _entity.Status,
            Parent = _entity.Parent,
            EntitiesProperties = new List<EntitiesPropertyDto>()
        };

        foreach (var field in Fields)
        {
            switch (field.Kind)
            {
                case EntityFieldViewModel.FieldKind.Text:
                    dto.EntitiesProperties.Add(Property(field, field.TextValue));
                    break;

                case EntityFieldViewModel.FieldKind.Integer:
                    dto.EntitiesProperties.Add(Property(field, Convert.ToUInt32(field.IntegerValue).ToString()));
                    break;

                case EntityFieldViewModel.FieldKind.Boolean:
                    dto.EntitiesProperties.Add(Property(field, field.BooleanValue.ToString().ToLower()));
                    break;

                case EntityFieldViewModel.FieldKind.SingleSelect:
                    if (field.SelectedOption != null)
                        dto.EntitiesProperties.Add(Property(field, field.SelectedOption.Key));
                    break;

                case EntityFieldViewModel.FieldKind.MultiSelect:
                    // A multi-valued property persists one row per selection, each keyed by value.
                    foreach (var item in field.SelectedItems)
                    {
                        dto.EntitiesProperties.Add(new EntitiesPropertyDto
                        {
                            Id = field.PropertyId,
                            Name = $"{field.PropertyKey}-{_entity.Id}-{item.Key}",
                            Type = field.PropertyKey,
                            Value = item.Key
                        });
                    }
                    break;
            }
        }

        var result = await _entitiesService.SaveEntityAsync(dto);

        if (result == null) return;

        _entity = result;
        IsDirty = false;
        Recompute();

        EntitySaved?.Invoke(this, new EntitySavedEventHandlerArgs { Entity = result });

        Toasts.Success(Localizer["EntitySavedSuccessMSG"]);
    }

    private EntitiesPropertyDto Property(EntityFieldViewModel field, string value) => new()
    {
        Id = field.PropertyId,
        Name = $"{field.PropertyKey}-{_entity!.Id}",
        Type = field.PropertyKey,
        Value = value
    };

    /// <summary>Discards edits by rebuilding the fields from the entity as last loaded/saved.</summary>
    private void ExecuteCancel()
    {
        if (_entity == null || _definition == null) return;

        Load(_entity, new EntitiesConfiguration
        {
            Definitions = new Dictionary<string, EntityDefinition> { [_entity.DefinitionName] = _definition }
        });
    }

    #endregion
}

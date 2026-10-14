using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AvaloniaExtraControls.Models;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models.Events;
using GUIClient.Tools;
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

    /// <summary>The notice shown instead of an editable form to a user without <c>entities_manage</c> (T241).</summary>
    public string StrEntitiesManageRequired => Localizer["EntitiesManageRequiredMSG"];

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

    /// <summary>Whether the user may save this form at all: <c>entities_manage</c> or the Admin role (T241).</summary>
    public bool CanManage { get; private set; }

    public bool IsReadOnlyForm => !CanManage;

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
        var riskContextLocked = Localizer["EntityRiskContextRequiredMSG"];
        var notSet = Localizer["EntityFieldNotSet"];
        var booleanNotSet = Localizer["EntityBooleanNotSetMSG"];

        var user = AuthenticationService.AuthenticatedUserInfo;
        CanManage = EntityAccess.CanManage(user);
        this.RaisePropertyChanged(nameof(CanManage));
        this.RaisePropertyChanged(nameof(IsReadOnlyForm));

        var fields = new ObservableCollection<EntityFieldViewModel>();

        foreach (var (key, type) in _definition.Properties)
        {
            var values = entity.EntitiesProperties.Where(ep => ep.Type == key).ToList();

            var access = new FieldAccess(
                EntityAccess.IsFieldReadOnly(key, user),
                EntityAccess.IsRiskContextLocked(key, user) ? riskContextLocked.Value : "",
                values.Select(v => v.Value).ToList());

            var field = BuildField(key, type, values, access);
            field.StrAvailable = strAvailable;
            field.StrSelected = strSelected;
            field.StrNotSet = notSet;
            field.StrBooleanNotSet = booleanNotSet;
            field.RequiredMessage = required;
            field.ValidityChanged = OnFieldChanged;
            field.Revalidate();

            fields.Add(field);
        }

        Fields = fields;
        IsDirty = false;
        Recompute();
    }

    /// <summary>What T241 decides about one field: read-only or not, why, and the values to save back if read-only.</summary>
    private sealed record FieldAccess(bool IsReadOnly, string Reason, IReadOnlyList<string> OriginalValues);

    private EntityFieldViewModel BuildField(string key, EntityType type, List<EntitiesProperty> values, FieldAccess access)
    {
        var label = LocalizedLabel(type.Label);
        var propertyId = values.Count > 0 ? values[0].Id : 0;

        EntityFieldViewModel New(EntityFieldViewModel.FieldKind kind) => new(key, label, type, kind, propertyId)
        {
            IsReadOnly = access.IsReadOnly,
            ReadOnlyReason = access.Reason,
            OriginalValues = access.OriginalValues
        };

        if (type.Type.StartsWith("Definition"))
        {
            var kind = type.Multiple
                ? EntityFieldViewModel.FieldKind.MultiSelect
                : EntityFieldViewModel.FieldKind.SingleSelect;

            var field = New(kind);

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
            // A nullable field with nothing stored stays not set, and saves nothing until the user sets it (T241/T242
            // review): loading it as the default made every save declare criticality 0 or internetFacing false.
            case "Integer":
            {
                var field = New(EntityFieldViewModel.FieldKind.Integer);
                field.IntegerValue = EntityFieldValues.LoadInteger(StoredValues(values), type.Nullable, type.DefaultValue);
                return field;
            }
            case "Boolean":
            {
                var field = New(EntityFieldViewModel.FieldKind.Boolean);
                field.BooleanValue = EntityFieldValues.LoadBoolean(StoredValues(values), type.Nullable, type.DefaultValue);
                return field;
            }
            default:
            {
                var field = New(EntityFieldViewModel.FieldKind.Text);
                field.TextValue = values.Count > 0 ? values[0].Value : type.DefaultValue ?? "";
                return field;
            }
        }
    }

    private static IReadOnlyList<string> StoredValues(List<EntitiesProperty> values) =>
        values.Select(v => v.Value).ToList();

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
        SaveEnabled = CanManage && Fields.All(f => f.IsValid) && IsDirty;

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
            // A read-only field goes back exactly as it was stored (T241). Its control may show a default the user
            // never chose — an absent criticality loads as 0 — and sending that would be a change the API refuses.
            if (field.IsReadOnly)
            {
                foreach (var value in field.OriginalValues)
                    dto.EntitiesProperties.Add(field.Kind == EntityFieldViewModel.FieldKind.MultiSelect
                        ? new EntitiesPropertyDto
                        {
                            Id = field.PropertyId, Name = $"{field.PropertyKey}-{_entity.Id}-{value}",
                            Type = field.PropertyKey, Value = value
                        }
                        : Property(field, value));
                continue;
            }

            switch (field.Kind)
            {
                case EntityFieldViewModel.FieldKind.Text:
                    dto.EntitiesProperties.Add(Property(field, field.TextValue));
                    break;

                // Not set saves nothing: the property stays absent rather than declared 0 or false.
                case EntityFieldViewModel.FieldKind.Integer:
                    if (EntityFieldValues.SaveInteger(field.IntegerValue) is { } integer)
                        dto.EntitiesProperties.Add(Property(field, integer));
                    break;

                case EntityFieldViewModel.FieldKind.Boolean:
                    if (EntityFieldValues.SaveBoolean(field.BooleanValue) is { } boolean)
                        dto.EntitiesProperties.Add(Property(field, boolean));
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

        Entity? result;
        try
        {
            result = await _entitiesService.SaveEntityAsync(dto);
        }
        catch (Exception ex)
        {
            // The API's refusal (403 for a permission or the scope, T241/T292) or any other failure: said, not thrown
            // into the command, which would take the window down.
            Logger.Warning(ex, "Saving entity {Id} failed", _entity.Id);
            Toasts.Error(ExplainError(ex));
            return;
        }

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

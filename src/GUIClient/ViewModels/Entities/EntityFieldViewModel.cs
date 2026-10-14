using System.Collections.Generic;
using AvaloniaExtraControls.Models;
using Model.Entities;
using ReactiveUI;

namespace GUIClient.ViewModels.Entities;

/// <summary>
/// One field of an entity form, derived from its <see cref="EntityType"/> definition.
///
/// The form used to be built imperatively in <c>EntityForm.axaml.cs</c> — 450 lines of
/// <c>new TextBox()</c>. IX-9 forbids that: the definition now becomes a list of these, and the
/// view renders them with DataTemplates keyed off <see cref="Kind"/>.
/// </summary>
public class EntityFieldViewModel : ReactiveObject
{
    public enum FieldKind
    {
        Text,
        Integer,
        Boolean,
        SingleSelect,
        MultiSelect
    }

    public EntityFieldViewModel(string propertyKey, string label, EntityType definition, FieldKind kind, int propertyId)
    {
        PropertyKey = propertyKey;
        Label = label;
        Definition = definition;
        Kind = kind;
        PropertyId = propertyId;
    }

    /// <summary>The definition's property key, used to build the persisted property name/type.</summary>
    public string PropertyKey { get; }

    /// <summary>Localized label shown next to the control.</summary>
    public string Label { get; }

    public EntityType Definition { get; }

    public FieldKind Kind { get; }

    /// <summary>Id of the existing <c>entities_properties</c> row, or 0 for a new one.</summary>
    public int PropertyId { get; }

    public bool IsText => Kind == FieldKind.Text;
    public bool IsInteger => Kind == FieldKind.Integer;
    public bool IsBoolean => Kind == FieldKind.Boolean;
    public bool IsSingleSelect => Kind == FieldKind.SingleSelect;
    public bool IsMultiSelect => Kind == FieldKind.MultiSelect;

    /// <summary>
    /// The control is disabled (T241): the user lacks <c>entities_manage</c>, or this is a risk-context property and
    /// they lack <c>entity_risk_context</c> (<c>Tools.EntityAccess</c>). A courtesy — the API refuses the write either
    /// way — but a read-only field is saved with the values it was loaded with, never with what its control shows.
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Why the field is read-only, shown under it; empty when the whole form is read-only or nothing is.</summary>
    public string ReadOnlyReason { get; init; } = "";

    public bool HasReadOnlyReason => !string.IsNullOrEmpty(ReadOnlyReason);

    /// <summary>The stored values the field was loaded with, saved back unchanged when it is read-only.</summary>
    public IReadOnlyList<string> OriginalValues { get; init; } = [];

    /// <summary>Labels shown by the multi-select control; supplied by the form.</summary>
    public string StrAvailable { get; set; } = "";
    public string StrSelected { get; set; } = "";

    private string _textValue = "";
    public string TextValue
    {
        get => _textValue;
        set
        {
            this.RaiseAndSetIfChanged(ref _textValue, value);
            RaiseValidityChanged();
        }
    }

    private decimal? _integerValue;

    /// <summary>
    /// Null is <em>not set</em>: a nullable field with no stored value loads that way and saves nothing until the user
    /// enters a number (<see cref="GUIClient.Tools.EntityFieldValues"/>, T241/T242).
    /// </summary>
    public decimal? IntegerValue
    {
        get => _integerValue;
        set
        {
            if (_integerValue == value) return;
            this.RaiseAndSetIfChanged(ref _integerValue, value);
            // An edit makes the form dirty, as a text edit does: setting a number alone must enable Save.
            RaiseValidityChanged();
        }
    }

    private bool? _booleanValue;

    /// <summary>Null is <em>not set</em>, as for <see cref="IntegerValue"/>: the check box shows indeterminate.</summary>
    public bool? BooleanValue
    {
        get => _booleanValue;
        set
        {
            if (_booleanValue == value) return;
            this.RaiseAndSetIfChanged(ref _booleanValue, value);
            this.RaisePropertyChanged(nameof(IsBooleanNotSet));
            RaiseValidityChanged();
        }
    }

    /// <summary>A Boolean field the user has not set: the form says nothing will be saved for it.</summary>
    public bool IsBooleanNotSet => IsBoolean && BooleanValue == null;

    /// <summary>The watermark of a not-set Integer field and the notice under a not-set Boolean; supplied by the form.</summary>
    public string StrNotSet { get; set; } = "";
    public string StrBooleanNotSet { get; set; } = "";

    private List<SelectEntity> _options = new();
    public List<SelectEntity> Options
    {
        get => _options;
        set => this.RaiseAndSetIfChanged(ref _options, value);
    }

    private SelectEntity? _selectedOption;
    public SelectEntity? SelectedOption
    {
        get => _selectedOption;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedOption, value);
            RaiseValidityChanged();
        }
    }

    private List<SelectEntity> _availableItems = new();
    public List<SelectEntity> AvailableItems
    {
        get => _availableItems;
        set => this.RaiseAndSetIfChanged(ref _availableItems, value);
    }

    private List<SelectEntity> _selectedItems = new();
    public List<SelectEntity> SelectedItems
    {
        get => _selectedItems;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedItems, value);
            RaiseValidityChanged();
        }
    }

    /// <summary>Error text for this field, or empty when it is satisfied.</summary>
    public string Error { get; private set; } = "";

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// A non-nullable field must have a value. Numeric and boolean fields load one when they are
    /// non-nullable (their default), so only text and selection fields can fail; a nullable numeric
    /// or boolean field may stay not set.
    /// </summary>
    public bool IsValid
    {
        get
        {
            if (Definition.Nullable) return true;

            return Kind switch
            {
                FieldKind.Text => !string.IsNullOrWhiteSpace(TextValue),
                FieldKind.SingleSelect => SelectedOption != null,
                FieldKind.MultiSelect => SelectedItems.Count > 0,
                _ => true
            };
        }
    }

    /// <summary>Recomputes <see cref="Error"/> and notifies the form. Set by the form.</summary>
    public string RequiredMessage { get; set; } = "";

    internal void Revalidate()
    {
        var error = IsValid ? "" : RequiredMessage;

        if (error == Error) return;

        Error = error;
        this.RaisePropertyChanged(nameof(Error));
        this.RaisePropertyChanged(nameof(HasError));
    }

    private void RaiseValidityChanged()
    {
        Revalidate();
        ValidityChanged?.Invoke();
    }

    /// <summary>Raised whenever this field's validity may have changed.</summary>
    internal System.Action? ValidityChanged { get; set; }
}

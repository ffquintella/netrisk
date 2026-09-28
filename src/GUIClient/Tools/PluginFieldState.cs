using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Model.Secrets;

namespace GUIClient.Tools;

/// <summary>
/// One control a vault plugin contributed to a host screen, as the form holds it while an operator
/// fills it in.
///
/// <para>Plain <see cref="INotifyPropertyChanged"/> and no Avalonia, for the same two reasons as
/// <see cref="VaultSecretFieldState"/>: the same state would otherwise be written twice (the picker
/// and the connection editor both render these), and <c>GUIClient.Tests</c> compiles source files
/// directly rather than referencing <c>GUIClient</c>, so anything touching a UI type cannot be
/// tested at all.</para>
///
/// <para>The four kinds are exposed as four booleans rather than as four different view-models,
/// because the view renders all four controls and shows one. A template selector would be tidier
/// XAML and one more class to keep in step with an enum that already exists on the wire.</para>
/// </summary>
public sealed class PluginFieldState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private PluginFieldState(VaultFieldSpecView spec, string? initialValue)
    {
        Spec = spec;
        _value = initialValue ?? spec.DefaultValue ?? string.Empty;
    }

    public VaultFieldSpecView Spec { get; }

    public string Key => Spec.Key;

    /// <summary>The plugin's label, already resolved to the operator's culture by the server.</summary>
    public string Label => Spec.Label;

    public string? Help => Spec.Help;

    public bool HasHelp => !string.IsNullOrWhiteSpace(Spec.Help);

    public bool IsRequired => Spec.Required;

    public int MaxLength => Spec.MaxLength;

    public bool IsText => Spec.Kind == VaultFieldKind.Text;

    /// <summary>A closed list: a combo, because every acceptable value is in it.</summary>
    public bool IsClosedChoice => Spec.Kind == VaultFieldKind.Choice && !Spec.AllowCustomValue;

    /// <summary>
    /// An open list: a suggestion box, for a vault that cannot enumerate everything it accepts. The
    /// plugin is then the only thing that can reject a bad value, and it does so on the read path.
    /// </summary>
    public bool IsOpenChoice => Spec.Kind == VaultFieldKind.Choice && Spec.AllowCustomValue;

    public bool IsToggle => Spec.Kind == VaultFieldKind.Toggle;

    public bool IsNumber => Spec.Kind == VaultFieldKind.Number;

    /// <summary>Whether the option list has to be re-fetched when the selected secret changes.</summary>
    public bool OptionsDependOnSecret => Spec.OptionsDependOnSecret;

    private string _value;

    /// <summary>
    /// The value as it will be stored. Every kind funnels through this one string: the reference and
    /// the connection's settings are both string-to-string, so a bool or a number that lived in its
    /// own typed property would only have to be converted somewhere less obvious.
    /// </summary>
    public string Value
    {
        get => _value;
        set
        {
            var incoming = value ?? string.Empty;
            if (string.Equals(_value, incoming, StringComparison.Ordinal)) return;

            _value = incoming;
            Error = null;
            RaiseAll();
        }
    }

    /// <summary>The check box's binding target.</summary>
    public bool BoolValue
    {
        get => bool.TryParse(_value, out var parsed) && parsed;
        set => Value = value ? "true" : "false";
    }

    /// <summary>The spinner's binding target. Invariant, because the value is stored as text.</summary>
    public decimal NumberValue
    {
        get => decimal.TryParse(_value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
        set => Value = ((long)value).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The combo's binding target, matched to the option list by value.</summary>
    public VaultFieldOptionView? SelectedOption
    {
        get => _options.FirstOrDefault(o => string.Equals(o.Value, _value, StringComparison.Ordinal));
        set => Value = value?.Value ?? string.Empty;
    }

    private IReadOnlyList<VaultFieldOptionView> _options = [];

    /// <summary>
    /// What the plugin offered for this field, for this connection and this selection. Empty until
    /// the fetch returns, and empty for a field that is not a choice.
    /// </summary>
    public IReadOnlyList<VaultFieldOptionView> Options
    {
        get => _options;
        set
        {
            _options = value ?? [];

            // A value that is no longer on offer is dropped on a closed list and kept on an open
            // one. Keeping it on a closed list would let Select return something the operator cannot
            // see in the combo, which is how a field ends up bound to a value nobody chose.
            if (IsClosedChoice && _value.Length > 0
                               && !_options.Any(o => string.Equals(o.Value, _value, StringComparison.Ordinal)))
                _value = string.Empty;

            RaiseAll();
        }
    }

    /// <summary>The labels of the options, for the suggestion box, which binds to strings.</summary>
    public IReadOnlyList<string> OptionValues => _options.Select(o => o.Value).ToList();

    private string? _error;

    /// <summary>What is wrong with the current value, or null. Cleared whenever the value changes.</summary>
    public string? Error
    {
        get => _error;
        private set
        {
            if (string.Equals(_error, value, StringComparison.Ordinal)) return;
            _error = value;
            RaiseAll();
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    /// <summary>
    /// Whether the value is one the screen may commit, recording the complaint when it is not.
    ///
    /// <para>The same rules the server applies in <c>PluginFieldSpecProjection.Validate</c>, written
    /// twice on purpose. Here they exist so an operator sees the problem while the form is open;
    /// there they exist because the form is not the only caller of the API. The server's copy is the
    /// one that decides.</para>
    /// </summary>
    public bool Validate(Func<string, string> localize)
    {
        var value = _value.Trim();

        if (value.Length == 0)
        {
            Error = Spec.Required
                ? string.Format(localize("VaultPluginFieldRequiredMSG"), Label)
                : null;

            return !Spec.Required;
        }

        if (value.Length > Spec.MaxLength)
        {
            Error = string.Format(localize("VaultPluginFieldTooLongMSG"), Label, Spec.MaxLength);
            return false;
        }

        switch (Spec.Kind)
        {
            case VaultFieldKind.Toggle when !bool.TryParse(value, out _):
                Error = string.Format(localize("VaultPluginFieldNotBooleanMSG"), Label);
                return false;

            case VaultFieldKind.Number
                when !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _):
                Error = string.Format(localize("VaultPluginFieldNotNumberMSG"), Label);
                return false;

            case VaultFieldKind.Text when Malformed(value):
                Error = string.Format(localize(Spec.Format == VaultFieldFormat.NoWhitespace
                    ? "VaultPluginFieldNoSpacesMSG"
                    : "VaultPluginFieldIdentifierMSG"), Label);
                return false;

            case VaultFieldKind.Choice when IsClosedChoice && _options.Count > 0
                                            && !_options.Any(o =>
                                                string.Equals(o.Value, value, StringComparison.Ordinal)):
                Error = string.Format(localize("VaultPluginFieldNotOfferedMSG"), Label);
                return false;
        }

        Error = null;
        return true;
    }

    private bool Malformed(string value) => Spec.Format switch
    {
        VaultFieldFormat.NoWhitespace => value.Any(char.IsWhiteSpace),
        VaultFieldFormat.Identifier => !value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'),
        _ => false
    };

    /// <summary>
    /// The fields of one screen, in the order the plugin declared them, pre-filled from whatever is
    /// already stored.
    /// </summary>
    public static List<PluginFieldState> Build(IEnumerable<VaultFieldSpecView>? specs,
        IReadOnlyDictionary<string, string>? current)
    {
        if (specs is null) return [];

        return specs.Select(spec =>
        {
            var value = current is not null && current.TryGetValue(spec.Key, out var stored)
                ? stored
                : null;

            return new PluginFieldState(spec, value);
        }).ToList();
    }

    /// <summary>
    /// The values worth storing. A blank one is an untouched control and means "not set" — storing
    /// an empty string would make a plugin's lookup succeed with nothing in it.
    /// </summary>
    public static Dictionary<string, string> Values(IEnumerable<PluginFieldState> fields) =>
        fields.Where(f => !string.IsNullOrWhiteSpace(f.Value))
            .ToDictionary(f => f.Key, f => f.Value.Trim(), StringComparer.Ordinal);

    private void RaiseAll()
    {
        foreach (var name in (string[])
                 [
                     nameof(Value), nameof(BoolValue), nameof(NumberValue), nameof(SelectedOption),
                     nameof(Options), nameof(OptionValues), nameof(Error), nameof(HasError)
                 ])
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

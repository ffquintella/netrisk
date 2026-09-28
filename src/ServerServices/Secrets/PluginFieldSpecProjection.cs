using System.Globalization;
using Contracts.Ui;
using Model.Secrets;

namespace ServerServices.Secrets;

/// <summary>
/// Turns what a plugin declared into what the host is willing to render, and checks the values that
/// come back against it.
///
/// <para>Pure and static, so it can be tested without a database, a plugin or a vault — which
/// matters, because this is the boundary. Everything on the far side of it was written by somebody
/// else and loaded into this process: a label is third-party text about to be drawn in an
/// operator's window, a key is about to become part of a string stored in a credential column, and a
/// field count is about to become rows in a dialog.</para>
///
/// <para><b>How it fails.</b> A declaration that breaks a structural rule — an unusable key, a
/// duplicate, a kind this host does not know, a screen with too many fields — is dropped
/// <i>whole</i>, and the screen renders exactly as it did before the plugin declared anything. A
/// declaration that is merely immoderate — a paragraph for a label, a length bound of a million —
/// is truncated and clamped. The distinction is deliberate: a missing control is a recoverable
/// state an operator can be told about, while a half-rendered screen on the credential path is
/// not.</para>
/// </summary>
public static class PluginFieldSpecProjection
{
    /// <summary>What the host will render, plus the reason it is rendering less than was declared.</summary>
    /// <param name="Fields">The accepted fields. Empty when the declaration was dropped.</param>
    /// <param name="Problem">Null when nothing was wrong. Logged by the caller, never shown raw to an operator.</param>
    public readonly record struct Projection(List<VaultFieldSpecView> Fields, string? Problem)
    {
        public static readonly Projection Empty = new([], null);

        public static Projection Rejected(string problem) => new([], problem);
    }

    /// <summary>
    /// Checks and converts one screen's declaration.
    ///
    /// <paramref name="culture"/> picks the label and help text: exact culture name, then the
    /// neutral parent, then the plugin's own <c>Label</c>. Plugin strings do not go through the
    /// host's <c>.resx</c> — that scheme is three files the host owns and a plugin cannot add a key
    /// to, and a dynamic lookup that missed would render as the key name.
    /// </summary>
    public static Projection Project(IReadOnlyList<PluginFieldSpec>? declared, CultureInfo? culture)
    {
        if (declared is null || declared.Count == 0) return Projection.Empty;

        if (declared.Count > SecretVaultDefaults.MaxPluginFieldsPerScreen)
            return Projection.Rejected(
                $"it declares {declared.Count} fields and the limit is "
                + $"{SecretVaultDefaults.MaxPluginFieldsPerScreen}");

        var fields = new List<VaultFieldSpecView>(declared.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var spec in declared)
        {
            if (spec is null) return Projection.Rejected("one of its fields is null");

            var key = spec.Key?.Trim() ?? string.Empty;

            if (!SecretReference.OptionKeyPattern.IsMatch(key))
                return Projection.Rejected(
                    $"'{Sanitize(key, 40)}' is not a usable field key: lower-case, starting with a "
                    + "letter or digit, then letters, digits, '_', '.' or '-', at most 32 characters");

            if (!keys.Add(key))
                return Projection.Rejected($"it declares the field key '{key}' twice");

            if (!Enum.IsDefined(spec.Kind))
                return Projection.Rejected($"field '{key}' asks for a control kind this host cannot render");

            if (!Enum.IsDefined(spec.Format))
                return Projection.Rejected($"field '{key}' asks for a validation format this host does not know");

            if (string.IsNullOrWhiteSpace(spec.Label))
                return Projection.Rejected($"field '{key}' has no label");

            fields.Add(new VaultFieldSpecView
            {
                Key = key,
                Label = Sanitize(Resolve(spec.Label, spec.LabelTranslations, culture),
                    SecretVaultDefaults.MaxFieldLabelLength)!,
                Help = Sanitize(Resolve(spec.Help, spec.HelpTranslations, culture),
                    SecretVaultDefaults.MaxFieldHelpLength),
                Kind = (VaultFieldKind)(int)spec.Kind,
                Required = spec.Required,
                DefaultValue = Sanitize(spec.DefaultValue, SecretVaultDefaults.MaxFieldValueLength),
                Format = (VaultFieldFormat)(int)spec.Format,
                MaxLength = Math.Clamp(spec.MaxLength, 1, SecretVaultDefaults.MaxFieldValueLength),
                AllowCustomValue = spec.AllowCustomValue,
                OptionsDependOnSecret = spec.OptionsDependOnSecret
            });
        }

        return new Projection(fields, null);
    }

    /// <summary>
    /// The options of one choice field, bounded the same way the declaration is: an option with no
    /// value is dropped, a duplicate value is dropped, and the list is cut at the host's maximum
    /// rather than refused — a truncated combo still lets an operator work, and the log says it was
    /// cut.
    /// </summary>
    public static List<VaultFieldOptionView> ProjectOptions(
        IReadOnlyList<PluginFieldOption>? declared, CultureInfo? culture, out bool truncated)
    {
        truncated = false;

        var options = new List<VaultFieldOptionView>();
        if (declared is null || declared.Count == 0) return options;

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var option in declared)
        {
            if (option is null || string.IsNullOrWhiteSpace(option.Value)) continue;

            var value = option.Value.Trim();

            if (value.Length > SecretVaultDefaults.MaxFieldValueLength) continue;
            if (!seen.Add(value)) continue;

            if (options.Count >= SecretVaultDefaults.MaxFieldOptions)
            {
                truncated = true;
                break;
            }

            options.Add(new VaultFieldOptionView
            {
                Value = value,
                Label = Sanitize(string.IsNullOrWhiteSpace(option.Label) ? value : option.Label,
                    SecretVaultDefaults.MaxFieldLabelLength)!,
                Description = Sanitize(option.Description, SecretVaultDefaults.MaxFieldHelpLength)
            });
        }

        _ = culture;

        return options;
    }

    /// <summary>
    /// What is wrong with the values an operator produced, or null when nothing is.
    ///
    /// <para>Runs on the server even though the client checks the same rules while the form is open,
    /// because the client is not the only caller of the API and a rule enforced only in a dialog is
    /// a rule.</para>
    ///
    /// <para><b>A key the declaration does not mention is left alone, not rejected.</b> A reference
    /// stored while the plugin declared a field that a later version dropped must keep resolving —
    /// the dictionary belongs to the plugin, and the plugin is the one that decides an unknown key
    /// is meaningless.</para>
    /// </summary>
    public static string? Validate(IReadOnlyList<VaultFieldSpecView> specs,
        IReadOnlyDictionary<string, string>? values)
    {
        values ??= new Dictionary<string, string>();

        foreach (var spec in specs)
        {
            values.TryGetValue(spec.Key, out var value);

            if (string.IsNullOrWhiteSpace(value))
            {
                if (spec.Required)
                    return $"'{spec.Label}' is required by this vault and has no value.";

                continue;
            }

            if (value.Length > spec.MaxLength)
                return $"'{spec.Label}' is longer than the {spec.MaxLength} characters this vault accepts.";

            switch (spec.Kind)
            {
                case VaultFieldKind.Toggle:
                    if (!bool.TryParse(value, out _))
                        return $"'{spec.Label}' must be true or false.";
                    break;

                case VaultFieldKind.Number:
                    if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        return $"'{spec.Label}' must be a whole number.";
                    break;

                case VaultFieldKind.Text:
                    if (Malformed(spec.Format, value))
                        return $"'{spec.Label}' " + FormatRule(spec.Format);
                    break;

                case VaultFieldKind.Choice:
                    // Membership is checked where the option list is — in the dialog, against the
                    // list it fetched. Re-fetching it here would be a vault round trip on every save
                    // of every credential field, and an option list is not stable enough for the
                    // answer to mean much: a value that was valid at pick time and is not now is
                    // reported by the plugin, at read time, naming the vault.
                    break;
            }
        }

        return null;
    }

    private static bool Malformed(VaultFieldFormat format, string value) => format switch
    {
        VaultFieldFormat.NoWhitespace => value.Any(char.IsWhiteSpace),
        VaultFieldFormat.Identifier => !value.All(c =>
            char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'),
        _ => false
    };

    private static string FormatRule(VaultFieldFormat format) => format switch
    {
        VaultFieldFormat.NoWhitespace => "may not contain spaces.",
        VaultFieldFormat.Identifier =>
            "may contain only letters, digits, '-', '_' and '.' — it is a name, not a path.",
        _ => "is not usable."
    };

    /// <summary>
    /// The plugin's text for a culture: exact name, then the neutral parent, then the fallback the
    /// plugin gave. A plugin that supplies only English still renders.
    /// </summary>
    private static string? Resolve(string? fallback, IReadOnlyDictionary<string, string>? translations,
        CultureInfo? culture)
    {
        if (translations is null || translations.Count == 0 || culture is null) return fallback;

        if (translations.TryGetValue(culture.Name, out var exact) && !string.IsNullOrWhiteSpace(exact))
            return exact;

        var neutral = culture.TwoLetterISOLanguageName;

        if (translations.TryGetValue(neutral, out var parent) && !string.IsNullOrWhiteSpace(parent))
            return parent;

        return fallback;
    }

    /// <summary>
    /// Bounds a string the host is about to render and strips the control characters that would let
    /// a declaration draw outside its own control. Truncation is marked, so a label that was cut
    /// does not read as a label that was written that way.
    /// </summary>
    private static string? Sanitize(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();

        if (cleaned.Length == 0) return null;

        return cleaned.Length <= maxLength ? cleaned : cleaned[..(maxLength - 1)] + "…";
    }
}

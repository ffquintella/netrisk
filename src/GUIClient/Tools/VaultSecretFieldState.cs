using System;
using System.ComponentModel;
using Model.Secrets;

namespace GUIClient.Tools;

/// <summary>
/// One credential field's answer to "is this typed in, or does it come from the vault?".
///
/// There is one of these per secret text box on an administration form. It exists as a plain class
/// with no Avalonia and no ReactiveUI in it for two reasons: the same three-way decision (typed / newly
/// picked / already bound) was going to be written seven times in the integrations view-model
/// otherwise, and <c>GUIClient.Tests</c> compiles source files directly rather than referencing
/// <c>GUIClient</c>, so anything that touches a UI type cannot be tested at all.
///
/// The decision it encodes, in the order that matters:
///
///  1. The operator picked a secret in this editing session — send that reference.
///  2. They typed something — send the literal.
///  3. Neither — send null, which every connection endpoint reads as "leave the stored credential
///     alone". This is what stops a form showing a redacted placeholder from overwriting a working
///     credential with the placeholder, and the vault feature must not break it.
///
/// A field that is already vault-backed on the server and is not touched therefore stays bound: the
/// reference the server holds is not resent, because it does not need to be.
///
/// It raises <see cref="INotifyPropertyChanged"/> — from <c>System.ComponentModel</c>, not from
/// ReactiveUI — so a view can bind straight to <see cref="DisplayText"/> and the enable flags without
/// the containing view-model mirroring five properties per field. Seven credential fields times five
/// mirrored properties is thirty-five pieces of boilerplate that each have to remember to raise.
/// </summary>
public sealed class VaultSecretFieldState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Announces every derived property at once.
    ///
    /// Blunt on purpose: each of the four mutating methods below can change any of them, and a
    /// per-property analysis that is right today is a stale label the first time one of them grows a
    /// branch. Seven of these objects exist per screen, so the cost is nothing.
    /// </summary>
    private void RaiseAll()
    {
        foreach (var name in (string[])
                 [
                     nameof(StoredReference), nameof(PendingReference), nameof(PendingDisplay),
                     nameof(StoredDisplay), nameof(Detached), nameof(VaultAvailable),
                     nameof(EffectiveReference), nameof(IsVaultBacked), nameof(AcceptsTypedValue),
                     nameof(ShowPicker), nameof(ShowDetach), nameof(DisplayText),
                     nameof(HasPendingChange), nameof(HasDisplayText)
                 ])
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


    /// <summary>
    /// The reference the server currently holds for this field, or null when it holds a literal (or
    /// nothing). Comes from the connection view's <c>…VaultReference</c> property.
    ///
    /// Not known for every field: a notification channel's secrets are write-only in this UI, so this
    /// stays null there and the field simply shows nothing until something is picked. That is honest —
    /// the client genuinely does not know — and it is better than a label that guesses.
    /// </summary>
    public string? StoredReference { get; private set; }

    /// <summary>What was picked in this editing session, if anything.</summary>
    public string? PendingReference { get; private set; }

    /// <summary>The human label for <see cref="PendingReference"/>, as the picker reported it.</summary>
    public string? PendingDisplay { get; private set; }

    /// <summary>The label the server reported for <see cref="StoredReference"/>, when it was asked.</summary>
    public string? StoredDisplay { get; private set; }

    /// <summary>What is in the text box. Set by the view-model from the bound property.</summary>
    public string TypedValue { get; set; } = string.Empty;

    /// <summary>
    /// True once the operator has explicitly detached the field from the vault in this session, so
    /// that <see cref="DisplayText"/> stops advertising a binding they have decided to replace.
    ///
    /// It does not by itself remove the binding on the server. Nothing in these forms can clear a
    /// credential — there is no "no credential" state for a connection that needs one — so detaching
    /// is a prelude to typing a literal, and the save only takes effect once something is typed.
    /// </summary>
    public bool Detached { get; private set; }

    private bool _vaultAvailable;

    /// <summary>Whether the installation has a usable vault at all. False hides the button entirely.</summary>
    public bool VaultAvailable
    {
        get => _vaultAvailable;
        set
        {
            if (_vaultAvailable == value) return;
            _vaultAvailable = value;
            RaiseAll();
        }
    }

    /// <summary>The reference that would be in force after a save with nothing further typed.</summary>
    public string? EffectiveReference =>
        PendingReference ?? (Detached ? null : StoredReference);

    /// <summary>Whether this field currently resolves through a vault.</summary>
    public bool IsVaultBacked => EffectiveReference != null;

    /// <summary>
    /// Whether the text box should be usable. A vault-backed field disables it, because typing into a
    /// box whose contents will be ignored — or worse, will silently replace the binding — is the kind
    /// of ambiguity that gets a credential rotated at 2am.
    /// </summary>
    public bool AcceptsTypedValue => !IsVaultBacked;

    /// <summary>Whether to show the picker button. Only when a vault exists to pick from.</summary>
    public bool ShowPicker => VaultAvailable;

    /// <summary>Whether to show the "stop using the vault" button.</summary>
    public bool ShowDetach => VaultAvailable && IsVaultBacked;

    /// <summary>
    /// The one-line label shown beside the field.
    ///
    /// Empty rather than a placeholder when there is nothing to say: a caption reading "not from a
    /// vault" beside every ordinary credential field on an installation that has no vault is noise,
    /// and the button beside it already implies the choice.
    ///
    /// Carries no "unsaved" wording of its own — see <see cref="HasPendingChange"/>. Composing an
    /// English suffix in here would put untranslatable text on a screen that is localized in three
    /// languages.
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (PendingReference != null) return PendingDisplay ?? Describe(PendingReference);

            if (Detached || StoredReference == null) return string.Empty;

            return StoredDisplay ?? Describe(StoredReference);
        }
    }

    /// <summary>Whether <see cref="DisplayText"/> has anything to show. Drives the label's visibility.</summary>
    public bool HasDisplayText => DisplayText.Length > 0;

    /// <summary>
    /// Whether the binding shown is one chosen in this session and not yet saved. The view uses it to
    /// add a localized "will be saved" marker, so an operator does not close the form believing the
    /// change took.
    /// </summary>
    public bool HasPendingChange => PendingReference != null;

    /// <summary>
    /// Reloads the field for a connection just fetched from the server. Clears anything picked or
    /// typed, because those belong to the editing session that just ended.
    /// </summary>
    public void LoadFrom(string? storedReference, string? storedDisplay = null)
    {
        StoredReference = SecretReference.StoredReferenceOrNull(storedReference);
        StoredDisplay = StoredReference == null ? null : storedDisplay;
        PendingReference = null;
        PendingDisplay = null;
        TypedValue = string.Empty;
        Detached = false;
        RaiseAll();
    }

    /// <summary>Resets to a blank new-connection draft.</summary>
    public void Reset() => LoadFrom(null);

    /// <summary>Records a secret chosen in the picker.</summary>
    public void Bind(string reference, string? display)
    {
        if (string.IsNullOrWhiteSpace(reference))
            throw new ArgumentException("A vault reference is required.", nameof(reference));

        PendingReference = reference;
        PendingDisplay = display;

        // Typing and picking are mutually exclusive, and the text box is disabled while bound. Left
        // in place, a half-typed key would be sent the moment the operator detached again.
        TypedValue = string.Empty;
        Detached = false;
        RaiseAll();
    }

    /// <summary>Detaches the field from the vault so a literal can be typed instead.</summary>
    public void Detach()
    {
        PendingReference = null;
        PendingDisplay = null;
        Detached = true;
        RaiseAll();
    }

    /// <summary>
    /// What to send as this field's secret on save: the picked reference, the typed literal, or null
    /// for "unchanged". See the class remarks for why the order is what it is.
    /// </summary>
    public string? ValueToSend()
    {
        if (PendingReference != null) return PendingReference;

        return string.IsNullOrWhiteSpace(TypedValue) ? null : TypedValue.Trim();
    }

    /// <summary>
    /// A readable label for a reference the server did not describe — the picker's own label is
    /// preferred when there is one, but a form reloaded from a list has only the raw reference.
    /// </summary>
    private static string Describe(string reference) =>
        SecretReference.TryParse(reference, out var parsed)
            ? "Vault #" + parsed.ConnectionId + ": " + parsed.DisplayKey
            : "Vault reference (unreadable)";
}

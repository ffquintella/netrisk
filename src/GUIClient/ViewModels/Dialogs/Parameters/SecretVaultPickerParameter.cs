namespace GUIClient.ViewModels.Dialogs.Parameters;

/// <summary>
/// What the secret picker needs to know about the field it was opened from.
///
/// Only presentation: the dialog reads nothing and writes nothing on the caller's behalf, it just
/// returns a reference. Naming the field in the title is what stops an operator who has opened the
/// picker three times in one form from binding the webhook secret to the API key's secret.
/// </summary>
public class SecretVaultPickerParameter : NavigationParameterBase
{
    /// <summary>The caption of the field being bound, e.g. "API key". Shown in the dialog's header.</summary>
    public string? FieldName { get; set; }

    /// <summary>The reference currently in force, so the dialog can pre-select it. Null when there is none.</summary>
    public string? CurrentReference { get; set; }
}

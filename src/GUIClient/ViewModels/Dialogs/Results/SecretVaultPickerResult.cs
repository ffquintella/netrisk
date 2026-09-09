namespace GUIClient.ViewModels.Dialogs.Results;

/// <summary>
/// The secret an operator chose.
///
/// Carries a reference and a label, and never a value: the desktop client has no way to obtain one,
/// because the server exposes no endpoint that returns it.
/// </summary>
public class SecretVaultPickerResult : DialogResultBase
{
    /// <summary>The <c>vault:v1:…</c> reference to store in the field. Null when the dialog was cancelled.</summary>
    public string? Reference { get; set; }

    /// <summary>A human label for the chosen secret, for the caption beside the field.</summary>
    public string? DisplayName { get; set; }
}

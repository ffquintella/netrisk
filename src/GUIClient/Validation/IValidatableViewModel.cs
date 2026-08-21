namespace GUIClient.Validation;

/// <summary>
/// Implemented by every view-model that declares validation rules. The rules live in the
/// <see cref="ValidationContext"/>, which is what gates Save and what the view binds to in
/// order to show the user *why* Save is blocked (docs/ux-interaction-standard.md IX-4).
/// </summary>
public interface IValidatableViewModel
{
    ValidationContext ValidationContext { get; }
}

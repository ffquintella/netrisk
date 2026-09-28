namespace Model.Secrets;

/// <summary>
/// One stored reference that its plugin expresses differently now — what it is, where it is, and
/// what it would become.
///
/// Reported before anything is written, because the operator approving the change is the only one
/// who can tell "the environment moved out of the id" from "this field now points at a different
/// secret".
/// </summary>
public class SecretReferenceRewrite
{
    /// <summary>Table, row and column, in a form an operator can look up. Never a credential.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>The reference as stored. A reference names a secret; it is not one.</summary>
    public string Before { get; set; } = string.Empty;

    /// <summary>The reference as the plugin now writes it.</summary>
    public string After { get; set; } = string.Empty;
}

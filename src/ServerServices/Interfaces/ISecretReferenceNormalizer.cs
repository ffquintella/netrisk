using Model.Secrets;

namespace ServerServices.Interfaces;

/// <summary>
/// Rewrites stored vault references into the form their plugin now produces.
///
/// Separate from <see cref="ISecretVaultService"/> because it is a maintenance operation and not
/// part of any read path: it walks every credential column in the product, and the only thing that
/// invokes it is an operator at a console.
/// </summary>
public interface ISecretReferenceNormalizer
{
    /// <summary>
    /// Every reference whose plugin reads it differently from how it is stored.
    ///
    /// <paramref name="apply"/> false is a plan and writes nothing — which is the mode to run first,
    /// because a rewrite the plugin got wrong repoints a credential field at a different secret.
    /// </summary>
    Task<List<SecretReferenceRewrite>> NormalizeAsync(bool apply, CancellationToken ct = default);
}

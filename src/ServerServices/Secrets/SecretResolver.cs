using Model.Exceptions;
using Model.Secrets;
using ServerServices.Interfaces;

namespace ServerServices.Secrets;

/// <summary>
/// The two-branch read path for a credential column: a vault reference goes to the vault, anything
/// else goes to <see cref="ISecretProtector"/>.
///
/// It is this thin on purpose. Every integration service on the server depends on it instead of on
/// <c>ISecretProtector</c>, and the value of that substitution is entirely in the branch below — one
/// place that knows a credential column can now hold a pointer, instead of nineteen call sites that
/// each have to remember.
/// </summary>
public class SecretResolver : ISecretResolver
{
    private readonly ISecretProtector _protector;
    private readonly ISecretVaultService _vaults;

    public SecretResolver(ISecretProtector protector, ISecretVaultService vaults)
    {
        _protector = protector;
        _vaults = vaults;
    }

    public bool IsVaultReference(string? stored) => SecretReference.IsReference(stored);

    public async Task<string?> ResolveAsync(string? stored, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(stored)) return null;

        if (SecretReference.TryParse(stored, out var reference))
            return await _vaults.ResolveAsync(reference, ct);

        // A value that carries the marker but does not parse is not a credential and must not be
        // sent as one. It is a truncated or hand-edited reference, and handing it to a third party
        // produces a 401 whose cause is invisible.
        if (SecretReference.IsReference(stored))
            throw new SecretVaultResolutionException(
                "A stored credential looks like a vault reference but could not be parsed. It has "
                + "most likely been truncated or edited by hand; re-select the secret on the field.",
                stored);

        return _protector.Unprotect(stored);
    }
}

namespace ServerServices.Interfaces;

/// <summary>
/// The short-lived, obfuscated store for secrets resolved out of an external vault.
///
/// Its reason to exist is arithmetic: a Vision One sync makes dozens of HTTP calls, and each one asks
/// for the API key. Without a cache that is dozens of vault round trips per sync, which is both slow
/// and a much larger audit trail than "NetRisk read this credential once". With one, it is a single
/// read per TTL window.
///
/// "Obfuscated" is the honest word, not "encrypted". The key lives in the same process as the
/// ciphertext, so anyone who can read this process's memory can recover the plaintext — a debugger, a
/// core dump, an attacker with code execution. What it does buy, and the reason it is worth doing:
/// a process dump, a crash report or a heap snapshot no longer contains the estate's credentials as
/// scannable UTF-16 strings, which is how credentials most often escape in practice.
///
/// What actually bounds the exposure is the TTL, and that is a policy, not an implementation detail:
/// see <see cref="Model.Secrets.SecretVaultDefaults.CacheTtlMinutes"/>.
/// </summary>
public interface IObfuscatedSecretCache
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or null when absent or expired. An
    /// expired entry is removed as a side effect, so nothing accumulates for keys that stop being
    /// asked for.
    /// </summary>
    string? Get(string key);

    /// <summary>
    /// Stores <paramref name="value"/> for at most <paramref name="ttl"/>. A non-positive TTL stores
    /// nothing and removes any existing entry — that is how "do not cache this" is expressed.
    /// </summary>
    void Set(string key, string value, TimeSpan ttl);

    /// <summary>Drops one entry. Used when a resolution fails, so a stale value cannot outlive its validity.</summary>
    void Remove(string key);

    /// <summary>
    /// Drops every entry whose key starts with <paramref name="prefix"/>. Rotating a vault
    /// connection's API key must not leave values it fetched still being served.
    /// </summary>
    int RemoveByPrefix(string prefix);

    /// <summary>Drops everything. The plugin set changing is the case that needs it.</summary>
    void Clear();

    /// <summary>Live entry count, for tests and for the diagnostics endpoint. Never the values.</summary>
    int Count { get; }
}

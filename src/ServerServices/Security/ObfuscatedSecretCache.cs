using System.Collections.Concurrent;
using Serilog;
using ServerServices.Interfaces;
using Tools;
using Tools.Criptography;

namespace ServerServices.Security;

/// <summary>
/// <see cref="IObfuscatedSecretCache"/> over a concurrent dictionary of AES-256-GCM ciphertexts under
/// a key that exists only for the lifetime of this process.
///
/// Three decisions worth stating:
///
/// <para><b>The key is generated per process, from the CSPRNG, and never persisted.</b> It is not
/// derived from the installation's server secret, and that is deliberate: a cache entry must not
/// survive a restart, and it must not be decryptable by anything that has the key file. Restarting
/// the API empties the cache by construction rather than by remembering to.</para>
///
/// <para><b>Expiry is absolute, not sliding.</b> A sliding window on a credential that a busy sync
/// job touches every few seconds never expires, which quietly turns a 15-minute cache into a
/// permanent copy — and with it, a revoked credential that NetRisk keeps using.</para>
///
/// <para><b>Reads sweep.</b> There is no timer. An expired entry is evicted when it is asked for, and
/// a whole-dictionary sweep runs when the cache grows past a threshold, so a process that resolves
/// many one-off references does not hold their ciphertexts until it restarts.</para>
/// </summary>
public class ObfuscatedSecretCache : IObfuscatedSecretCache
{
    /// <summary>Entry count past which a <see cref="Set"/> sweeps expired entries before inserting.</summary>
    private const int SweepThreshold = 256;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ILogger _logger;

    /// <summary>
    /// The process-ephemeral obfuscation key, as a base64 passphrase for
    /// <see cref="AesGcm256"/> (which salts and HKDF-derives per entry, so two identical secrets do
    /// not produce identical ciphertext in memory).
    /// </summary>
    private readonly string _passphrase = RandomGenerator.RandomToken(32);

    public ObfuscatedSecretCache(ILogger logger)
    {
        _logger = logger;
    }

    public int Count => _entries.Count;

    public string? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (!_entries.TryGetValue(key, out var entry)) return null;

        if (entry.ExpiresAt <= DateTime.UtcNow)
        {
            _entries.TryRemove(key, out _);
            return null;
        }

        try
        {
            return AesGcm256.Decrypt(entry.Ciphertext, _passphrase);
        }
        catch (Exception ex)
        {
            // Cannot happen with a stable in-process key, which is exactly why it must not be
            // swallowed silently: if it ever does, the answer is "go to the vault", not "use
            // something that failed to authenticate".
            _logger.Warning("A cached secret could not be de-obfuscated and was dropped: {Message}", ex.Message);
            _entries.TryRemove(key, out _);
            return null;
        }
    }

    public void Set(string key, string value, TimeSpan ttl)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (ttl <= TimeSpan.Zero || string.IsNullOrEmpty(value))
        {
            Remove(key);
            return;
        }

        if (_entries.Count >= SweepThreshold) Sweep();

        _entries[key] = new Entry
        {
            Ciphertext = AesGcm256.Encrypt(value, _passphrase),
            ExpiresAt = DateTime.UtcNow.Add(ttl)
        };
    }

    public void Remove(string key)
    {
        if (!string.IsNullOrEmpty(key)) _entries.TryRemove(key, out _);
    }

    public int RemoveByPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return 0;

        var removed = 0;
        foreach (var key in _entries.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (_entries.TryRemove(key, out _)) removed++;
        }

        return removed;
    }

    public void Clear() => _entries.Clear();

    private void Sweep()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _entries)
            if (pair.Value.ExpiresAt <= now)
                _entries.TryRemove(pair.Key, out _);
    }

    private sealed class Entry
    {
        public required string Ciphertext { get; init; }

        public required DateTime ExpiresAt { get; init; }
    }
}

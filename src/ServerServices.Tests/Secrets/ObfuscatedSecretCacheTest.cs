using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Serilog;
using ServerServices.Security;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// The short-lived store that keeps a vault-resolved credential out of a heap dump.
///
/// Three properties, in descending order of how much damage getting them wrong would do:
/// an expired entry is never served (a revoked credential must stop working); the plaintext is not
/// sitting in memory as a string (the reason the class exists); and eviction by connection prefix
/// actually removes things (a rotated API key must not leave values it fetched still being handed
/// out).
/// </summary>
[TestSubject(typeof(ObfuscatedSecretCache))]
public class ObfuscatedSecretCacheTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static ObfuscatedSecretCache Cache() => new(Log);

    [Fact]
    public void StoresAndReturnsAValue()
    {
        var cache = Cache();

        cache.Set("vault:1:a", "s3cret", TimeSpan.FromMinutes(15));

        Assert.Equal("s3cret", cache.Get("vault:1:a"));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void ReturnsNullForAKeyItNeverHeld()
    {
        Assert.Null(Cache().Get("vault:1:missing"));
    }

    [Fact]
    public async Task DoesNotServeAnExpiredEntryAndDropsIt()
    {
        var cache = Cache();

        cache.Set("vault:1:a", "s3cret", TimeSpan.FromMilliseconds(30));

        await Task.Delay(80);

        // The property that bounds the whole feature's exposure: a credential revoked in the vault
        // stops working here within the TTL, and not one request later.
        Assert.Null(cache.Get("vault:1:a"));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task ExpiryIsAbsoluteRatherThanSliding()
    {
        var cache = Cache();

        cache.Set("vault:1:a", "s3cret", TimeSpan.FromMilliseconds(120));

        // A busy sync job touches a credential constantly. With a sliding window this entry would
        // never expire, quietly turning a 15-minute cache into a permanent second copy of the secret.
        for (var i = 0; i < 4; i++)
        {
            await Task.Delay(40);
            cache.Get("vault:1:a");
        }

        Assert.Null(cache.Get("vault:1:a"));
    }

    [Fact]
    public void ANonPositiveTtlStoresNothingAndClearsWhatWasThere()
    {
        var cache = Cache();

        cache.Set("vault:1:a", "s3cret", TimeSpan.FromMinutes(5));
        cache.Set("vault:1:a", "s3cret", TimeSpan.Zero);

        Assert.Null(cache.Get("vault:1:a"));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void AnEmptyValueIsNotCached()
    {
        var cache = Cache();

        cache.Set("vault:1:a", "", TimeSpan.FromMinutes(5));

        Assert.Null(cache.Get("vault:1:a"));
    }

    [Fact]
    public void RemoveByPrefixEvictsOneConnectionAndLeavesTheRest()
    {
        var cache = Cache();

        cache.Set("vault:1:a", "one", TimeSpan.FromMinutes(5));
        cache.Set("vault:1:b", "two", TimeSpan.FromMinutes(5));
        cache.Set("vault:2:a", "three", TimeSpan.FromMinutes(5));

        var removed = cache.RemoveByPrefix("vault:1:");

        Assert.Equal(2, removed);
        Assert.Null(cache.Get("vault:1:a"));
        Assert.Null(cache.Get("vault:1:b"));
        Assert.Equal("three", cache.Get("vault:2:a"));
    }

    [Fact]
    public void RemoveAndClearDoWhatTheySay()
    {
        var cache = Cache();

        cache.Set("a", "1", TimeSpan.FromMinutes(5));
        cache.Set("b", "2", TimeSpan.FromMinutes(5));

        cache.Remove("a");
        Assert.Null(cache.Get("a"));
        Assert.Equal("2", cache.Get("b"));

        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void TheSameSecretUnderTwoKeysDoesNotProduceTheSameStoredBytes()
    {
        // Not cosmetic. Identical ciphertext for identical plaintext leaks which fields share a
        // credential to anything that can read the process's memory but not decrypt it — and it is
        // the property AES-GCM's per-entry salt exists to provide.
        var cache = Cache();

        cache.Set("k1", "same-secret", TimeSpan.FromMinutes(5));
        cache.Set("k2", "same-secret", TimeSpan.FromMinutes(5));

        var stored = StoredCiphertexts(cache);

        Assert.Equal(2, stored.Length);
        Assert.NotEqual(stored[0], stored[1]);

        // Both still decrypt to the original, so the difference is the salt and not corruption.
        Assert.Equal("same-secret", cache.Get("k1"));
        Assert.Equal("same-secret", cache.Get("k2"));
    }

    [Fact]
    public void ThePlaintextIsNotWhatIsHeldInMemory()
    {
        // The whole point of the class: a heap snapshot or a crash dump should not contain the
        // estate's credentials as scannable strings.
        var cache = Cache();

        cache.Set("k", "correct-horse-battery-staple", TimeSpan.FromMinutes(5));

        Assert.DoesNotContain(StoredCiphertexts(cache),
            text => text.Contains("correct-horse", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoCachesInTheSameProcessCannotReadEachOthersEntries()
    {
        // Each instance generates its own key, which is what makes a restart empty the cache by
        // construction. It is also why the cache must be registered as a singleton — a transient one
        // would have a 100% miss rate.
        var first = Cache();
        first.Set("k", "value", TimeSpan.FromMinutes(5));

        var second = Cache();

        Assert.Null(second.Get("k"));
        Assert.Equal("value", first.Get("k"));
    }

    /// <summary>
    /// The ciphertexts the cache is holding, read out of the private dictionary.
    ///
    /// Reflection, deliberately: the assertions above are about what is <em>in memory</em>, and an
    /// accessor added to the class for the test's benefit would be a way for production code to get
    /// at the raw entries.
    /// </summary>
    private static string[] StoredCiphertexts(ObfuscatedSecretCache cache)
    {
        var entriesField = typeof(ObfuscatedSecretCache)
            .GetField("_entries", System.Reflection.BindingFlags.Instance
                                  | System.Reflection.BindingFlags.NonPublic)!;

        var entries = (System.Collections.IEnumerable)entriesField.GetValue(cache)!;

        return (from object? pair in entries
                select pair!.GetType().GetProperty("Value")!.GetValue(pair)!
                into entry
                select (string)entry.GetType().GetProperty("Ciphertext")!.GetValue(entry)!)
            .ToArray();
    }
}

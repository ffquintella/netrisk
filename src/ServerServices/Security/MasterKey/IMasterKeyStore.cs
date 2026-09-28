using ServerServices.Interfaces;

namespace ServerServices.Security.MasterKey;

/// <summary>
/// One place a master key can live. The provider walks a list of these in preference order, most
/// protected first, and uses the first one that both reports itself available and survives a
/// write-then-read-back check.
/// </summary>
internal interface IMasterKeyStore
{
    /// <summary>What this store is, for logging and for <see cref="IMasterKeyProvider.Backing"/>.</summary>
    MasterKeyBacking Backing { get; }

    /// <summary>
    /// Whether this store can be used on this host at all — the right OS, the tooling present, the
    /// device node readable. Checked before anything is written, so an unavailable store costs one
    /// stat call rather than a failed key write.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable location, for the startup log and for operators who have to back it up.</summary>
    string Location { get; }

    /// <summary>The stored key, or null when this store holds nothing yet.</summary>
    string? TryRead();

    /// <summary>Stores <paramref name="keyBase64"/>, replacing anything already there.</summary>
    void Write(string keyBase64);
}

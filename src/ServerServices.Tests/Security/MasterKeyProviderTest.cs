using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Security.MasterKey;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// Resolution of the installation's credential-encryption key.
///
/// The properties under test are the ones whose failure mode is "every stored credential on the
/// installation is now unreadable": the key is stable across processes, an existing key is never
/// replaced, and a store that cannot actually give the key back is not used.
/// </summary>
[TestSubject(typeof(MasterKeyProvider))]
public class MasterKeyProviderTest : IDisposable
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "netrisk-masterkey-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProtectedFileMasterKeyStore FileStore() => new(_folder);

    [Fact]
    public void CreatesAKeyAndReusesItOnTheNextProcess()
    {
        var first = new MasterKeyProvider(Log, [FileStore()]).GetMasterKey();

        // A second provider over the same folder is what a restart is. This is the whole point of
        // the class: before it, the protector re-derived from a value that could change, and every
        // credential stopped decrypting.
        var second = new MasterKeyProvider(Log, [FileStore()]).GetMasterKey();

        Assert.Equal(first, second);
        Assert.Equal(32, Convert.FromBase64String(first).Length);
    }

    [Fact]
    public void TheCreatedKeyIsRandomPerInstallation()
    {
        var one = new MasterKeyProvider(Log, [FileStore()]).GetMasterKey();

        using var other = new MasterKeyProviderTest();
        var two = new MasterKeyProvider(Log, [other.FileStore()]).GetMasterKey();

        Assert.NotEqual(one, two);
    }

    [Fact]
    public void TheFileIsReadableOnlyByItsOwner()
    {
        new MasterKeyProvider(Log, [FileStore()]).GetMasterKey();

        var path = Path.Combine(_folder, "master.key");
        Assert.True(File.Exists(path));

        if (OperatingSystem.IsWindows()) return;

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(_folder));
    }

    [Fact]
    public void PrefersTheMostProtectedStoreThatWorks()
    {
        var hardware = new FakeStore(MasterKeyBacking.Tpm);

        var provider = new MasterKeyProvider(Log, [hardware, FileStore()]);
        var key = provider.GetMasterKey();

        Assert.Equal(MasterKeyBacking.Tpm, provider.Backing);
        Assert.Equal(key, hardware.Stored);

        // The weaker store must be left empty, not written "just in case": a copy of the key in a
        // plain file would undo the point of sealing it to hardware.
        Assert.False(File.Exists(Path.Combine(_folder, "master.key")));
    }

    [Fact]
    public void SkipsAStoreThatIsNotAvailableOnThisHost()
    {
        var absent = new FakeStore(MasterKeyBacking.Tpm) { IsAvailable = false };

        var provider = new MasterKeyProvider(Log, [absent, FileStore()]);
        provider.GetMasterKey();

        Assert.Equal(MasterKeyBacking.ProtectedFile, provider.Backing);
        Assert.Null(absent.Stored);
    }

    [Fact]
    public void SkipsAStoreThatThrowsOnWrite()
    {
        var broken = new FakeStore(MasterKeyBacking.Tpm) { FailOnWrite = true };

        var provider = new MasterKeyProvider(Log, [broken, FileStore()]);

        Assert.Equal(32, Convert.FromBase64String(provider.GetMasterKey()).Length);
        Assert.Equal(MasterKeyBacking.ProtectedFile, provider.Backing);
    }

    /// <summary>
    /// The TPM path cannot be exercised on a developer machine or in CI, so the provider does not
    /// take a zero exit code as proof — it unseals the key again and compares. A store that writes
    /// and then hands back something else would otherwise lose every credential written between
    /// that start and the next one.
    /// </summary>
    [Fact]
    public void SkipsAStoreThatDoesNotReturnWhatItWasGiven()
    {
        var lying = new FakeStore(MasterKeyBacking.Tpm) { CorruptOnRead = true };

        var provider = new MasterKeyProvider(Log, [lying, FileStore()]);
        var key = provider.GetMasterKey();

        Assert.Equal(MasterKeyBacking.ProtectedFile, provider.Backing);
        Assert.Equal(key, File.ReadAllText(Path.Combine(_folder, "master.key")));
    }

    /// <summary>
    /// A host that gains — or loses — TPM tooling after first start must keep the key it already
    /// has. Minting a second one beside it is the same outage this class exists to prevent.
    /// </summary>
    [Fact]
    public void ReadsAnExistingKeyFromALowerStoreRatherThanMintingANewOne()
    {
        var existing = new MasterKeyProvider(Log, [FileStore()]).GetMasterKey();

        var newHardware = new FakeStore(MasterKeyBacking.Tpm);
        var provider = new MasterKeyProvider(Log, [newHardware, FileStore()]);

        Assert.Equal(existing, provider.GetMasterKey());
        Assert.Equal(MasterKeyBacking.ProtectedFile, provider.Backing);
        Assert.Null(newHardware.Stored);
    }

    [Fact]
    public void ThrowsWhenNothingCanStoreTheKey()
    {
        var provider = new MasterKeyProvider(Log, [new FakeStore(MasterKeyBacking.Tpm) { FailOnWrite = true }]);

        var thrown = Assert.Throws<InvalidOperationException>(() => provider.GetMasterKey());
        Assert.Contains("NETRISK_SECRET_MASTER_KEY", thrown.Message);
    }

    /// <summary>
    /// The escape hatch for a container with no persistent volume and no TPM, which would otherwise
    /// mint a key on every start. Nothing may be written to disk when it is set — a deployment that
    /// supplies the key out of band has said where the key lives.
    /// </summary>
    [Fact]
    public void TheEnvironmentVariableWinsAndWritesNothing()
    {
        const string supplied = "Zm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyZm9vYmFyOTA=";
        var store = new FakeStore(MasterKeyBacking.Tpm);

        Environment.SetEnvironmentVariable("NETRISK_SECRET_MASTER_KEY", supplied);
        try
        {
            var provider = new MasterKeyProvider(Log, [store, FileStore()]);

            Assert.Equal(supplied, provider.GetMasterKey());
            Assert.Equal(MasterKeyBacking.Environment, provider.Backing);
            Assert.Null(store.Stored);
            Assert.False(Directory.Exists(_folder));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NETRISK_SECRET_MASTER_KEY", null);
        }
    }

    [Fact]
    public void ResolvesOnlyOnceAcrossConcurrentCallers()
    {
        var store = new FakeStore(MasterKeyBacking.Tpm);
        var provider = new MasterKeyProvider(Log, [store]);

        var keys = Enumerable.Range(0, 16)
            .AsParallel()
            .Select(_ => provider.GetMasterKey())
            .Distinct()
            .ToList();

        Assert.Single(keys);
        Assert.Equal(1, store.Writes);
    }

    private sealed class FakeStore(MasterKeyBacking backing) : IMasterKeyStore
    {
        public MasterKeyBacking Backing => backing;
        public bool IsAvailable { get; init; } = true;
        public string Location => "fake";

        public bool FailOnWrite { get; init; }
        public bool CorruptOnRead { get; init; }

        public string? Stored { get; private set; }
        public int Writes { get; private set; }

        public string? TryRead() => Stored is null || !CorruptOnRead ? Stored : Stored + "-tampered";

        public void Write(string keyBase64)
        {
            if (FailOnWrite) throw new InvalidOperationException("the fake store refuses writes");

            Writes++;
            Stored = keyBase64;
        }
    }
}

using System.Security.Cryptography;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Security.MasterKey;

/// <summary>
/// Resolves the installation's credential-encryption key once per process, from the most protected
/// place the host offers.
///
/// <para>
/// Order: the <c>NETRISK_SECRET_MASTER_KEY</c> environment variable, then hardware-backed storage
/// (TPM 2.0 on Linux, the keychain on macOS, DPAPI on Windows), then an owner-only file. A store
/// that is not available on this host is skipped; a store that is available but fails to store the
/// key is skipped too, and the failure is logged rather than swallowed.
/// </para>
/// <para>
/// Two properties matter more than which backend wins, because getting either wrong loses every
/// stored credential on the installation:
/// </para>
/// <list type="number">
/// <item>
/// <b>An existing key is never overwritten.</b> Reads run across every store before any write is
/// considered, so a host that gains TPM tooling — or loses it — keeps reading the key it already
/// had rather than minting a second one beside it.
/// </item>
/// <item>
/// <b>A newly written key is read back before it is used.</b> The TPM path in particular cannot be
/// exercised on a developer machine, so "the tool exited 0" is not accepted as proof; the key is
/// unsealed again and compared, and a mismatch demotes the store to the next one down. That turns
/// an untestable backend from a risk into a fallback.
/// </item>
/// </list>
/// </summary>
public sealed class MasterKeyProvider : IMasterKeyProvider
{
    /// <summary>
    /// Out-of-band override. A container that mounts no persistent volume has nowhere to put a key
    /// file and no TPM, and would otherwise mint a new key on every start — which is precisely the
    /// "all my credentials stopped decrypting" failure this class exists to end. Supplying this
    /// makes the key an input to the deployment instead.
    /// </summary>
    internal const string EnvironmentVariable = "NETRISK_SECRET_MASTER_KEY";

    /// <summary>256 bits, to match the AES-256 key <c>AesGcm256</c> derives from it.</summary>
    private const int KeyBytes = 32;

    private readonly ILogger _logger;
    private readonly IReadOnlyList<IMasterKeyStore> _stores;
    private readonly Lazy<Resolution> _resolution;

    public MasterKeyProvider(ILogger logger, IEnvironmentService environmentService)
        : this(logger, DefaultStores(Path.Combine(environmentService.ApplicationDataFolder, "secrets")))
    {
    }

    /// <summary>Test seam: an explicit, ordered store list, so a test never touches the install's real key.</summary>
    internal MasterKeyProvider(ILogger logger, IReadOnlyList<IMasterKeyStore> stores)
    {
        _logger = logger;
        _stores = stores;
        _resolution = new Lazy<Resolution>(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string GetMasterKey() => _resolution.Value.Key;

    public MasterKeyBacking Backing => _resolution.Value.Backing;

    public string Location => _resolution.Value.Location;

    /// <summary>
    /// The per-platform chain. Only stores that can exist on the running OS are constructed at all:
    /// the DPAPI and TPM types are annotated for their platform, and instantiating them elsewhere
    /// would be an analyzer error rather than a graceful skip.
    /// </summary>
    private static List<IMasterKeyStore> DefaultStores(string secretsFolder)
    {
        var stores = new List<IMasterKeyStore>();

        if (OperatingSystem.IsLinux()) stores.Add(new LinuxTpm2MasterKeyStore(secretsFolder));
        if (OperatingSystem.IsMacOS()) stores.Add(new MacOsKeychainMasterKeyStore());
        if (OperatingSystem.IsWindows()) stores.Add(new WindowsDpapiMasterKeyStore(secretsFolder));

        stores.Add(new ProtectedFileMasterKeyStore(secretsFolder));

        return stores;
    }

    private Resolution Resolve()
    {
        var fromEnvironment = System.Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            _logger.Information(
                "The credential encryption key is supplied by the {Variable} environment variable", EnvironmentVariable);
            return new Resolution(fromEnvironment.Trim(), MasterKeyBacking.Environment, EnvironmentVariable);
        }

        foreach (var store in _stores.Where(s => s.IsAvailable))
        {
            string? existing;
            try
            {
                existing = store.TryRead();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not read the credential encryption key from {Store}", store.Backing);
                continue;
            }

            if (string.IsNullOrWhiteSpace(existing)) continue;

            _logger.Information(
                "The credential encryption key is held by {Store} at {Location}", store.Backing, store.Location);
            return new Resolution(existing.Trim(), store.Backing, store.Location);
        }

        return Create();
    }

    private Resolution Create()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

        foreach (var store in _stores.Where(s => s.IsAvailable))
        {
            try
            {
                store.Write(key);

                // Read-back, not trust. See the class remarks: a store that reports success and
                // hands back something else on the next start would lose every credential written
                // in between, and that failure would first surface after a restart.
                if (store.TryRead()?.Trim() != key)
                {
                    _logger.Warning(
                        "{Store} accepted the new credential encryption key but did not return it again; "
                        + "falling back to the next store", store.Backing);
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not store the credential encryption key in {Store}", store.Backing);
                continue;
            }

            _logger.Information(
                "A new credential encryption key was created and stored by {Store} at {Location}",
                store.Backing, store.Location);
            return new Resolution(key, store.Backing, store.Location);
        }

        throw new InvalidOperationException(
            "No credential encryption key could be stored. Every backend failed, including the "
            + $"owner-only file — check that the application-data folder is writable, or set the "
            + $"{EnvironmentVariable} environment variable to a base64 32-byte key.");
    }

    private sealed record Resolution(string Key, MasterKeyBacking Backing, string Location);
}

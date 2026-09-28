using System.Runtime.Versioning;
using System.Text;
using ServerServices.Interfaces;

namespace ServerServices.Security.MasterKey;

/// <summary>
/// Seals the master key to the host's TPM 2.0 through <c>tpm2-tools</c>.
///
/// <para>
/// What this buys: the sealed blobs on disk are useless anywhere but this machine's TPM. Copying
/// <c>/var/netrisk</c> to another host, restoring a VM image elsewhere, or reading the files out of
/// a stolen disk yields nothing, because the unsealing key never leaves the chip.
/// </para>
/// <para>
/// What it does not buy: protection from a root process on the running host, which can simply ask
/// the TPM to unseal, exactly as this code does. The TPM moves the key out of reach of everything
/// that copies files; it does not move it out of reach of the machine.
/// </para>
/// <para>
/// The primary key is re-derived on every operation with <c>tpm2_createprimary</c> from the owner
/// hierarchy rather than persisted at a handle. Re-derivation is deterministic — the same hierarchy
/// seed and the same template give the same key — so the sealed blob stays loadable across reboots,
/// and nothing is left occupying a persistent handle that an operator would have to know about to
/// clean up. It costs a few hundred milliseconds, once, at startup.
/// </para>
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxTpm2MasterKeyStore(string secretsFolder) : IMasterKeyStore
{
    /// <summary>The resource-manager device. The raw <c>/dev/tpm0</c> is deliberately not accepted: it is
    /// single-open, so using it means racing whatever else on the host wants the TPM.</summary>
    private const string ResourceManagerDevice = "/dev/tpmrm0";

    private static readonly string[] RequiredTools = ["tpm2_createprimary", "tpm2_create", "tpm2_load", "tpm2_unseal"];

    private readonly string _publicPath = Path.Combine(secretsFolder, "master.key.tpm2.pub");
    private readonly string _privatePath = Path.Combine(secretsFolder, "master.key.tpm2.priv");

    public MasterKeyBacking Backing => MasterKeyBacking.Tpm;

    public bool IsAvailable =>
        OperatingSystem.IsLinux()
        && File.Exists(ResourceManagerDevice)
        && RequiredTools.All(CommandRunner.Exists);

    public string Location => $"{_publicPath} + {_privatePath} (sealed to the TPM at {ResourceManagerDevice})";

    public string? TryRead()
    {
        if (!File.Exists(_publicPath) || !File.Exists(_privatePath)) return null;

        // Every intermediate context goes in a directory that is removed on the way out. The
        // unsealed key itself never touches the filesystem — tpm2_unseal writes it to stdout.
        var work = CreateWorkspace();

        try
        {
            var primaryContext = Path.Combine(work, "primary.ctx");
            var sealContext = Path.Combine(work, "seal.ctx");

            if (!CreatePrimary(primaryContext)) return null;

            var load = CommandRunner.Run("tpm2_load",
                ["-C", primaryContext, "-u", _publicPath, "-r", _privatePath, "-c", sealContext]);
            if (!load.Success) return null;

            var unseal = CommandRunner.Run("tpm2_unseal", ["-c", sealContext]);
            if (!unseal.Success) return null;

            var value = unseal.StandardOutput.Trim();
            return value.Length == 0 ? null : value;
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    public void Write(string keyBase64)
    {
        ProtectedFileMasterKeyStore.CreateOwnerOnlyDirectory(secretsFolder);

        var work = CreateWorkspace();

        try
        {
            var primaryContext = Path.Combine(work, "primary.ctx");
            if (!CreatePrimary(primaryContext))
                throw new InvalidOperationException("tpm2_createprimary failed");

            // Sealed into fresh files in the workspace and moved into place only once the whole
            // sequence has succeeded. Writing straight to the real paths would mean a failure
            // half-way leaves a public blob with no matching private one, which reads back as a
            // TPM store that exists and cannot be unsealed — the one state with no way out.
            var stagedPublic = Path.Combine(work, "seal.pub");
            var stagedPrivate = Path.Combine(work, "seal.priv");

            var create = CommandRunner.Run("tpm2_create",
                ["-C", primaryContext, "-u", stagedPublic, "-r", stagedPrivate, "-i", "-"],
                Encoding.UTF8.GetBytes(keyBase64));

            if (!create.Success)
                throw new InvalidOperationException($"tpm2_create failed: {create.StandardError.Trim()}");

            File.Move(stagedPublic, _publicPath, overwrite: true);
            File.Move(stagedPrivate, _privatePath, overwrite: true);

            ProtectedFileMasterKeyStore.RestrictToOwner(_publicPath);
            ProtectedFileMasterKeyStore.RestrictToOwner(_privatePath);
        }
        finally
        {
            TryDeleteDirectory(work);
        }
    }

    private static bool CreatePrimary(string contextPath) =>
        CommandRunner.Run("tpm2_createprimary",
            ["-C", "o", "-g", "sha256", "-G", "ecc", "-c", contextPath]).Success;

    private string CreateWorkspace()
    {
        var work = Path.Combine(secretsFolder, ".tpm2-" + Guid.NewGuid().ToString("N"));
        ProtectedFileMasterKeyStore.CreateOwnerOnlyDirectory(work);
        return work;
    }

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }
}

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ServerServices.Interfaces;

namespace ServerServices.Security.MasterKey;

/// <summary>
/// Windows DPAPI at machine scope: the key is written to a file, but wrapped under a key the OS
/// holds and will only unwrap on this machine.
///
/// <para>
/// Machine scope rather than user scope deliberately. The server runs as a service account, and a
/// user-scoped blob stops decrypting the moment the service is moved to another account or its
/// profile is not loaded — which turns "the deployment changed service user" into "every stored
/// credential is lost". Machine scope is a weaker boundary (any process on the host can unwrap it)
/// but the host is not the boundary this protects; the database dump is.
/// </para>
/// <para>
/// Where Windows is configured to bind DPAPI's machine key to the TPM, this is TPM-backed without
/// any extra work here. Where it is not, it is still an OS-held key rather than one on disk.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsDpapiMasterKeyStore(string secretsFolder) : IMasterKeyStore
{
    /// <summary>
    /// DPAPI's optional entropy. Not a secret — it ships in the binary — and not pretending to be
    /// one: it scopes the blob so that another application on the same host cannot unwrap it by
    /// accident simply by passing our file to <c>Unprotect</c>.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("netrisk.secret.master-key.v1");

    private readonly string _path = Path.Combine(secretsFolder, "master.key.dpapi");

    public MasterKeyBacking Backing => MasterKeyBacking.Dpapi;

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string Location => _path;

    public string? TryRead()
    {
        if (!File.Exists(_path)) return null;

        try
        {
            var wrapped = File.ReadAllBytes(_path);
            if (wrapped.Length == 0) return null;

            var plain = ProtectedData.Unprotect(wrapped, Entropy, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            // A blob from a different machine — a restored image, a copied folder. Null rather than
            // a throw: the provider treats it as "nothing stored", writes a fresh key, and the
            // protector's legacy fallback is what keeps existing credentials readable meanwhile.
            return null;
        }
    }

    public void Write(string keyBase64)
    {
        ProtectedFileMasterKeyStore.CreateOwnerOnlyDirectory(secretsFolder);

        var wrapped = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(keyBase64), Entropy, DataProtectionScope.LocalMachine);

        File.WriteAllBytes(_path, wrapped);
    }
}

using ServerServices.Interfaces;

namespace ServerServices.Security.MasterKey;

/// <summary>
/// The fallback: the key in a file only its owner can read.
///
/// <para>
/// This is the same protection the JWT signing key has always had, so it is not a downgrade of the
/// installation's posture — but it is the weakest link in the chain, which is why it is last and why
/// the provider logs which store it actually landed on. Against the threat model that matters here
/// (a stolen database dump) an owner-only file is enough; against a compromised host it is not, and
/// neither is anything short of the TPM and keychain stores above it.
/// </para>
/// </summary>
internal sealed class ProtectedFileMasterKeyStore(string secretsFolder) : IMasterKeyStore
{
    private readonly string _path = Path.Combine(secretsFolder, "master.key");

    public MasterKeyBacking Backing => MasterKeyBacking.ProtectedFile;

    /// <summary>Always. A store that can fail to be available leaves the provider with nothing to fall back to.</summary>
    public bool IsAvailable => true;

    public string Location => _path;

    public string? TryRead()
    {
        if (!File.Exists(_path)) return null;

        var content = File.ReadAllText(_path).Trim();
        return content.Length == 0 ? null : content;
    }

    public void Write(string keyBase64)
    {
        CreateOwnerOnlyDirectory(secretsFolder);
        File.WriteAllText(_path, keyBase64);
        RestrictToOwner(_path);
    }

    /// <summary>
    /// Creates <paramref name="folder"/> with 0700 where the OS has POSIX modes.
    ///
    /// The mode is applied after creation as well as at creation, because <see cref="Directory.CreateDirectory(string)"/>
    /// leaves an already-existing directory's permissions alone — and on most of these installs the
    /// folder already exists, created world-readable by the code that writes the JWT key file.
    /// </summary>
    internal static void CreateOwnerOnlyDirectory(string folder)
    {
        Directory.CreateDirectory(folder);

        if (OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception)
        {
            // A filesystem that does not carry modes (a mounted share, some container overlays).
            // Not fatal: the write still has to round-trip before the provider will use this store.
        }
    }

    /// <summary>Narrows <paramref name="path"/> to 0600 where the OS has POSIX modes.</summary>
    internal static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception)
        {
            // See above.
        }
    }
}

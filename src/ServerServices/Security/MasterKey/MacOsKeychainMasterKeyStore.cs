using System.Runtime.Versioning;
using ServerServices.Interfaces;

namespace ServerServices.Security.MasterKey;

/// <summary>
/// The macOS keychain, reached through the <c>security</c> CLI.
///
/// <para>
/// This is the closest macOS equivalent of sealing to a TPM. The login keychain's own key is
/// protected by the Secure Enclave on Apple silicon, so the stored value is bound to the machine and
/// to the logged-in account rather than sitting in a file that a backup, a `tar` of the home
/// directory or a synced folder picks up by accident.
/// </para>
/// <para>
/// The CLI rather than the Security framework because the framework needs a native interop layer,
/// and the whole surface used here is two verbs. The cost of the CLI is real and worth naming: <c>security
/// add-generic-password</c> has no way to take the secret on stdin, so the key appears in this
/// process's argv for the few milliseconds the write takes, where a concurrent <c>ps</c> could see
/// it. That happens exactly once per installation, at first start, and the alternative fallback
/// leaves the key in a file permanently — but on a host where other users' processes are part of the
/// threat model, set <c>NETRISK_SECRET_MASTER_KEY</c> instead and nothing is ever written.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOsKeychainMasterKeyStore : IMasterKeyStore
{
    private const string SecurityTool = "/usr/bin/security";
    private const string Service = "netrisk-server";
    private const string Account = "secret-master-key";

    public MasterKeyBacking Backing => MasterKeyBacking.Keychain;

    public bool IsAvailable => OperatingSystem.IsMacOS() && File.Exists(SecurityTool);

    public string Location => $"macOS keychain, generic password service '{Service}' account '{Account}'";

    public string? TryRead()
    {
        var result = CommandRunner.Run(SecurityTool,
            ["find-generic-password", "-s", Service, "-a", Account, "-w"]);

        if (!result.Success) return null;

        var value = result.StandardOutput.Trim();
        return value.Length == 0 ? null : value;
    }

    public void Write(string keyBase64)
    {
        // -U updates the item when it already exists; without it a second write fails with
        // "item already exists" and the provider would fall through to the file store on every
        // start of an install that already has a keychain key.
        var result = CommandRunner.Run(SecurityTool,
            ["add-generic-password", "-U", "-s", Service, "-a", Account, "-w", keyBase64]);

        if (!result.Success)
            throw new InvalidOperationException(
                $"the keychain rejected the write: {result.StandardError.Trim()}");
    }
}

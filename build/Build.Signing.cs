using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NetRisk.Packaging;
using Nuke.Common;
using Nuke.Common.IO;
using static Nuke.Common.EnvironmentInfo;
using Serilog;

/// <summary>
/// Milestone 5.1 — automated code-signing pipelines.
///
/// Two rules shape everything here:
/// <list type="number">
/// <item>
/// No credential ever lives in the repository. Every value arrives as a Nuke parameter or an
/// environment variable, and anything secret is redacted before it reaches a log.
/// </item>
/// <item>
/// A missing certificate is not a build failure. Signing switches itself off with a single
/// warning line and the unsigned artifact is still produced — that is the normal case for a
/// developer and for a CI fork. Only <c>--require-signing</c> (or naming a signing mode
/// explicitly) turns a gap into an error.
/// </item>
/// </list>
/// </summary>
partial class Build
{
    // ---------------------------------------------------------------------------------------
    // Parameters. Nuke resolves these from the command line and the environment; the
    // NETRISK_-prefixed environment names are the documented CI contract and are consulted as
    // a fallback so a pipeline never has to guess Nuke's own naming.
    // ---------------------------------------------------------------------------------------

    [Parameter("Fail the build if signing material is missing instead of producing unsigned artifacts")]
    readonly bool RequireSigning;

    [Parameter("Fail the build if notarization cannot be performed (macOS)")]
    readonly bool RequireNotarization;

    [Parameter("Windows signing mode: auto (default), none, trustedsigning or signtool")]
    readonly string WindowsSigningMode;

    [Parameter("Azure Trusted Signing endpoint, e.g. https://eus.codesigning.azure.net")]
    readonly string TrustedSigningEndpoint;

    [Parameter("Azure Trusted Signing account name")]
    readonly string TrustedSigningAccount;

    [Parameter("Azure Trusted Signing certificate profile name")]
    readonly string TrustedSigningCertificateProfile;

    [Parameter("SHA-1 thumbprint of an installed Authenticode certificate (signtool /sha1)")]
    readonly string WindowsCertificateThumbprint;

    [Parameter("Path to an Authenticode PFX. Prefer a cloud HSM: file-based keys no longer meet the CA/B baseline")]
    readonly string WindowsCertificateFile;

    [Parameter("Password for the Authenticode PFX")]
    [Secret]
    readonly string WindowsCertificatePassword;

    [Parameter("CSP exposing a cloud-HSM Authenticode key (signtool /csp)")]
    readonly string WindowsCertificateCsp;

    [Parameter("Key container inside the CSP (signtool /kc)")]
    readonly string WindowsCertificateKeyContainer;

    [Parameter("Primary RFC 3161 timestamp URL")]
    readonly string TimestampUrl;

    [Parameter("Extra RFC 3161 timestamp URLs (comma separated), tried after the primary")]
    readonly string TimestampUrlFallbacks;

    [Parameter("macOS Developer ID identity, e.g. 'Developer ID Application: Acme Ltd (TEAMID1234)'")]
    readonly string MacSigningIdentity;

    [Parameter("macOS Developer ID Installer identity used to sign the .pkg")]
    readonly string MacInstallerSigningIdentity;

    [Parameter("Apple Developer Team ID")]
    readonly string MacTeamId;

    [Parameter("Base64 of a Developer ID .p12 to import into a throwaway keychain (CI)")]
    [Secret]
    readonly string MacCertificateBase64;

    [Parameter("Password of the .p12 passed through mac-certificate-base64")]
    [Secret]
    readonly string MacCertificatePassword;

    [Parameter("notarytool keychain profile name (developer machines)")]
    readonly string MacNotaryKeychainProfile;

    [Parameter("App Store Connect API key id for notarytool")]
    readonly string MacNotaryApiKeyId;

    [Parameter("App Store Connect issuer id for notarytool")]
    readonly string MacNotaryApiIssuerId;

    [Parameter("Path to the App Store Connect .p8 private key used by notarytool")]
    readonly string MacNotaryApiKeyPath;

    // ---------------------------------------------------------------------------------------
    // Option resolution
    // ---------------------------------------------------------------------------------------

    static string ParamOrEnv(string value, string environmentVariable) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : Environment.GetEnvironmentVariable(environmentVariable);

    static bool FlagOrEnv(bool value, string environmentVariable)
    {
        if (value)
            return true;

        var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);
        return !string.IsNullOrWhiteSpace(fromEnvironment) &&
               (fromEnvironment.Equals("1", StringComparison.Ordinal) ||
                fromEnvironment.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    bool SigningRequired => FlagOrEnv(RequireSigning, "NETRISK_REQUIRE_SIGNING");

    bool NotarizationRequired => FlagOrEnv(RequireNotarization, "NETRISK_REQUIRE_NOTARIZATION");

    WindowsSigningOptions WindowsSigning => new()
    {
        Mode = ParamOrEnv(WindowsSigningMode, "NETRISK_WINDOWS_SIGNING_MODE"),
        RequireSigning = SigningRequired,
        TrustedSigningEndpoint = ParamOrEnv(TrustedSigningEndpoint, "NETRISK_TRUSTED_SIGNING_ENDPOINT"),
        TrustedSigningAccount = ParamOrEnv(TrustedSigningAccount, "NETRISK_TRUSTED_SIGNING_ACCOUNT"),
        TrustedSigningCertificateProfile =
            ParamOrEnv(TrustedSigningCertificateProfile, "NETRISK_TRUSTED_SIGNING_CERTIFICATE_PROFILE"),
        CertificateThumbprint = ParamOrEnv(WindowsCertificateThumbprint, "NETRISK_WINDOWS_CERTIFICATE_THUMBPRINT"),
        CertificateFile = ParamOrEnv(WindowsCertificateFile, "NETRISK_WINDOWS_CERTIFICATE_FILE"),
        CertificatePassword = ParamOrEnv(WindowsCertificatePassword, "NETRISK_WINDOWS_CERTIFICATE_PASSWORD"),
        CryptoServiceProvider = ParamOrEnv(WindowsCertificateCsp, "NETRISK_WINDOWS_CERTIFICATE_CSP"),
        KeyContainer = ParamOrEnv(WindowsCertificateKeyContainer, "NETRISK_WINDOWS_CERTIFICATE_KEY_CONTAINER")
    };

    string EffectiveMacInstallerSigningIdentity =>
        ParamOrEnv(MacInstallerSigningIdentity, "NETRISK_MAC_INSTALLER_SIGNING_IDENTITY");

    MacSigningOptions MacSigning => new()
    {
        SigningIdentity = ParamOrEnv(MacSigningIdentity, "NETRISK_MAC_SIGNING_IDENTITY"),
        TeamId = ParamOrEnv(MacTeamId, "NETRISK_MAC_TEAM_ID"),
        CertificateBase64 = ParamOrEnv(MacCertificateBase64, "NETRISK_MAC_CERTIFICATE_BASE64"),
        CertificatePassword = ParamOrEnv(MacCertificatePassword, "NETRISK_MAC_CERTIFICATE_PASSWORD"),
        NotaryKeychainProfile = ParamOrEnv(MacNotaryKeychainProfile, "NETRISK_MAC_NOTARY_KEYCHAIN_PROFILE"),
        NotaryApiKeyId = ParamOrEnv(MacNotaryApiKeyId, "NETRISK_MAC_NOTARY_API_KEY_ID"),
        NotaryApiIssuerId = ParamOrEnv(MacNotaryApiIssuerId, "NETRISK_MAC_NOTARY_API_ISSUER_ID"),
        NotaryApiKeyPath = ParamOrEnv(MacNotaryApiKeyPath, "NETRISK_MAC_NOTARY_API_KEY_PATH"),
        RequireSigning = SigningRequired,
        RequireNotarization = NotarizationRequired
    };

    IReadOnlyList<string> TimestampUrls => TimestampServers.Resolve(
        ParamOrEnv(TimestampUrl, "NETRISK_TIMESTAMP_URL"),
        ParamOrEnv(TimestampUrlFallbacks, "NETRISK_TIMESTAMP_URL_FALLBACKS"));

    /// <summary>Every value that must never appear in a log line.</summary>
    IReadOnlyList<string> Secrets => new[]
    {
        WindowsSigning.CertificatePassword,
        MacSigning.CertificatePassword,
        MacSigning.CertificateBase64,
        KeychainPassword
    }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

    // ---------------------------------------------------------------------------------------
    // Windows Authenticode
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Signs the given files, or explains in one line why it did not. Safe to call with an
    /// empty list, and safe to call on a non-Windows host: signing is skipped with a reason.
    /// </summary>
    void SignWindowsArtifacts(IReadOnlyCollection<AbsolutePath> files, string what)
    {
        if (files.Count == 0)
            return;

        var plan = WindowsSigningPlanner.Plan(WindowsSigning);

        if (!plan.ShouldSign)
        {
            Log.Warning("Authenticode signing skipped for {What}: {Reason}", what, plan.Reason);
            return;
        }

        if (!IsWin)
        {
            const string reason =
                "Authenticode signing needs a Windows host (signtool/the sign CLI are Windows-only).";

            if (SigningRequired)
                throw new Exception(reason + " Run this target on a Windows runner.");

            Log.Warning("Authenticode signing skipped for {What}: {Reason}", what, reason);
            return;
        }

        Log.Information("{Reason} Signing {Count} file(s) for {What}.", plan.Reason, files.Count, what);

        foreach (var file in files)
        {
            switch (plan.Provider)
            {
                case WindowsSigningProvider.TrustedSigning:
                    SignWithTrustedSigning(file);
                    break;
                case WindowsSigningProvider.SignTool:
                    SignWithSignTool(file);
                    break;
                default:
                    throw new Exception($"Unexpected Windows signing provider '{plan.Provider}'.");
            }
        }

        VerifyWindowsSignatures(files);
    }

    void SignWithTrustedSigning(AbsolutePath file)
    {
        var options = WindowsSigning;
        var signCli = ResolveSignCli();

        // The `sign` CLI authenticates through DefaultAzureCredential, so the Azure
        // credentials themselves stay in the environment and never touch the command line.
        var timestamp = TimestampUrls.First();

        var arguments =
            $"code trusted-signing \"{file}\" " +
            $"--trusted-signing-endpoint \"{options.TrustedSigningEndpoint}\" " +
            $"--trusted-signing-account \"{options.TrustedSigningAccount}\" " +
            $"--trusted-signing-certificate-profile \"{options.TrustedSigningCertificateProfile}\" " +
            $"--timestamp-url \"{timestamp}\" " +
            "--file-digest SHA256 " +
            "--timestamp-digest SHA256 " +
            "--verbosity Warning";

        RunSensitiveProcess(signCli, arguments, RootDirectory);
    }

    void SignWithSignTool(AbsolutePath file)
    {
        var options = WindowsSigning;
        var signTool = ResolveSignTool();

        var key = new SignToolArguments();
        if (!string.IsNullOrWhiteSpace(options.CertificateThumbprint))
            key.Add($"/sha1 {options.CertificateThumbprint}");
        if (!string.IsNullOrWhiteSpace(options.CertificateFile))
            key.Add($"/f \"{options.CertificateFile}\"");
        if (!string.IsNullOrWhiteSpace(options.CertificatePassword))
            key.Add($"/p \"{options.CertificatePassword}\"");
        if (!string.IsNullOrWhiteSpace(options.CryptoServiceProvider))
            key.Add($"/csp \"{options.CryptoServiceProvider}\"");
        if (!string.IsNullOrWhiteSpace(options.KeyContainer))
            key.Add($"/kc \"{options.KeyContainer}\"");

        // Timestamping is not optional: without it the signature stops validating the day the
        // certificate expires. Timestamp authorities do go down, hence the ordered fallbacks.
        Exception lastFailure = null;
        foreach (var timestampUrl in TimestampUrls)
        {
            var arguments =
                $"sign /fd SHA256 /td SHA256 /tr \"{timestampUrl}\" {key} \"{file}\"";

            try
            {
                RunSensitiveProcess(signTool, arguments, RootDirectory, TimeSpan.FromMinutes(5));
                return;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
                Log.Warning("Timestamping via {Url} failed; trying the next authority.", timestampUrl);
            }
        }

        throw new Exception(
            $"signtool failed against every timestamp authority ({string.Join(", ", TimestampUrls)}).",
            lastFailure);
    }

    /// <summary>
    /// `signtool verify /pa /all` — the gate before an artifact may be published. Never
    /// weakened: a file that does not verify fails the build.
    /// </summary>
    void VerifyWindowsSignatures(IReadOnlyCollection<AbsolutePath> files)
    {
        if (!IsWin || files.Count == 0)
            return;

        var signTool = ResolveSignTool();

        foreach (var file in files)
            RunSensitiveProcess(signTool, $"verify /pa /all \"{file}\"", RootDirectory, TimeSpan.FromMinutes(2));

        Log.Information("Verified Authenticode signatures on {Count} file(s).", files.Count);
    }

    /// <summary>
    /// signtool ships in the Windows SDK and is usually not on PATH outside a developer
    /// command prompt, so fall back to the newest SDK build found under Windows Kits.
    /// </summary>
    string ResolveSignTool() => ResolveWindowsSdkTool("signtool.exe");

    string ResolveMakeAppx() => ResolveWindowsSdkTool("makeappx.exe");

    string ResolveWindowsSdkTool(string executable)
    {
        if (ToolOnPath(executable))
            return executable;

        var roots = new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("ProgramFiles")
            }
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.Combine(r!, "Windows Kits", "10", "bin"))
            .Where(Directory.Exists)
            .ToList();

        var candidate = roots
            .SelectMany(root => Directory.EnumerateFiles(root, executable, SearchOption.AllDirectories))
            .Where(path => path.Contains("x64", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (candidate is not null)
            return candidate;

        throw new Exception(
            $"{executable} was not found on PATH or under the Windows 10/11 SDK. " +
            "Install the Windows SDK (workload 'Microsoft.VisualStudio.Component.Windows11SDK') on the runner.");
    }

    string ResolveSignCli()
    {
        if (ToolOnPath("sign"))
            return "sign";

        throw new Exception(
            "The `sign` CLI (Azure Trusted Signing client) was not found. " +
            "Install it on the runner with: dotnet tool install --global sign");
    }

    // ---------------------------------------------------------------------------------------
    // macOS Developer ID + notarization
    // ---------------------------------------------------------------------------------------

    AbsolutePath MacEntitlementsFile => InstallersDirectory / "macos" / "entitlements.plist";

    string TemporaryKeychainPath;
    string KeychainPassword;

    /// <summary>
    /// Signs every Mach-O inside the bundle with the hardened runtime, then the bundle itself.
    /// Nested binaries are signed first — a bundle signature covers its contents, so signing
    /// the outside before the inside invalidates itself.
    /// </summary>
    void SignMacBundle(AbsolutePath appBundle, MacSigningPlan plan)
    {
        if (!plan.ShouldSign)
        {
            Log.Warning("macOS code signing skipped: {Reason}", plan.Reason);
            return;
        }

        var identity = MacSigning.SigningIdentity;

        var nested = Directory
            .EnumerateFiles(appBundle, "*", SearchOption.AllDirectories)
            .Where(IsMachOCandidate)
            .OrderByDescending(path => path.Count(c => c == Path.DirectorySeparatorChar))
            .ToList();

        foreach (var binary in nested)
        {
            RunSensitiveProcess("codesign",
                $"--force --timestamp --options runtime --sign \"{identity}\" \"{binary}\"",
                RootDirectory,
                TimeSpan.FromMinutes(10));
        }

        // The entitlements only belong on the main executable's signature, which the bundle
        // signature carries.
        RunSensitiveProcess("codesign",
            $"--force --timestamp --options runtime --entitlements \"{MacEntitlementsFile}\" " +
            $"--sign \"{identity}\" \"{appBundle}\"",
            RootDirectory,
            TimeSpan.FromMinutes(10));

        RunSensitiveProcess("codesign", $"--verify --strict --deep --verbose=2 \"{appBundle}\"", RootDirectory);

        Log.Information("Signed {Count} nested binary/binaries and the {Bundle} bundle with the hardened runtime.",
            nested.Count, appBundle.Name);
    }

    static bool IsMachOCandidate(string path)
    {
        // Mach-O only exists on macOS, and the executable-bit probe below has no meaning on
        // Windows -- returning early also keeps the platform-compatibility analyzer happy.
        if (OperatingSystem.IsWindows())
            return false;

        var name = Path.GetFileName(path);
        if (name.StartsWith('.'))
            return false;

        var extension = Path.GetExtension(path);
        if (extension is ".dylib" or ".so")
            return true;

        // Self-contained .NET output puts the apphost and a few helpers in the bundle with no
        // extension at all; anything executable and extension-less is a signing candidate.
        if (extension.Length != 0)
            return false;

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Signs a standalone file (a .dmg) rather than a bundle. Disk images carry a plain code
    /// signature and no entitlements.
    /// </summary>
    void SignMacFile(AbsolutePath file, MacSigningPlan plan)
    {
        if (!plan.ShouldSign)
            return;

        RunSensitiveProcess("codesign",
            $"--force --timestamp --sign \"{MacSigning.SigningIdentity}\" \"{file}\"",
            RootDirectory,
            TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Submits to Apple's notary service, waits, staples the ticket and asks Gatekeeper for a
    /// verdict. A rejection fails the build and prints the log URL Apple returned — that URL
    /// is the only way to find out which binary was at fault.
    /// </summary>
    void NotarizeAndStaple(AbsolutePath artifact, MacSigningPlan plan)
    {
        if (!plan.ShouldNotarize)
        {
            Log.Warning("Notarization skipped for {Artifact}: {Reason}", artifact.Name, plan.Reason);
            return;
        }

        // notarytool only accepts .zip, .dmg and .pkg. An .app has to be zipped first, with
        // ditto so that symlinks and resource forks survive the round trip.
        var submission = artifact;
        AbsolutePath temporaryZip = null;

        if (artifact.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            temporaryZip = artifact.Parent / (artifact.NameWithoutExtension + "-notarize.zip");
            temporaryZip.DeleteFile();
            RunProcess("ditto", $"-c -k --keepParent \"{artifact}\" \"{temporaryZip}\"", RootDirectory);
            submission = temporaryZip;
        }

        try
        {
            var credentials = NotaryCredentialArguments(plan);
            var output = RunSensitiveProcess("xcrun",
                $"notarytool submit \"{submission}\" --wait --output-format json {credentials}",
                RootDirectory,
                TimeSpan.FromMinutes(45));

            var status = Match(output, "\"status\"\\s*:\\s*\"([^\"]+)\"");
            var submissionId = Match(output, "\"id\"\\s*:\\s*\"([^\"]+)\"");

            if (!string.Equals(status, "Accepted", StringComparison.OrdinalIgnoreCase))
            {
                var detail = string.Empty;
                if (!string.IsNullOrWhiteSpace(submissionId))
                {
                    detail =
                        $" Apple's log for submission {submissionId} explains why; fetch it with: " +
                        $"xcrun notarytool log {submissionId} {DescribeNotaryCredentials(plan)}";

                    TryLogNotarizationFailure(submissionId, plan);
                }

                throw new Exception(
                    $"Notarization of {submission.Name} was not accepted (status: {status ?? "unknown"}).{detail}");
            }

            Log.Information("Notarization of {Artifact} accepted (submission {Id}).", submission.Name, submissionId);
        }
        finally
        {
            temporaryZip?.DeleteFile();
        }

        // The ticket is stapled to the original artifact, not to the zip we submitted.
        RunProcess("xcrun", $"stapler staple \"{artifact}\"", RootDirectory, TimeSpan.FromMinutes(10));

        AssessWithGatekeeper(artifact);
    }

    void TryLogNotarizationFailure(string submissionId, MacSigningPlan plan)
    {
        try
        {
            var log = RunSensitiveProcess("xcrun",
                $"notarytool log {submissionId} {NotaryCredentialArguments(plan)}",
                RootDirectory,
                TimeSpan.FromMinutes(5));

            Log.Error("Apple notarization log for {Id}:{NewLine}{Log}", submissionId, Environment.NewLine, log);
        }
        catch (Exception exception)
        {
            Log.Warning("Could not download the notarization log for {Id}: {Message}", submissionId, exception.Message);
        }
    }

    string NotaryCredentialArguments(MacSigningPlan plan) => plan.NotaryCredential switch
    {
        MacNotaryCredential.KeychainProfile => $"--keychain-profile \"{MacSigning.NotaryKeychainProfile}\"",
        MacNotaryCredential.ApiKey =>
            $"--key \"{MacSigning.NotaryApiKeyPath}\" --key-id \"{MacSigning.NotaryApiKeyId}\" " +
            $"--issuer \"{MacSigning.NotaryApiIssuerId}\"",
        _ => throw new Exception("Notarization was requested without credentials.")
    };

    /// <summary>The same credential flags, for printing in an error message.</summary>
    string DescribeNotaryCredentials(MacSigningPlan plan) => plan.NotaryCredential switch
    {
        MacNotaryCredential.KeychainProfile => "--keychain-profile <profile>",
        MacNotaryCredential.ApiKey => "--key <p8> --key-id <id> --issuer <issuer>",
        _ => string.Empty
    };

    void AssessWithGatekeeper(AbsolutePath artifact)
    {
        // spctl needs the right assessment type per artifact kind: an app is executed, a disk
        // image is opened, an installer package is installed. The wrong one reports
        // "rejected" on a perfectly good artifact.
        var name = artifact.Name;
        var (type, context) = true switch
        {
            _ when name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) => ("execute", string.Empty),
            _ when name.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase) =>
                ("install", " --context context:primary-signature"),
            _ => ("open", " --context context:primary-signature")
        };

        RunProcess("spctl", $"--assess --type {type}{context} --verbose=4 \"{artifact}\"", RootDirectory,
            TimeSpan.FromMinutes(5));

        Log.Information("Gatekeeper accepted {Artifact}.", artifact.Name);
    }

    /// <summary>
    /// Imports a CI-supplied .p12 into a throwaway keychain. The keychain is created with a
    /// random password, unlocked only for this process, and destroyed in
    /// <see cref="RemoveTemporaryKeychain"/> whatever happens.
    /// </summary>
    void ImportMacCertificate(MacSigningPlan plan)
    {
        if (!plan.ShouldImportCertificate)
            return;

        KeychainPassword = Guid.NewGuid().ToString("N");
        TemporaryKeychainPath = Path.Combine(Path.GetTempPath(), $"netrisk-signing-{Guid.NewGuid():N}.keychain-db");
        var certificatePath = Path.Combine(Path.GetTempPath(), $"netrisk-signing-{Guid.NewGuid():N}.p12");

        try
        {
            File.WriteAllBytes(certificatePath, Convert.FromBase64String(MacSigning.CertificateBase64!));

            RunSensitiveProcess("security", $"create-keychain -p \"{KeychainPassword}\" \"{TemporaryKeychainPath}\"", RootDirectory);
            RunSensitiveProcess("security", $"set-keychain-settings -lut 3600 \"{TemporaryKeychainPath}\"", RootDirectory);
            RunSensitiveProcess("security", $"unlock-keychain -p \"{KeychainPassword}\" \"{TemporaryKeychainPath}\"", RootDirectory);
            RunSensitiveProcess("security",
                $"import \"{certificatePath}\" -k \"{TemporaryKeychainPath}\" -P \"{MacSigning.CertificatePassword}\" " +
                "-T /usr/bin/codesign -T /usr/bin/productsign -f pkcs12 -A",
                RootDirectory);
            RunSensitiveProcess("security",
                $"set-key-partition-list -S apple-tool:,apple:,codesign: -s -k \"{KeychainPassword}\" \"{TemporaryKeychainPath}\"",
                RootDirectory);

            // Prepend, never replace: dropping the login keychain from the search list breaks
            // every other tool running on the machine.
            var current = RunProcessCapture("security", "list-keychains -d user");
            var existing = Regex.Matches(current, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
            var list = string.Join(" ", new[] { TemporaryKeychainPath }.Concat(existing).Select(k => $"\"{k}\""));
            RunSensitiveProcess("security", $"list-keychains -d user -s {list}", RootDirectory);

            Log.Information("Imported the Developer ID certificate into a temporary keychain.");
        }
        finally
        {
            if (File.Exists(certificatePath))
                File.Delete(certificatePath);
        }
    }

    void RemoveTemporaryKeychain()
    {
        if (string.IsNullOrEmpty(TemporaryKeychainPath))
            return;

        TryRun("security", $"delete-keychain \"{TemporaryKeychainPath}\"");
        Log.Information("Removed the temporary signing keychain.");

        TemporaryKeychainPath = null;
        KeychainPassword = null;
    }

    // ---------------------------------------------------------------------------------------
    // Verification target
    // ---------------------------------------------------------------------------------------

    Target VerifySignatures => _ => _
        .Description("Verifies the signatures of every packaged artifact found in output/publish")
        .Executes(() =>
        {
            if (!Directory.Exists(PublishDirectory))
            {
                Log.Warning("Nothing to verify: {Directory} does not exist. Run a Package* target first.", PublishDirectory);
                return;
            }

            var verified = 0;

            if (IsWin)
            {
                var windowsArtifacts = new[] { "*.exe", "*.msi", "*.msix", "*.dll" }
                    .SelectMany(pattern => PublishDirectory.GlobFiles("**/" + pattern))
                    .Distinct()
                    .ToList();

                VerifyWindowsSignatures(windowsArtifacts);
                verified += windowsArtifacts.Count;
            }

            if (IsOsx)
            {
                foreach (var bundle in PublishDirectory.GlobDirectories("**/*.app"))
                {
                    RunProcess("codesign", $"--verify --strict --deep --verbose=2 \"{bundle}\"", RootDirectory);
                    verified++;
                }

                foreach (var image in PublishDirectory.GlobFiles("**/*.dmg", "**/*.pkg"))
                {
                    RunProcess("codesign", $"--verify --verbose=2 \"{image}\"", RootDirectory);
                    verified++;
                }
            }

            if (verified == 0)
                Log.Warning(
                    "No artifact was verified. Signature verification only runs on the platform that produced the artifact " +
                    "(signtool on Windows, codesign on macOS).");
            else
                Log.Information("Verified {Count} artifact(s).", verified);
        });

    // ---------------------------------------------------------------------------------------
    // Process helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a command whose arguments may contain secrets: the command line is redacted before
    /// it is logged, and so is anything the command writes to stderr on failure.
    /// </summary>
    string RunSensitiveProcess(string fileName, string arguments, AbsolutePath workingDirectory,
        TimeSpan? timeout = null)
    {
        var secrets = Secrets;
        var display = SecretRedactor.Redact($"{fileName} {arguments}", secrets);

        Log.Debug("Running: {Command}", display);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        var effectiveTimeout = timeout ?? ProcessTimeout;
        if (!process.WaitForExit((int)effectiveTimeout.TotalMilliseconds))
        {
            TryKillProcessTree(process);
            throw new Exception(
                $"Command '{display}' timed out after {effectiveTimeout.TotalMinutes:0.#} minute(s) and was killed.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
            throw new Exception(
                $"Command '{display}' failed with exit code {process.ExitCode}: " +
                SecretRedactor.Redact(error, secrets));

        return output;
    }

    /// <summary>Runs a command and returns stdout, tolerating a non-zero exit code.</summary>
    static string RunProcessCapture(string fileName, string arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }

    static bool ToolOnPath(string executable)
    {
        var probe = EnvironmentInfo.IsWin ? "where" : "which";
        var output = RunProcessCapture(probe, executable);
        return !string.IsNullOrWhiteSpace(output);
    }

    static string Match(string text, string pattern)
    {
        var match = Regex.Match(text ?? string.Empty, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Tiny argument accumulator: keeps the signtool command line readable.</summary>
    private sealed class SignToolArguments
    {
        private readonly List<string> _parts = new();

        public void Add(string part) => _parts.Add(part);

        public override string ToString() => string.Join(" ", _parts);
    }
}

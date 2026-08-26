using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Serilog;

namespace ServerServices.Security;

/// <summary>What a signature check concluded about one plugin assembly.</summary>
/// <param name="IsSigned">A signature was present and cryptographically valid.</param>
/// <param name="Publisher">The signing certificate's subject, for the load log.</param>
/// <param name="Thumbprint">SHA-256 thumbprint of the signing certificate, uppercase hex.</param>
/// <param name="Detail">Why an unsigned or invalid result came out that way.</param>
public readonly record struct PluginSignatureResult(bool IsSigned, string? Publisher, string? Thumbprint,
    string? Detail);

/// <summary>
/// Verifies the publisher of a plugin assembly before it is loaded
/// (security finding NR-2026-027's proposed mitigation).
///
/// <para><b>What this is not.</b> It is not confinement. A loaded plugin still runs with the API's
/// full authority — .NET removed Code Access Security and has no supported in-process sandbox — so
/// the finding stays risk-accepted. What this changes is the trust decision: "any DLL that appears
/// in the plugins directory" becomes "a DLL from a publisher this installation named", and every
/// load records who signed it. That is the difference between an attacker needing code execution as
/// the service account and needing the publisher's signing key as well.</para>
///
/// <para><b>Two mechanisms, because one is not portable.</b> Authenticode
/// (<c>X509Certificate.CreateFromSignedFile</c>) only reads embedded signatures on Windows, and
/// NetRisk's API is normally deployed on Linux. So the primary mechanism is a <em>detached</em>
/// signature: <c>Foo.Plugin.dll.sig</c> (the raw signature bytes) beside
/// <c>Foo.Plugin.dll.cer</c> (the signing certificate), verified with the certificate's public key
/// over the SHA-256 of the assembly. That works identically on every platform and needs no OS trust
/// store, which also means an air-gapped installation can use it.</para>
/// </summary>
public class PluginSignatureVerifier(ILogger logger)
{
    /// <summary>Extension of the detached signature file, beside the assembly.</summary>
    public const string SignatureExtension = ".sig";

    /// <summary>Extension of the signing certificate, beside the assembly.</summary>
    public const string CertificateExtension = ".cer";

    /// <summary>
    /// Checks <paramref name="assemblyPath"/>. Never throws: a verification failure is a result, not
    /// an exception, because the caller's decision (skip or load with a warning) is a policy question
    /// and a thrown exception would make every unsigned plugin look like a crash.
    /// </summary>
    public PluginSignatureResult Verify(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            return new PluginSignatureResult(false, null, null, "The assembly file does not exist.");

        var detached = VerifyDetached(assemblyPath);
        if (detached.IsSigned || detached.Detail is not null and not "No detached signature present.")
            return detached;

        return VerifyAuthenticode(assemblyPath);
    }

    /// <summary>
    /// Whether the result satisfies the installation's policy.
    /// </summary>
    /// <param name="trustedThumbprints">
    /// SHA-256 thumbprints the installation trusts. Empty means "any valid signature", which is the
    /// weaker but still useful posture: it proves the file was not swapped after signing, without
    /// requiring the operator to enrol a publisher first.
    /// </param>
    public static bool IsTrusted(PluginSignatureResult result, IReadOnlyCollection<string> trustedThumbprints)
    {
        if (!result.IsSigned) return false;
        if (trustedThumbprints.Count == 0) return true;

        return result.Thumbprint is not null &&
               trustedThumbprints.Any(t =>
                   string.Equals(t.Replace(":", string.Empty).Trim(), result.Thumbprint,
                       StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses the comma- or whitespace-separated thumbprint list from settings.</summary>
    public static string[] ParseThumbprints(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? []
            : configured.Split([',', ';', ' ', '\n', '\r', '\t'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.Replace(":", string.Empty).ToUpperInvariant())
                .Distinct()
                .ToArray();

    private PluginSignatureResult VerifyDetached(string assemblyPath)
    {
        var signaturePath = assemblyPath + SignatureExtension;
        var certificatePath = assemblyPath + CertificateExtension;

        if (!File.Exists(signaturePath) || !File.Exists(certificatePath))
            return new PluginSignatureResult(false, null, null, "No detached signature present.");

        try
        {
            var signature = File.ReadAllBytes(signaturePath);
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);

            byte[] digest;
            using (var stream = File.OpenRead(assemblyPath)) digest = SHA256.HashData(stream);

            var thumbprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
            var subject = certificate.Subject;

            // The certificate's own validity window is checked as well: a signature made with an
            // expired certificate proves the file is unchanged but says nothing about whether the
            // publisher is still the publisher.
            var now = DateTime.UtcNow;
            if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
                return new PluginSignatureResult(false, subject, thumbprint,
                    $"The signing certificate is outside its validity window " +
                    $"({certificate.NotBefore:yyyy-MM-dd} to {certificate.NotAfter:yyyy-MM-dd}).");

            using var rsa = certificate.GetRSAPublicKey();
            if (rsa is not null &&
                rsa.VerifyHash(digest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                return new PluginSignatureResult(true, subject, thumbprint, null);

            using var ecdsa = certificate.GetECDsaPublicKey();
            if (ecdsa is not null && ecdsa.VerifyHash(digest, signature))
                return new PluginSignatureResult(true, subject, thumbprint, null);

            return new PluginSignatureResult(false, subject, thumbprint,
                "The detached signature does not match the assembly.");
        }
        catch (Exception ex)
        {
            logger.Warning("Could not verify the detached signature of {Path}: {Message}", assemblyPath,
                ex.Message);

            return new PluginSignatureResult(false, null, null,
                $"The detached signature could not be read: {ex.Message}");
        }
    }

    private PluginSignatureResult VerifyAuthenticode(string assemblyPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return new PluginSignatureResult(false, null, null,
                "No detached signature, and embedded Authenticode signatures can only be read on " +
                "Windows. Ship a .sig/.cer pair beside the assembly to sign a plugin portably.");

        try
        {
            // CreateFromSignedFile is the only API that reads an embedded Authenticode signature,
            // and it is marked obsolete in favour of X509CertificateLoader — which cannot read one.
            // Suppressed rather than "modernised" into something that does not do the job.
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(
                X509Certificate.CreateFromSignedFile(assemblyPath));
#pragma warning restore SYSLIB0057

            var thumbprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));

            // CreateFromSignedFile returns the certificate without validating the chain, so the
            // chain is built explicitly. Without this an attacker-generated certificate would read
            // as a valid signature.
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

            var chainValid = chain.Build(certificate);

            return chainValid
                ? new PluginSignatureResult(true, certificate.Subject, thumbprint, null)
                : new PluginSignatureResult(false, certificate.Subject, thumbprint,
                    "The Authenticode certificate chain did not validate.");
        }
        catch (CryptographicException ex)
        {
            return new PluginSignatureResult(false, null, null,
                $"The assembly carries no usable Authenticode signature: {ex.Message}");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace NetRisk.Packaging;

/// <summary>How the Windows artifacts get an Authenticode signature.</summary>
public enum WindowsSigningProvider
{
    /// <summary>Nothing is signed.</summary>
    None,

    /// <summary>Azure Trusted Signing through the `sign` CLI (cloud HSM, CI-friendly).</summary>
    TrustedSigning,

    /// <summary>`signtool` against a thumbprint, a CSP/key-container (cloud HSM) or a PFX.</summary>
    SignTool
}

/// <summary>
/// Everything the build knows about Windows signing material. All of it arrives from Nuke
/// parameters or environment variables; none of it is ever read from the repository.
/// </summary>
public sealed record WindowsSigningOptions
{
    /// <summary>"auto" (or null/empty), "none", "trustedsigning" or "signtool".</summary>
    public string? Mode { get; init; }

    /// <summary>
    /// Set by --require-signing. Turns "no material configured" from a skip into a build
    /// failure, which is what a release pipeline wants.
    /// </summary>
    public bool RequireSigning { get; init; }

    // --- Azure Trusted Signing ---
    public string? TrustedSigningEndpoint { get; init; }
    public string? TrustedSigningAccount { get; init; }
    public string? TrustedSigningCertificateProfile { get; init; }

    // --- signtool ---
    public string? CertificateThumbprint { get; init; }
    public string? CertificateFile { get; init; }
    public string? CertificatePassword { get; init; }
    public string? CryptoServiceProvider { get; init; }
    public string? KeyContainer { get; init; }
}

/// <summary>The resolved decision, plus the single-line reason that gets logged.</summary>
public sealed record WindowsSigningPlan(WindowsSigningProvider Provider, bool ShouldSign, string Reason);

/// <summary>Raised when signing was explicitly requested but cannot be carried out.</summary>
public sealed class SigningConfigurationException : Exception
{
    public SigningConfigurationException(string message) : base(message)
    {
    }
}

public static class WindowsSigningPlanner
{
    public const string ModeAuto = "auto";
    public const string ModeNone = "none";
    public const string ModeTrustedSigning = "trustedsigning";
    public const string ModeSignTool = "signtool";

    /// <summary>
    /// Decides whether and how to sign. The contract:
    /// <list type="bullet">
    /// <item>nothing configured and nothing demanded — skip, with a reason;</item>
    /// <item>a mode named explicitly, or --require-signing given — any gap is a hard error;</item>
    /// <item>credentials present — sign.</item>
    /// </list>
    /// </summary>
    public static WindowsSigningPlan Plan(WindowsSigningOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var mode = (options.Mode ?? ModeAuto).Trim().ToLowerInvariant();
        if (mode.Length == 0)
            mode = ModeAuto;

        var explicitMode = mode is not ModeAuto;

        if (mode is ModeNone)
        {
            if (options.RequireSigning)
                throw new SigningConfigurationException(
                    "--require-signing was given together with --windows-signing-mode none. Pick one.");

            return new WindowsSigningPlan(WindowsSigningProvider.None, false,
                "Windows signing disabled explicitly (--windows-signing-mode none).");
        }

        if (mode is not (ModeAuto or ModeTrustedSigning or ModeSignTool))
            throw new SigningConfigurationException(
                $"Unknown Windows signing mode '{options.Mode}'. Valid values: auto, none, trustedsigning, signtool.");

        var trustedSigningMissing = MissingTrustedSigningFields(options);
        var signToolMissing = MissingSignToolFields(options);

        if (mode is ModeTrustedSigning)
        {
            return trustedSigningMissing.Count == 0
                ? new WindowsSigningPlan(WindowsSigningProvider.TrustedSigning, true,
                    "Signing with Azure Trusted Signing.")
                : throw new SigningConfigurationException(
                    "Azure Trusted Signing was requested but is missing: " + Join(trustedSigningMissing) + ".");
        }

        if (mode is ModeSignTool)
        {
            return signToolMissing.Count == 0
                ? new WindowsSigningPlan(WindowsSigningProvider.SignTool, true, "Signing with signtool.")
                : throw new SigningConfigurationException(
                    "signtool signing was requested but is missing: " + Join(signToolMissing) + ".");
        }

        // Auto: prefer Trusted Signing, fall back to signtool, otherwise skip.
        if (trustedSigningMissing.Count == 0)
            return new WindowsSigningPlan(WindowsSigningProvider.TrustedSigning, true,
                "Signing with Azure Trusted Signing (auto-detected).");

        if (signToolMissing.Count == 0)
            return new WindowsSigningPlan(WindowsSigningProvider.SignTool, true,
                "Signing with signtool (auto-detected).");

        var reason =
            "No Windows signing material configured; producing unsigned artifacts. " +
            "Set the Azure Trusted Signing parameters or a signtool certificate to enable signing.";

        if (options.RequireSigning)
            throw new SigningConfigurationException("--require-signing was given but " + reason);

        // A half-configured provider is worth naming even when we only skip: it is almost
        // always a typo in a CI variable rather than a deliberate unsigned build.
        if (IsPartiallyConfigured(options))
            reason +=
                " Partial configuration detected — Trusted Signing missing: " + Join(trustedSigningMissing) +
                "; signtool missing: " + Join(signToolMissing) + ".";

        return new WindowsSigningPlan(WindowsSigningProvider.None, false, reason);
    }

    private static bool IsPartiallyConfigured(WindowsSigningOptions options) =>
        Present(options.TrustedSigningEndpoint) || Present(options.TrustedSigningAccount) ||
        Present(options.TrustedSigningCertificateProfile) || Present(options.CertificateThumbprint) ||
        Present(options.CertificateFile) || Present(options.CryptoServiceProvider) ||
        Present(options.KeyContainer);

    private static List<string> MissingTrustedSigningFields(WindowsSigningOptions options)
    {
        var missing = new List<string>();
        if (!Present(options.TrustedSigningEndpoint)) missing.Add("endpoint");
        if (!Present(options.TrustedSigningAccount)) missing.Add("account");
        if (!Present(options.TrustedSigningCertificateProfile)) missing.Add("certificate profile");
        return missing;
    }

    private static List<string> MissingSignToolFields(WindowsSigningOptions options)
    {
        // signtool can address the key three ways: an installed certificate (thumbprint),
        // a CSP/key-container pair (the cloud-HSM path) or a PFX file plus password.
        if (Present(options.CertificateThumbprint))
            return new List<string>();

        if (Present(options.CryptoServiceProvider) || Present(options.KeyContainer))
        {
            var missing = new List<string>();
            if (!Present(options.CryptoServiceProvider)) missing.Add("CSP name");
            if (!Present(options.KeyContainer)) missing.Add("key container");
            if (!Present(options.CertificateThumbprint) && !Present(options.CertificateFile))
                missing.Add("certificate thumbprint or file");
            return missing;
        }

        if (Present(options.CertificateFile))
        {
            // A password-less PFX makes signtool prompt interactively, which hangs CI.
            return Present(options.CertificatePassword)
                ? new List<string>()
                : new List<string> { "certificate password" };
        }

        return new List<string> { "certificate thumbprint, CSP/key-container pair or certificate file" };
    }

    private static string Join(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? "nothing" : string.Join(", ", values);

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);
}

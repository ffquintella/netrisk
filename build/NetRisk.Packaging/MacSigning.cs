using System;
using System.Collections.Generic;

namespace NetRisk.Packaging;

/// <summary>Which credential form `notarytool` will be handed.</summary>
public enum MacNotaryCredential
{
    None,

    /// <summary>A `notarytool store-credentials` profile in the keychain (developer laptop).</summary>
    KeychainProfile,

    /// <summary>An App Store Connect API key (.p8 + key id + issuer id) — the CI path.</summary>
    ApiKey
}

/// <summary>macOS signing/notarisation material, sourced only from parameters or the environment.</summary>
public sealed record MacSigningOptions
{
    /// <summary>e.g. "Developer ID Application: Example Corp (TEAMID1234)".</summary>
    public string? SigningIdentity { get; init; }

    public string? TeamId { get; init; }

    /// <summary>Base64 of a .p12 to import into a throwaway keychain (CI).</summary>
    public string? CertificateBase64 { get; init; }

    public string? CertificatePassword { get; init; }

    public string? NotaryKeychainProfile { get; init; }

    public string? NotaryApiKeyId { get; init; }

    public string? NotaryApiIssuerId { get; init; }

    public string? NotaryApiKeyPath { get; init; }

    public bool RequireSigning { get; init; }

    public bool RequireNotarization { get; init; }
}

public sealed record MacSigningPlan(
    bool ShouldSign,
    bool ShouldNotarize,
    bool ShouldImportCertificate,
    MacNotaryCredential NotaryCredential,
    string Reason);

public static class MacSigningPlanner
{
    /// <summary>
    /// Signing and notarisation are decided separately: an unsigned build is normal for a
    /// developer, a signed-but-not-notarised build is a legitimate intermediate state, and a
    /// notarised build always implies a signed one.
    /// </summary>
    public static MacSigningPlan Plan(MacSigningOptions options)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var requireSigning = options.RequireSigning || options.RequireNotarization;

        var hasIdentity = Present(options.SigningIdentity);
        var hasCertificateBlob = Present(options.CertificateBase64);

        if (hasCertificateBlob && !Present(options.CertificatePassword))
        {
            const string message =
                "A macOS signing certificate was supplied without its password; the keychain import would prompt.";
            if (requireSigning)
                throw new SigningConfigurationException(message);
            return new MacSigningPlan(false, false, false, MacNotaryCredential.None,
                message + " Producing an unsigned bundle.");
        }

        if (hasCertificateBlob && !hasIdentity)
        {
            const string message =
                "A macOS signing certificate was supplied but no --mac-signing-identity, so codesign has no identity to select.";
            if (requireSigning)
                throw new SigningConfigurationException(message);
            return new MacSigningPlan(false, false, false, MacNotaryCredential.None,
                message + " Producing an unsigned bundle.");
        }

        if (!hasIdentity)
        {
            const string reason =
                "No macOS Developer ID identity configured; producing an unsigned, un-notarized bundle.";
            if (requireSigning)
                throw new SigningConfigurationException("Signing was required but " + reason);
            return new MacSigningPlan(false, false, false, MacNotaryCredential.None, reason);
        }

        var credential = ResolveNotaryCredential(options, out var missing);

        if (credential is MacNotaryCredential.None)
        {
            var reason = missing.Count == 0
                ? "Signing with the Developer ID identity; no notarization credentials configured."
                : "Signing with the Developer ID identity; notarization skipped — missing: " +
                  string.Join(", ", missing) + ".";

            if (options.RequireNotarization)
                throw new SigningConfigurationException("Notarization was required but is " +
                    (missing.Count == 0
                        ? "not configured: supply either a notarytool keychain profile or an App Store Connect API key (key id, issuer id and .p8 path)."
                        : "missing: " + string.Join(", ", missing) + "."));

            return new MacSigningPlan(true, false, hasCertificateBlob, MacNotaryCredential.None, reason);
        }

        return new MacSigningPlan(true, true, hasCertificateBlob, credential,
            credential is MacNotaryCredential.ApiKey
                ? "Signing with the Developer ID identity and notarizing with an App Store Connect API key."
                : "Signing with the Developer ID identity and notarizing with a stored keychain profile.");
    }

    private static MacNotaryCredential ResolveNotaryCredential(MacSigningOptions options, out List<string> missing)
    {
        missing = new List<string>();

        var anyApiKeyField = Present(options.NotaryApiKeyId) || Present(options.NotaryApiIssuerId) ||
                             Present(options.NotaryApiKeyPath);

        if (anyApiKeyField)
        {
            if (!Present(options.NotaryApiKeyId)) missing.Add("notary API key id");
            if (!Present(options.NotaryApiIssuerId)) missing.Add("notary API issuer id");
            if (!Present(options.NotaryApiKeyPath)) missing.Add("notary API key (.p8) path");

            if (missing.Count == 0)
                return MacNotaryCredential.ApiKey;
        }

        // An API key beats a keychain profile when both are present: it is the CI-safe form.
        if (Present(options.NotaryKeychainProfile))
        {
            missing.Clear();
            return MacNotaryCredential.KeychainProfile;
        }

        return MacNotaryCredential.None;
    }

    private static bool Present(string? value) => !string.IsNullOrWhiteSpace(value);
}

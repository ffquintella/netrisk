using System;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// The contract these tests defend: a build with no signing material produces unsigned
/// artifacts and says so, and a build that asked for signing fails rather than silently
/// shipping something unsigned.
/// </summary>
public class WindowsSigningPlannerTest
{
    [Fact]
    public void NothingConfiguredSkipsSigningWithAReason()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions());

        Assert.False(plan.ShouldSign);
        Assert.Equal(WindowsSigningProvider.None, plan.Provider);
        Assert.Contains("No Windows signing material configured", plan.Reason);
    }

    [Fact]
    public void NothingConfiguredFailsWhenSigningWasRequired()
    {
        var exception = Assert.Throws<SigningConfigurationException>(() =>
            WindowsSigningPlanner.Plan(new WindowsSigningOptions { RequireSigning = true }));

        Assert.Contains("require-signing", exception.Message);
    }

    [Fact]
    public void ModeNoneSkipsSigning()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions { Mode = "None" });

        Assert.False(plan.ShouldSign);
        Assert.Contains("disabled explicitly", plan.Reason);
    }

    [Fact]
    public void ModeNoneTogetherWithRequireSigningIsContradictory() =>
        Assert.Throws<SigningConfigurationException>(() =>
            WindowsSigningPlanner.Plan(new WindowsSigningOptions { Mode = "none", RequireSigning = true }));

    [Fact]
    public void AnUnknownModeIsRejected()
    {
        var exception = Assert.Throws<SigningConfigurationException>(() =>
            WindowsSigningPlanner.Plan(new WindowsSigningOptions { Mode = "hsm-magic" }));

        Assert.Contains("auto, none, trustedsigning, signtool", exception.Message);
    }

    [Fact]
    public void CompleteTrustedSigningMaterialIsAutoDetected()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions
        {
            TrustedSigningEndpoint = "https://eus.codesigning.azure.net",
            TrustedSigningAccount = "netrisk",
            TrustedSigningCertificateProfile = "netrisk-public"
        });

        Assert.True(plan.ShouldSign);
        Assert.Equal(WindowsSigningProvider.TrustedSigning, plan.Provider);
    }

    [Fact]
    public void TrustedSigningIsPreferredOverSignToolWhenBothAreConfigured()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions
        {
            TrustedSigningEndpoint = "https://eus.codesigning.azure.net",
            TrustedSigningAccount = "netrisk",
            TrustedSigningCertificateProfile = "netrisk-public",
            CertificateThumbprint = "ABCDEF0123456789"
        });

        Assert.Equal(WindowsSigningProvider.TrustedSigning, plan.Provider);
    }

    [Fact]
    public void PartialTrustedSigningMaterialSkipsButNamesWhatIsMissing()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions
        {
            TrustedSigningEndpoint = "https://eus.codesigning.azure.net",
            TrustedSigningAccount = "netrisk"
        });

        Assert.False(plan.ShouldSign);
        Assert.Contains("certificate profile", plan.Reason);
    }

    [Fact]
    public void NamingTrustedSigningExplicitlyMakesAGapAnError()
    {
        var exception = Assert.Throws<SigningConfigurationException>(() =>
            WindowsSigningPlanner.Plan(new WindowsSigningOptions
            {
                Mode = "trustedsigning",
                TrustedSigningEndpoint = "https://eus.codesigning.azure.net"
            }));

        Assert.Contains("account", exception.Message);
        Assert.Contains("certificate profile", exception.Message);
    }

    [Fact]
    public void AThumbprintSelectsSignTool()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions
        {
            CertificateThumbprint = "ABCDEF0123456789"
        });

        Assert.True(plan.ShouldSign);
        Assert.Equal(WindowsSigningProvider.SignTool, plan.Provider);
    }

    [Fact]
    public void ACspAndKeyContainerPairSelectsSignTool()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions
        {
            CryptoServiceProvider = "DigiCert Signing Manager KSP",
            KeyContainer = "netrisk-key",
            CertificateFile = "cert.crt"
        });

        Assert.True(plan.ShouldSign);
        Assert.Equal(WindowsSigningProvider.SignTool, plan.Provider);
    }

    [Fact]
    public void AHalfConfiguredCspPairSkipsAndNamesWhatIsMissing()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions { KeyContainer = "netrisk-key" });

        Assert.False(plan.ShouldSign);
        Assert.Contains("CSP name", plan.Reason);
    }

    [Fact]
    public void APfxWithoutItsPasswordIsRejectedBecauseSignToolWouldPrompt()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions { CertificateFile = "netrisk.pfx" });

        Assert.False(plan.ShouldSign);
        Assert.Contains("certificate password", plan.Reason);
    }

    [Fact]
    public void APfxWithItsPasswordSelectsSignTool()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions
        {
            CertificateFile = "netrisk.pfx",
            CertificatePassword = "s3cret"
        });

        Assert.True(plan.ShouldSign);
        Assert.Equal(WindowsSigningProvider.SignTool, plan.Provider);
    }

    [Fact]
    public void ThePlanNeverLeaksThePasswordIntoItsReason()
    {
        var plan = WindowsSigningPlanner.Plan(new WindowsSigningOptions
        {
            CertificateFile = "netrisk.pfx",
            CertificatePassword = "correct-horse-battery-staple"
        });

        Assert.DoesNotContain("correct-horse-battery-staple", plan.Reason);
    }
}

public class MacSigningPlannerTest
{
    [Fact]
    public void NothingConfiguredProducesAnUnsignedBundle()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions());

        Assert.False(plan.ShouldSign);
        Assert.False(plan.ShouldNotarize);
        Assert.False(plan.ShouldImportCertificate);
        Assert.Contains("unsigned", plan.Reason);
    }

    [Fact]
    public void NothingConfiguredFailsWhenSigningWasRequired() =>
        Assert.Throws<SigningConfigurationException>(() =>
            MacSigningPlanner.Plan(new MacSigningOptions { RequireSigning = true }));

    [Fact]
    public void AnIdentityAloneSignsButDoesNotNotarize()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            SigningIdentity = "Developer ID Application: Acme Ltd (TEAMID1234)"
        });

        Assert.True(plan.ShouldSign);
        Assert.False(plan.ShouldNotarize);
        Assert.Equal(MacNotaryCredential.None, plan.NotaryCredential);
    }

    [Fact]
    public void RequiringNotarizationWithoutCredentialsIsAnError()
    {
        var exception = Assert.Throws<SigningConfigurationException>(() =>
            MacSigningPlanner.Plan(new MacSigningOptions
            {
                SigningIdentity = "Developer ID Application: Acme Ltd (TEAMID1234)",
                RequireNotarization = true
            }));

        Assert.Contains("notarytool keychain profile", exception.Message);
    }

    [Fact]
    public void AKeychainProfileNotarizes()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            SigningIdentity = "Developer ID Application: Acme Ltd (TEAMID1234)",
            NotaryKeychainProfile = "netrisk-notary"
        });

        Assert.True(plan.ShouldNotarize);
        Assert.Equal(MacNotaryCredential.KeychainProfile, plan.NotaryCredential);
    }

    [Fact]
    public void ACompleteApiKeyIsPreferredOverAKeychainProfile()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            SigningIdentity = "Developer ID Application: Acme Ltd (TEAMID1234)",
            NotaryKeychainProfile = "netrisk-notary",
            NotaryApiKeyId = "ABC123",
            NotaryApiIssuerId = "11111111-2222-3333-4444-555555555555",
            NotaryApiKeyPath = "/tmp/AuthKey_ABC123.p8"
        });

        Assert.Equal(MacNotaryCredential.ApiKey, plan.NotaryCredential);
    }

    [Fact]
    public void AnIncompleteApiKeyFallsBackToTheKeychainProfile()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            SigningIdentity = "Developer ID Application: Acme Ltd (TEAMID1234)",
            NotaryKeychainProfile = "netrisk-notary",
            NotaryApiKeyId = "ABC123"
        });

        Assert.Equal(MacNotaryCredential.KeychainProfile, plan.NotaryCredential);
    }

    [Fact]
    public void AnIncompleteApiKeyAndNoProfileSkipsNotarizationAndNamesWhatIsMissing()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            SigningIdentity = "Developer ID Application: Acme Ltd (TEAMID1234)",
            NotaryApiKeyId = "ABC123"
        });

        Assert.True(plan.ShouldSign);
        Assert.False(plan.ShouldNotarize);
        Assert.Contains("issuer id", plan.Reason);
        Assert.Contains(".p8", plan.Reason);
    }

    [Fact]
    public void ACertificateBlobRequestsAKeychainImport()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            SigningIdentity = "Developer ID Application: Acme Ltd (TEAMID1234)",
            CertificateBase64 = "ZmFrZQ==",
            CertificatePassword = "s3cret"
        });

        Assert.True(plan.ShouldSign);
        Assert.True(plan.ShouldImportCertificate);
    }

    [Fact]
    public void ACertificateBlobWithoutAPasswordWouldPromptSoItIsRefused()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            SigningIdentity = "Developer ID Application: Acme Ltd (TEAMID1234)",
            CertificateBase64 = "ZmFrZQ=="
        });

        Assert.False(plan.ShouldSign);
        Assert.Contains("without its password", plan.Reason);
    }

    [Fact]
    public void ACertificateBlobWithoutAnIdentityHasNothingToSignWith()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            CertificateBase64 = "ZmFrZQ==",
            CertificatePassword = "s3cret"
        });

        Assert.False(plan.ShouldSign);
        Assert.Contains("no --mac-signing-identity", plan.Reason);
    }

    [Fact]
    public void RequiringNotarizationImpliesRequiringSigning() =>
        Assert.Throws<SigningConfigurationException>(() =>
            MacSigningPlanner.Plan(new MacSigningOptions { RequireNotarization = true }));

    [Fact]
    public void ThePlanNeverLeaksTheCertificatePasswordIntoItsReason()
    {
        var plan = MacSigningPlanner.Plan(new MacSigningOptions
        {
            CertificateBase64 = "ZmFrZQ==",
            CertificatePassword = "correct-horse-battery-staple"
        });

        Assert.DoesNotContain("correct-horse-battery-staple", plan.Reason);
    }
}

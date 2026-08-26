using System;
using System.Collections.Generic;
using NetRisk.Packaging;
using Xunit;

namespace Packaging.Tests;

/// <summary>
/// Build logs are published artifacts, and signing commands necessarily carry passwords on
/// their command line. Anything that gets logged goes through here first.
/// </summary>
public class SecretRedactorTest
{
    [Fact]
    public void ASecretIsReplacedEverywhereItAppears()
    {
        var redacted = SecretRedactor.Redact(
            "signtool sign /f cert.pfx /p hunter2 file.exe (retry with /p hunter2)",
            new[] { "hunter2" });

        Assert.DoesNotContain("hunter2", redacted);
        Assert.Equal("signtool sign /f cert.pfx /p *** file.exe (retry with /p ***)", redacted);
    }

    [Fact]
    public void TheLongestSecretIsRedactedFirstSoOverlappingSecretsStillDisappear()
    {
        // "pass" is a substring of "password123". Redacting the short one first would leave
        // "***word123" behind, which still exposes most of the real secret.
        var redacted = SecretRedactor.Redact("token=password123", new[] { "pass", "password123" });

        Assert.Equal("token=***", redacted);
    }

    [Fact]
    public void EmptyAndWhitespaceSecretsAreIgnoredSoNothingIsMangled()
    {
        var redacted = SecretRedactor.Redact("codesign --sign identity app", new[] { "", "   ", null });

        Assert.Equal("codesign --sign identity app", redacted);
    }

    [Fact]
    public void NoSecretsMeansTheTextIsUntouched() =>
        Assert.Equal("hdiutil create", SecretRedactor.Redact("hdiutil create", null));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyInputProducesAnEmptyString(string? text) =>
        Assert.Equal(string.Empty, SecretRedactor.Redact(text, new[] { "secret" }));

    [Fact]
    public void RedactionIsCaseSensitiveBecauseSecretsAre()
    {
        var redacted = SecretRedactor.Redact("HUNTER2 and hunter2", new[] { "hunter2" });

        Assert.Equal("HUNTER2 and ***", redacted);
    }
}

public class TimestampServersTest
{
    [Fact]
    public void TheCallersPrimaryComesFirstThenExtrasThenTheDefaults()
    {
        var resolved = TimestampServers.Resolve("http://ts.example.com", "http://ts2.example.com,http://ts3.example.com");

        Assert.Equal("http://ts.example.com", resolved[0]);
        Assert.Equal("http://ts2.example.com", resolved[1]);
        Assert.Equal("http://ts3.example.com", resolved[2]);
        Assert.Equal(TimestampServers.Defaults[0], resolved[3]);
    }

    [Fact]
    public void ThereIsAlwaysAFallbackEvenWithNoConfiguration()
    {
        // A signature without a timestamp expires with the certificate, and a single
        // timestamp authority outage is the classic flaky release build.
        var resolved = TimestampServers.Resolve(null, null);

        Assert.True(resolved.Count >= 2);
        Assert.Equal(TimestampServers.Defaults, resolved);
    }

    [Fact]
    public void DuplicatesAreCollapsedCaseInsensitively()
    {
        var resolved = TimestampServers.Resolve(
            TimestampServers.Defaults[0].ToUpperInvariant(),
            TimestampServers.Defaults[0]);

        Assert.Equal(TimestampServers.Defaults.Count, resolved.Count);
        Assert.Equal(TimestampServers.Defaults[0].ToUpperInvariant(), resolved[0]);
    }

    [Fact]
    public void SemicolonsSeparateExtrasToo()
    {
        var resolved = TimestampServers.Resolve(null, "http://a.example.com;http://b.example.com");

        Assert.Equal("http://a.example.com", resolved[0]);
        Assert.Equal("http://b.example.com", resolved[1]);
    }

    [Fact]
    public void EveryDefaultIsAnAbsoluteUrl()
    {
        foreach (var url in TimestampServers.Defaults)
            Assert.True(Uri.TryCreate(url, UriKind.Absolute, out _), $"'{url}' is not an absolute URL.");
    }
}

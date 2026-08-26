using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using RiskPortal.Services;
using Xunit;

namespace RiskPortal.Tests.Services;

/// <summary>
/// The portal's identity with the API.
///
/// It has to be stable across restarts: every credential presentation the API accepts is checked
/// against an <em>approved</em> client registration, so a fresh id on each start would ask an
/// administrator to approve the portal again after every deployment — and an operator who has to
/// approve something weekly stops reading what they are approving.
/// </summary>
[TestSubject(typeof(PortalRegistration))]
public class PortalRegistrationTest : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "netrisk-portal-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private PortalRegistration New(string? configuredId = null) =>
        new(new PortalOptions
        {
            ClientId = configuredId,
            Hostname = "review-host",
            DataDirectory = _directory
        }, NullLogger<PortalRegistration>.Instance);

    [Fact]
    public void AConfiguredIdIsUsedAsIs()
    {
        Assert.Equal("portal-fixed", New("portal-fixed").ClientId);

        // Nothing is written when the id came from configuration — the file is a fallback, not a cache.
        Assert.False(File.Exists(Path.Combine(_directory, PortalRegistration.ClientIdFileName)));
    }

    [Fact]
    public void AGeneratedIdIsPersistedAndReusedOnTheNextStart()
    {
        var first = New().ClientId;
        var second = New().ClientId;

        Assert.Equal(first, second);
        Assert.True(File.Exists(Path.Combine(_directory, PortalRegistration.ClientIdFileName)));
    }

    /// <summary>
    /// Not a GUID. The id is what decides whether a client may present credentials at all, and
    /// <c>Guid.NewGuid</c> makes no cryptographic promise about its output — the rule in CLAUDE.md's
    /// security conventions table.
    /// </summary>
    [Fact]
    public void AGeneratedIdCarriesAHundredAndTwentyEightBitsOfEntropy()
    {
        var id = New().ClientId;

        Assert.StartsWith("portal-", id);
        Assert.Equal("portal-".Length + 32, id.Length);
        Assert.True(id["portal-".Length..].All(Uri.IsHexDigit));
    }

    [Fact]
    public void TwoFreshDirectoriesProduceDifferentIds()
    {
        var a = New().ClientId;

        var other = Path.Combine(Path.GetTempPath(), "netrisk-portal-tests-" + Guid.NewGuid().ToString("N"));

        try
        {
            var b = new PortalRegistration(new PortalOptions { DataDirectory = other },
                NullLogger<PortalRegistration>.Instance).ClientId;

            Assert.NotEqual(a, b);
        }
        finally
        {
            if (Directory.Exists(other)) Directory.Delete(other, recursive: true);
        }
    }

    [Fact]
    public void TheHostnameFallsBackToTheMachineName()
    {
        var registration = new PortalRegistration(new PortalOptions { DataDirectory = _directory },
            NullLogger<PortalRegistration>.Instance);

        Assert.Equal(Environment.MachineName, registration.Hostname);
    }

    [Fact]
    public void AConfiguredHostnameIsUsed()
    {
        Assert.Equal("review-host", New("portal-fixed").Hostname);
    }
}

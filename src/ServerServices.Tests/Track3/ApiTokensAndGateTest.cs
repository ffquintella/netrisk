using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Findings;
using ServerServices.Findings;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// Scoped CI API tokens (Track 3 milestone 3.5.1) and the exit-code gating policy (3.5.4).
///
/// The token half is checked hardest on what must <em>not</em> happen: the secret must never be
/// recoverable, a revoked or expired token must stop working immediately, and a wrong secret must be
/// indistinguishable from an unknown one.
/// </summary>
[TestSubject(typeof(ApiTokensService))]
public class ApiTokensAndGateTest : InMemoryServiceTestBase
{
    private readonly IApiTokensService _tokens;

    public ApiTokensAndGateTest()
    {
        _tokens = GetService<IApiTokensService>();

        Seed(ctx => ctx.Users.Add(new User
        {
            Value = 1, Name = "ci", Login = "ci", Enabled = true, Type = "local", Salt = "s",
            Password = Encoding.UTF8.GetBytes("p"), Email = "ci@example.com"
        }));
    }

    // --- issuing -----------------------------------------------------------------------------

    [Fact]
    public async Task TestIssuedTokenIsUsableAndCarriesItsScopes()
    {
        var issued = await _tokens.IssueAsync("github-actions", ApiTokenScopes.VulnerabilitiesImport,
            actsAsUserId: 1, createdByUserId: 1);

        Assert.StartsWith(ApiToken.SecretPrefix, issued.Secret);
        Assert.Equal([ApiTokenScopes.VulnerabilitiesImport], issued.Scopes);

        var authenticated = await _tokens.AuthenticateAsync(issued.Secret);

        Assert.NotNull(authenticated);
        Assert.Equal(1, authenticated!.UserId);
    }

    [Fact]
    public async Task TestTheSecretIsNeverStoredAndCannotBeRecovered()
    {
        var issued = await _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesImport, 1, 1);

        await using var db = OpenContext();
        var stored = db.ApiTokens.Single();

        // A leaked database dump must not hand over working tokens.
        Assert.DoesNotContain(issued.Secret, stored.SecretHash);
        Assert.NotEqual(issued.Secret, stored.SecretHash);
        // The key id is the public half and is stored in clear so lookup is one indexed read.
        Assert.Contains(stored.KeyId, issued.Secret);
    }

    [Fact]
    public async Task TestListingNeverExposesASecret()
    {
        var issued = await _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesRead, 1, 1);

        var listed = Assert.Single(await _tokens.GetTokensAsync());

        // There is no code path that can show a token again; that is deliberate.
        Assert.NotEqual(issued.Secret, listed.SecretHash);
        Assert.Equal(issued.KeyId, listed.KeyId);
    }

    [Fact]
    public async Task TestAtLeastOneScopeIsRequired()
    {
        // A token with no scopes can do nothing, which makes it a mistake rather than a credential.
        await Assert.ThrowsAsync<InvalidParameterException>(() => _tokens.IssueAsync("ci", "", 1, 1));
        await Assert.ThrowsAsync<InvalidParameterException>(() => _tokens.IssueAsync("ci", "   ", 1, 1));
    }

    [Fact]
    public async Task TestAnUnknownScopeIsRefusedAndNamed()
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _tokens.IssueAsync("ci", "vulnerabilities:import,everything", 1, 1));

        Assert.Contains("everything", ex.Message);
        Assert.Contains(ApiTokenScopes.VulnerabilitiesImport, ex.Message);
    }

    [Fact]
    public async Task TestNameAndExpiryAreValidated()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _tokens.IssueAsync("", ApiTokenScopes.VulnerabilitiesRead, 1, 1));

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesRead, 1, 1,
                expiresAt: DateTime.UtcNow.AddDays(-1)));
    }

    // --- authentication ----------------------------------------------------------------------

    [Fact]
    public async Task TestAWrongSecretIsRefusedJustLikeAnUnknownToken()
    {
        var issued = await _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesRead, 1, 1);

        var tampered = issued.Secret[..^4] + "aaaa";

        // Both answers are null: a caller holding a bad token learns only that it does not work.
        Assert.Null(await _tokens.AuthenticateAsync(tampered));
        Assert.Null(await _tokens.AuthenticateAsync($"{ApiToken.SecretPrefix}deadbeef_whatever"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Bearer something")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.e30.signature")]
    [InlineData("nrk_missing-separator")]
    [InlineData("nrk__")]
    public async Task TestMalformedTokensAreRefusedWithoutTouchingTheDatabase(string presented)
    {
        Assert.Null(await _tokens.AuthenticateAsync(presented));
    }

    [Fact]
    public async Task TestARevokedTokenStopsWorkingImmediately()
    {
        var issued = await _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesImport, 1, 1);

        await _tokens.RevokeAsync(issued.Id, revokedByUserId: 1);

        Assert.Null(await _tokens.AuthenticateAsync(issued.Secret));
    }

    [Fact]
    public async Task TestAnExpiredTokenStopsWorking()
    {
        var issued = await _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesImport, 1, 1,
            expiresAt: DateTime.UtcNow.AddMinutes(1));

        Seed(ctx => ctx.ApiTokens.Single().ExpiresAt = DateTime.UtcNow.AddMinutes(-1));

        Assert.Null(await _tokens.AuthenticateAsync(issued.Secret));
    }

    [Fact]
    public async Task TestRevokingTwiceIsHarmless()
    {
        var issued = await _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesRead, 1, 1);

        var first = await _tokens.RevokeAsync(issued.Id, 1);
        var second = await _tokens.RevokeAsync(issued.Id, 1);

        // The second call must not move the revocation date, or an audit trail loses when it
        // actually happened.
        Assert.Equal(first.RevokedAt, second.RevokedAt);
    }

    [Fact]
    public async Task TestRevokedTokensAreHiddenUnlessAskedFor()
    {
        var issued = await _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesRead, 1, 1);
        await _tokens.RevokeAsync(issued.Id, 1);

        Assert.Empty(await _tokens.GetTokensAsync());
        Assert.Single(await _tokens.GetTokensAsync(includeRevoked: true));
    }

    [Fact]
    public async Task TestRevokingAMissingTokenIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _tokens.RevokeAsync(404, 1));
    }

    [Fact]
    public async Task TestTouchRecordsLastUse()
    {
        var issued = await _tokens.IssueAsync("ci", ApiTokenScopes.VulnerabilitiesRead, 1, 1);

        var when = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        await _tokens.TouchAsync(issued.Id, when);

        Assert.Equal(when, (await _tokens.GetTokenAsync(issued.Id)).LastUsedAt);
    }

    [Fact]
    public async Task TestTouchingAMissingTokenDoesNotThrow()
    {
        // Recording use is a nicety; it must never fail an otherwise valid request.
        await _tokens.TouchAsync(404, DateTime.UtcNow);
    }

    [Theory]
    [InlineData("vulnerabilities:import", 1)]
    [InlineData("vulnerabilities:import,vulnerabilities:read", 2)]
    [InlineData("vulnerabilities:import,nonsense", 1)]
    [InlineData("VULNERABILITIES:IMPORT", 1)]
    [InlineData("", 0)]
    public void TestScopeParsingDropsUnknownNames(string raw, int expected)
    {
        // A typo must not grant anything, and it must not deny everything either.
        Assert.Equal(expected, ApiTokenScopes.Parse(raw).Length);
    }

    // --- CI gate policy (3.5.4) ---------------------------------------------------------------

    private static ScanImport Import(string? bySeverity, int status = (int)ScanImportStatus.Succeeded) => new()
    {
        Id = 1, Importer = "trivy", Status = status, NewBySeverity = bySeverity, StartedAt = DateTime.UtcNow
    };

    [Fact]
    public void TestGateFailsOnANewCritical()
    {
        var result = CiGatePolicy.Evaluate("new-critical", Import("{\"critical\":1,\"high\":3}"));

        Assert.True(result.Failed);
        Assert.Equal(1, result.Actual);
        Assert.Equal(0, result.Threshold);
    }

    [Fact]
    public void TestGatePassesWhenNothingNewCrossedTheBar()
    {
        var result = CiGatePolicy.Evaluate("new-critical", Import("{\"high\":3,\"medium\":10}"));

        Assert.False(result.Failed);
    }

    [Fact]
    public void TestSeverityThresholdCountsTheNamedBandAndWorse()
    {
        // "fail on new highs" does not mean "unless it is critical".
        var result = CiGatePolicy.Evaluate("any-high>5", Import("{\"critical\":2,\"high\":4}"));

        Assert.True(result.Failed);
        Assert.Equal(6, result.Actual);
        Assert.Equal(5, result.Threshold);
    }

    [Fact]
    public void TestSeverityThresholdIsInclusiveOfTheAllowance()
    {
        var result = CiGatePolicy.Evaluate("any-high>5", Import("{\"high\":5}"));

        Assert.False(result.Failed);
    }

    [Theory]
    [InlineData("new-high")]
    [InlineData("any-high")]
    [InlineData("NEW-HIGH")]
    [InlineData(" new-high ")]
    public void TestPolicySpellings(string policy)
    {
        Assert.True(CiGatePolicy.Evaluate(policy, Import("{\"high\":1}")).Failed);
    }

    [Fact]
    public void TestSlaBreachPolicy()
    {
        Assert.True(CiGatePolicy.Evaluate("sla-breach", Import(null), slaBreachedCount: 2).Failed);
        Assert.False(CiGatePolicy.Evaluate("sla-breach", Import(null), slaBreachedCount: 0).Failed);
    }

    [Fact]
    public void TestNonePolicyNeverFails()
    {
        Assert.False(CiGatePolicy.Evaluate("none", Import("{\"critical\":50}")).Failed);
    }

    [Fact]
    public void TestAFailedImportFailsTheGateWhateverThePolicy()
    {
        var failed = Import(null, (int)ScanImportStatus.Failed);
        failed.ErrorMessage = "the file was truncated";

        // Nobody can claim a build is clean when the scan results never landed.
        var result = CiGatePolicy.Evaluate("none", failed);

        Assert.True(result.Failed);
        Assert.Contains("truncated", result.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gibberish")]
    [InlineData("new-catastrophic")]
    [InlineData("any-high>lots")]
    public void TestAnUnparseablePolicyIsAClearError(string policy)
    {
        Assert.Throws<InvalidParameterException>(() => CiGatePolicy.Evaluate(policy, Import(null)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public void TestAMalformedSeverityBlobMeansNoNewFindingsRatherThanAnError(string? blob)
    {
        // The gate's job is to decide. Failing a build because a JSON blob is odd fails it for the
        // wrong reason.
        Assert.False(CiGatePolicy.Evaluate("new-critical", Import(blob)).Failed);
    }
}

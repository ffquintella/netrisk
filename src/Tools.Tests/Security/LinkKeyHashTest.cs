using JetBrains.Annotations;
using Tools.Security;
using Xunit;

namespace Tools.Tests.Security;

/// <summary>
/// Track 7 finding NR-2026-014, and the regression it nearly caused.
///
/// A password-reset link is created by the API, which stores <c>SHA-256(key)</c> in <c>key_hash</c>.
/// That row is then pushed to the WebSite over <c>/sync</c> **verbatim**, and the WebSite hashes the
/// key from the visitor's URL to look it up. Moving the API from MD5 to SHA-256 without moving the
/// WebSite would have made every reset link fail to resolve — presenting as an expired link, with
/// nothing logged. Hence one shared helper, and hence these tests.
/// </summary>
[TestSubject(typeof(LinkKeyHash))]
public class LinkKeyHashTest
{
    private const string Key = "aBcDeFgHiJkLmNoPqRsTuVwXyZ0123456789aBcD";

    [Fact]
    public void ThePrimaryDigestIsSha256()
    {
        var hash = LinkKeyHash.Primary(Key);

        Assert.Equal(64, hash.Length);
        Assert.Equal(HashTool.CreateSha256(Key), hash);
    }

    [Fact]
    public void TheLegacyDigestIsTheMd5PreviouslyStored()
    {
        var hash = LinkKeyHash.Legacy(Key);

        Assert.Equal(32, hash.Length);
        Assert.Equal(HashTool.CreateMD5(Key), hash);
    }

    /// <summary>The two must be distinguishable, or the fallback lookup would be ambiguous.</summary>
    [Fact]
    public void ThePrimaryAndLegacyDigestsDiffer() =>
        Assert.NotEqual(LinkKeyHash.Primary(Key), LinkKeyHash.Legacy(Key));

    [Fact]
    public void HashingIsDeterministic()
    {
        Assert.Equal(LinkKeyHash.Primary(Key), LinkKeyHash.Primary(Key));
        Assert.Equal(LinkKeyHash.Legacy(Key), LinkKeyHash.Legacy(Key));
    }

    [Fact]
    public void DifferentKeysHashDifferently() =>
        Assert.NotEqual(LinkKeyHash.Primary(Key), LinkKeyHash.Primary(Key + "x"));

    /// <summary>
    /// The stored column is <c>varchar(255)</c> on both sides, so the widened digest cannot truncate.
    /// Asserted here because a truncated hash would also present as an expired link.
    /// </summary>
    [Fact]
    public void TheDigestFitsTheStoredColumn() =>
        Assert.True(LinkKeyHash.Primary(Key).Length <= 255);
}

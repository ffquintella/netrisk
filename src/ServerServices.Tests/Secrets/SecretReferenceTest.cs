using JetBrains.Annotations;
using Model.Secrets;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// The stored form of "this field comes from the vault".
///
/// This string lives in credential columns in customers' databases, so its two properties are worth
/// nailing down: it round-trips whatever a vault calls its secrets, including the punctuation NetRisk
/// uses as delimiters; and nothing that is not a reference is ever mistaken for one, because the
/// consequence of that mistake is a real credential being parsed instead of sent.
/// </summary>
[TestSubject(typeof(SecretReference))]
public class SecretReferenceTest
{
    [Fact]
    public void RoundTripsASimpleReference()
    {
        var reference = SecretReference.Create(3, "db-prod");

        var text = reference.ToString();

        Assert.StartsWith("vault:v1:3:", text);
        Assert.True(SecretReference.TryParse(text, out var parsed));
        Assert.Equal(3, parsed.ConnectionId);
        Assert.Equal("db-prod", parsed.SecretId);
        Assert.Null(parsed.Field);
    }

    [Fact]
    public void RoundTripsAFieldReference()
    {
        var text = SecretReference.Create(12, "db-prod", "password").ToString();

        Assert.True(SecretReference.TryParse(text, out var parsed));
        Assert.Equal(12, parsed.ConnectionId);
        Assert.Equal("db-prod", parsed.SecretId);
        Assert.Equal("password", parsed.Field);
        Assert.Equal("db-prod / password", parsed.DisplayKey);
    }

    [Theory]
    // The reason the identifiers are encoded rather than written literally. A vault's secret id is
    // arbitrary text, and every one of these breaks a naive split on ':' or '/'.
    [InlineData("infra/db:prod", null)]
    [InlineData("a:b:c:d", "x:y")]
    [InlineData("segredo com espaços", "senha")]
    [InlineData("emoji 🔐 secret", "field/with/slashes")]
    [InlineData("vault:v1:9:looks-like-a-reference", null)]
    public void RoundTripsIdentifiersContainingTheDelimiters(string secretId, string? field)
    {
        var text = SecretReference.Create(5, secretId, field).ToString();

        Assert.True(SecretReference.TryParse(text, out var parsed));
        Assert.Equal(secretId, parsed.SecretId);
        Assert.Equal(field, parsed.Field);
    }

    [Fact]
    public void TreatsAWhitespaceFieldAsAbsent()
    {
        // What an untouched text box in the picker sends. A field of " " would encode, parse back, and
        // then be looked up in the vault as a field named " ".
        Assert.Null(SecretReference.Create(1, "s", "   ").Field);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-real-api-key")]
    [InlineData("enc:v2:AAAA")]
    [InlineData("vault:v2:1:abc")]      // a future format this build does not understand
    [InlineData("vaultv1:1:abc")]
    public void DoesNotClaimValuesThatAreNotReferences(string? value)
    {
        Assert.False(SecretReference.IsReference(value));
        Assert.False(SecretReference.TryParse(value, out _));
        Assert.Null(SecretReference.StoredReferenceOrNull(value));
    }

    [Theory]
    [InlineData("vault:v1:")]                  // nothing after the marker
    [InlineData("vault:v1:3")]                 // no secret id
    [InlineData("vault:v1:0:ZGI=")]            // connection ids start at 1
    [InlineData("vault:v1:-2:ZGI=")]
    [InlineData("vault:v1:abc:ZGI=")]          // non-numeric connection id
    [InlineData("vault:v1:3:!!!!")]            // not base64url
    [InlineData("vault:v1:3:ZGI=:")]           // empty field segment
    [InlineData("vault:v1:3:")]                // empty secret segment
    [InlineData("vault:v1:3:ZGIx:!!")]         // unparseable field
    public void RejectsAMalformedReferenceRatherThanGuessing(string value)
    {
        // Marked as a reference — so the resolver will refuse it rather than send it as a credential —
        // but not parseable, which is the state a truncated or hand-edited value is in.
        Assert.True(SecretReference.IsReference(value));
        Assert.False(SecretReference.TryParse(value, out _));
    }

    [Fact]
    public void StoredReferenceOrNullPassesAReferenceThrough()
    {
        var text = SecretReference.Create(1, "s").ToString();

        Assert.Equal(text, SecretReference.StoredReferenceOrNull(text));
    }

    [Fact]
    public void RefusesToBuildAReferenceThatCouldNotBeParsedBack()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => SecretReference.Create(0, "s"));
        Assert.Throws<System.ArgumentException>(() => SecretReference.Create(1, "   "));
    }

    [Fact]
    public void EncodesWithoutCharactersThatNeedEscapingElsewhere()
    {
        // base64url, not base64: a '+' or a '/' in a value that also appears in URLs, JSON and log
        // lines is an escaping bug waiting for the right secret name.
        var text = SecretReference.Create(1, "ÿþýü").ToString();

        Assert.DoesNotContain('+', text);
        Assert.DoesNotContain('=', text);
        Assert.DoesNotContain('/', text[SecretReference.Prefix.Length..]);
    }
}

using System.Collections.Generic;
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
    [InlineData("vault:v3:1:abc")]      // a future format this build does not understand
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

    // --- v2: the values a plugin's own controls produced ----------------------------------------

    /// <summary>
    /// The compatibility promise the whole second version rests on: a reference with no plugin
    /// values is still written exactly as it was before v2 existed. Every reference in every
    /// installed database is one of these, so if this changed, every one of them would.
    /// </summary>
    [Fact]
    public void StillWritesVersionOneWhenThereAreNoOptions()
    {
        Assert.StartsWith("vault:v1:", SecretReference.Create(3, "db-prod").ToString());
        Assert.StartsWith("vault:v1:", SecretReference.Create(3, "db-prod", "password").ToString());

        Assert.StartsWith("vault:v1:",
            SecretReference.Create(3, "db-prod", "password", new Dictionary<string, string>()).ToString());
    }

    [Fact]
    public void RoundTripsOptions()
    {
        var text = SecretReference.Create(7, "secret/prod/db", "password",
            new Dictionary<string, string> { ["environment"] = "hml" }).ToString();

        Assert.StartsWith("vault:v2:7:", text);
        Assert.True(SecretReference.TryParse(text, out var parsed));
        Assert.Equal(7, parsed.ConnectionId);
        Assert.Equal("secret/prod/db", parsed.SecretId);
        Assert.Equal("password", parsed.Field);
        Assert.Equal("hml", parsed.Options["environment"]);
    }

    /// <summary>
    /// v2 is fixed-arity, so "no field, one option" and "one field, no option" cannot be confused —
    /// which is the whole reason it is a new tag rather than a fourth segment bolted onto v1.
    /// </summary>
    [Fact]
    public void RoundTripsOptionsWithoutAField()
    {
        var text = SecretReference.Create(2, "secret/api", null,
            new Dictionary<string, string> { ["environment"] = "prd" }).ToString();

        Assert.True(SecretReference.TryParse(text, out var parsed));
        Assert.Null(parsed.Field);
        Assert.Equal("prd", parsed.Options["environment"]);
    }

    /// <summary>
    /// The canonical form is stable whatever order the values arrived in. The reference's own text
    /// is the cache key (<c>SecretVaultService.CacheKey</c>), so two spellings of one selection
    /// would be two cache entries and two reads of the same secret.
    /// </summary>
    [Fact]
    public void WritesOptionsInAStableOrder()
    {
        var one = SecretReference.Create(1, "s", null,
            new Dictionary<string, string> { ["environment"] = "hml", ["tenant"] = "acme" });

        var other = SecretReference.Create(1, "s", null,
            new Dictionary<string, string> { ["tenant"] = "acme", ["environment"] = "hml" });

        Assert.Equal(one.ToString(), other.ToString());
    }

    [Fact]
    public void RoundTripsOptionValuesThatContainTheDelimiters()
    {
        var text = SecretReference.Create(1, "s", null,
            new Dictionary<string, string> { ["environment"] = "a=b&c:d/e" }).ToString();

        Assert.True(SecretReference.TryParse(text, out var parsed));
        Assert.Equal("a=b&c:d/e", parsed.Options["environment"]);
    }

    /// <summary>
    /// A blank value is an untouched control, and a key that is not a key at all would produce a
    /// reference that cannot be parsed back. Both are dropped rather than written.
    /// </summary>
    [Fact]
    public void DropsBlankValuesAndUnusableKeys()
    {
        var reference = SecretReference.Create(1, "s", null, new Dictionary<string, string>
        {
            ["environment"] = "   ",
            ["Not A Key"] = "x",
            ["tenant"] = " acme "
        });

        Assert.Equal(new Dictionary<string, string> { ["tenant"] = "acme" }, reference.Options);

        // And a reference whose every value was dropped is a v1 reference again, because v1 is
        // what "no plugin values" is written as.
        Assert.StartsWith("vault:v1:",
            SecretReference.Create(1, "s", null,
                new Dictionary<string, string> { ["environment"] = "  " }).ToString());
    }

    [Theory]
    // Three segments where v2 has four.
    [InlineData("vault:v2:1:ZGI:cGFzcw")]
    // An empty option set, which nothing ever writes: accepting it would give one selection two
    // canonical forms, and the cache key is built from the canonical form.
    [InlineData("vault:v2:1:ZGI::")]
    // A repeated key has no meaning and no obvious winner.
    [InlineData("vault:v2:1:ZGI::ZW52PWEmZW52PWI")]
    public void RejectsAMalformedVersionTwoReference(string text)
    {
        Assert.False(SecretReference.TryParse(text, out _));

        // Still recognised as a reference, so the resolver refuses it loudly instead of sending the
        // literal string to a third party as a credential.
        Assert.True(SecretReference.IsReference(text));
    }

    /// <summary>
    /// Both versions are scanned when asking "is this connection still in use". Counting only v1
    /// would let a connection a v2 reference points at be deleted, and that field would then fail
    /// to resolve naming a connection that no longer exists.
    /// </summary>
    [Fact]
    public void NamesBothPrefixesForAConnection()
    {
        Assert.Equal(["vault:v1:4:", "vault:v2:4:"], SecretReference.ConnectionPrefixes(4));
    }

    /// <summary>
    /// The declared values are in the label, because "which environment did that read" is otherwise
    /// invisible in a log line and in the caption beside a credential field.
    /// </summary>
    [Fact]
    public void ShowsTheOptionsInTheDisplayKey()
    {
        var reference = SecretReference.Create(1, "secret/prod/db", "password",
            new Dictionary<string, string> { ["environment"] = "hml" });

        Assert.Equal("secret/prod/db / password (environment=hml)", reference.DisplayKey);
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

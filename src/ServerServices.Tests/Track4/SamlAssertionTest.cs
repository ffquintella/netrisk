using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using JetBrains.Annotations;
using Model.Authentication.Federation;
using ServerServices.Auth;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// SAML 2.0 assertion validation in the service-provider role (Track 4 milestone 4.3.1).
///
/// Every check here has been a real-world SAML bypass at some point: accepting a certificate embedded
/// in the response rather than one from metadata, verifying a signature that covers a different element
/// than the one being read (signature wrapping), ignoring the audience so an assertion minted for
/// another service provider is accepted, and leaving DTD processing on so the response is an XXE.
/// </summary>
[TestSubject(typeof(SamlAssertion))]
public class SamlAssertionTest
{
    private const string Acs = "https://netrisk.acme.com/IdentityProviders/1/saml/acs";
    private const string SpEntityId = "https://netrisk.acme.com/saml/metadata";

    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    private static X509Certificate2 NewCertificate()
    {
        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest("CN=idp.acme.com", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
    }

    private static string Metadata(X509Certificate2 certificate, string use = "signing") => $"""
        <?xml version="1.0"?>
        <EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="https://idp.acme.com">
          <IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
            <KeyDescriptor use="{use}">
              <KeyInfo xmlns="http://www.w3.org/2000/09/xmldsig#">
                <X509Data><X509Certificate>{Convert.ToBase64String(certificate.RawData)}</X509Certificate></X509Data>
              </KeyInfo>
            </KeyDescriptor>
            <SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect"
                                 Location="https://idp.acme.com/sso"/>
          </IDPSSODescriptor>
        </EntityDescriptor>
        """;

    /// <summary>Builds a response and signs the assertion, the way a real IdP does.</summary>
    private static string SignedResponse(X509Certificate2 certificate, string assertionId = "_a1",
        string? inResponseTo = "_req1", string audience = SpEntityId,
        DateTime? notBefore = null, DateTime? notOnOrAfter = null, string nameId = "alice@acme.com",
        string status = "urn:oasis:names:tc:SAML:2.0:status:Success", bool sign = true,
        string? signElementId = null)
    {
        var conditions =
            $"""<saml:Conditions NotBefore="{(notBefore ?? Now.AddMinutes(-5)):yyyy-MM-ddTHH:mm:ssZ}" NotOnOrAfter="{(notOnOrAfter ?? Now.AddMinutes(5)):yyyy-MM-ddTHH:mm:ssZ}"><saml:AudienceRestriction><saml:Audience>{audience}</saml:Audience></saml:AudienceRestriction></saml:Conditions>""";

        var xml =
            $"""<samlp:Response xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol" xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion" ID="_r1"{(inResponseTo == null ? "" : $" InResponseTo=\"{inResponseTo}\"")} Version="2.0" IssueInstant="{Now:yyyy-MM-ddTHH:mm:ssZ}"><samlp:Status><samlp:StatusCode Value="{status}"/></samlp:Status><saml:Assertion ID="{assertionId}" Version="2.0" IssueInstant="{Now:yyyy-MM-ddTHH:mm:ssZ}"><saml:Issuer>https://idp.acme.com</saml:Issuer><saml:Subject><saml:NameID Format="urn:oasis:names:tc:SAML:2.0:nameid-format:emailAddress">{nameId}</saml:NameID></saml:Subject>{conditions}<saml:AttributeStatement><saml:Attribute Name="http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"><saml:AttributeValue>{nameId}</saml:AttributeValue></saml:Attribute><saml:Attribute Name="name"><saml:AttributeValue>Alice Adams</saml:AttributeValue></saml:Attribute><saml:Attribute Name="groups"><saml:AttributeValue>Security-Admins</saml:AttributeValue><saml:AttributeValue>Everyone</saml:AttributeValue></saml:Attribute></saml:AttributeStatement></saml:Assertion></samlp:Response>""";

        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.LoadXml(xml);

        if (sign)
        {
            var target = signElementId ?? assertionId;

            var signedXml = new SignedXml(document) { SigningKey = certificate.GetRSAPrivateKey() };

            var reference = new Reference("#" + target);
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigExcC14NTransform());
            signedXml.AddReference(reference);

            signedXml.ComputeSignature();

            var element = FindById(document, target)!;
            element.AppendChild(document.ImportNode(signedXml.GetXml(), true));
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(document.OuterXml));
    }

    private static XmlElement? FindById(XmlDocument document, string id)
    {
        foreach (XmlNode node in document.GetElementsByTagName("*"))
            if (node is XmlElement element && element.GetAttribute("ID") == id)
                return element;

        return null;
    }

    private static ClaimMapping Mapping() => new()
    {
        Email = "emailaddress", Name = "name", Subject = "emailaddress", Groups = "groups"
    };

    // --- happy path --------------------------------------------------------------------------

    [Fact]
    public void AValidSignedAssertionYieldsTheMappedIdentity()
    {
        using var certificate = NewCertificate();

        var outcome = SamlAssertion.Validate(SignedResponse(certificate), [certificate], SpEntityId,
            "_req1", Mapping(), requireSignature: true, clockSkewSeconds: 120, Now);

        Assert.True(outcome.Valid, outcome.Error);

        var identity = outcome.Identity!;

        Assert.Equal("alice@acme.com", identity.Email);
        Assert.Equal("Alice Adams", identity.Name);
        // Multi-valued group attributes are what group mapping reads.
        Assert.Equal(2, identity.Groups.Count);
        Assert.Contains("Security-Admins", identity.Groups);
    }

    [Fact]
    public void AUriStyleAttributeNameIsAlsoIndexedByItsShortForm()
    {
        using var certificate = NewCertificate();

        var outcome = SamlAssertion.Validate(SignedResponse(certificate), [certificate], SpEntityId,
            "_req1", Mapping(), true, 120, Now);

        // Operators configure "emailaddress" far more often than the full schemas.xmlsoap.org URI.
        Assert.Contains("emailaddress", outcome.Identity!.Claims.Keys);
        Assert.Contains("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
            outcome.Identity.Claims.Keys);
    }

    // --- signature ---------------------------------------------------------------------------

    [Fact]
    public void AnUnsignedAssertionIsRefusedWhenSignaturesAreRequired()
    {
        using var certificate = NewCertificate();

        var outcome = SamlAssertion.Validate(SignedResponse(certificate, sign: false), [certificate],
            SpEntityId, "_req1", Mapping(), requireSignature: true, 120, Now);

        Assert.False(outcome.Valid);
        Assert.Contains("signature", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnAssertionSignedByAnotherKeyIsRefused()
    {
        using var signer = NewCertificate();
        using var trusted = NewCertificate();

        // Signed by a key the IdP metadata does not name — which is what an attacker controls.
        var outcome = SamlAssertion.Validate(SignedResponse(signer), [trusted], SpEntityId, "_req1",
            Mapping(), true, 120, Now);

        Assert.False(outcome.Valid);
    }

    [Fact]
    public void AResponseLevelSignatureIsAccepted()
    {
        using var certificate = NewCertificate();

        // Either the response or the assertion may carry the signature; both are legitimate, and what
        // matters is that the element whose contents are read is inside a verified one.
        var response = SignedResponse(certificate, assertionId: "_a1", signElementId: "_r1");

        var outcome = SamlAssertion.Validate(response, [certificate], SpEntityId, "_req1", Mapping(),
            true, 120, Now);

        Assert.True(outcome.Valid, outcome.Error);
    }

    [Fact]
    public void AWrappedAssertionIsRefused()
    {
        using var certificate = NewCertificate();

        // The signature-wrapping attack: a genuinely signed assertion is smuggled somewhere the parser
        // does not read from, and an unsigned forgery is put where it does. The signature verifies
        // against the decoy, so a check that only asked "does the document contain a valid signature"
        // would accept the forgery.
        var legitimate = Encoding.UTF8.GetString(Convert.FromBase64String(SignedResponse(certificate)));

        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        document.LoadXml(legitimate);

        var signed = FindById(document, "_a1")!;

        var forgery = (XmlElement)signed.CloneNode(true);
        forgery.SetAttribute("ID", "_forged");

        // Strip the signature from the copy and change who it asserts.
        var signature = forgery["Signature", SignedXml.XmlDsigNamespaceUrl];
        if (signature != null) forgery.RemoveChild(signature);

        foreach (XmlNode node in forgery.GetElementsByTagName("NameID",
                     "urn:oasis:names:tc:SAML:2.0:assertion"))
            node.InnerText = "attacker@evil.example";

        // The signed original is moved out of the way; the forgery takes its place.
        var extensions = document.CreateElement("samlp", "Extensions",
            "urn:oasis:names:tc:SAML:2.0:protocol");
        document.DocumentElement!.RemoveChild(signed);
        extensions.AppendChild(signed);
        document.DocumentElement.AppendChild(extensions);
        document.DocumentElement.AppendChild(forgery);

        var wrapped = Convert.ToBase64String(Encoding.UTF8.GetBytes(document.OuterXml));

        var outcome = SamlAssertion.Validate(wrapped, [certificate], SpEntityId, "_req1", Mapping(),
            true, 120, Now);

        Assert.False(outcome.Valid);
    }

    [Fact]
    public void AnUnsignedAssertionIsAcceptedOnlyWhenTheProviderExplicitlyAllowsIt()
    {
        using var certificate = NewCertificate();

        var outcome = SamlAssertion.Validate(SignedResponse(certificate, sign: false), [certificate],
            SpEntityId, "_req1", Mapping(), requireSignature: false, 120, Now);

        // Permitted for a test IdP that cannot sign; the service logs a warning on every use.
        Assert.True(outcome.Valid, outcome.Error);
    }

    // --- conditions --------------------------------------------------------------------------

    [Fact]
    public void AnExpiredAssertionIsRefused()
    {
        using var certificate = NewCertificate();

        var response = SignedResponse(certificate, notOnOrAfter: Now.AddMinutes(-10));

        var outcome = SamlAssertion.Validate(response, [certificate], SpEntityId, "_req1", Mapping(),
            true, 0, Now);

        Assert.False(outcome.Valid);
        Assert.Contains("expired", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClockSkewToleranceCoversASmallDifference()
    {
        using var certificate = NewCertificate();

        var response = SignedResponse(certificate, notBefore: Now.AddSeconds(30));

        Assert.False(SamlAssertion.Validate(response, [certificate], SpEntityId, "_req1", Mapping(),
            true, clockSkewSeconds: 0, Now).Valid);

        Assert.True(SamlAssertion.Validate(response, [certificate], SpEntityId, "_req1", Mapping(),
            true, clockSkewSeconds: 120, Now).Valid);
    }

    [Fact]
    public void AnAssertionForAnotherServiceProviderIsRefused()
    {
        using var certificate = NewCertificate();

        var response = SignedResponse(certificate, audience: "https://someone-else.example/saml");

        var outcome = SamlAssertion.Validate(response, [certificate], SpEntityId, "_req1", Mapping(),
            true, 120, Now);

        // The whole purpose of the audience restriction.
        Assert.False(outcome.Valid);
        Assert.Contains("audience", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AResponseThatAnswersADifferentRequestIsRefused()
    {
        using var certificate = NewCertificate();

        var outcome = SamlAssertion.Validate(SignedResponse(certificate, inResponseTo: "_other"),
            [certificate], SpEntityId, "_req1", Mapping(), true, 120, Now);

        // An unsolicited or injected assertion; refused before anything expensive happens.
        Assert.False(outcome.Valid);
        Assert.Contains("InResponseTo", outcome.Error!);
    }

    [Fact]
    public void AFailedStatusIsReportedAsTheIdentityProvidersRefusal()
    {
        using var certificate = NewCertificate();

        var response = SignedResponse(certificate,
            status: "urn:oasis:names:tc:SAML:2.0:status:Responder");

        var outcome = SamlAssertion.Validate(response, [certificate], SpEntityId, "_req1", Mapping(),
            true, 120, Now);

        Assert.False(outcome.Valid);
        Assert.Contains("refused", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    // --- malformed input ---------------------------------------------------------------------

    [Fact]
    public void NonBase64InputIsRejectedCleanly()
    {
        var outcome = SamlAssertion.Validate("not base64!", [], SpEntityId, null, Mapping(), true, 120, Now);

        Assert.False(outcome.Valid);
        Assert.Contains("base64", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonSamlXmlIsRejectedCleanly()
    {
        var xml = Convert.ToBase64String(Encoding.UTF8.GetBytes("<hello/>"));

        var outcome = SamlAssertion.Validate(xml, [], SpEntityId, null, Mapping(), true, 120, Now);

        Assert.False(outcome.Valid);
        Assert.Contains("not a SAML", outcome.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DtdProcessingIsProhibitedSoAResponseCannotBeAnXxe()
    {
        const string malicious = """
            <!DOCTYPE root [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <samlp:Response xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol" ID="_r1">&xxe;</samlp:Response>
            """;

        // A SAML response is attacker-supplied XML reaching an unauthenticated endpoint. Leaving DTDs on
        // would make this endpoint a file-read primitive.
        Assert.Throws<XmlException>(() => SamlAssertion.LoadSecurely(malicious));
    }

    // --- metadata ----------------------------------------------------------------------------

    [Fact]
    public void MetadataYieldsTheSigningCertificateAndTheSsoEndpoint()
    {
        using var certificate = NewCertificate();

        var (certificates, ssoUrl, entityId, error) = SamlAssertion.ParseMetadata(Metadata(certificate));

        Assert.Null(error);
        Assert.Single(certificates);
        Assert.Equal("https://idp.acme.com/sso", ssoUrl);
        Assert.Equal("https://idp.acme.com", entityId);
    }

    [Fact]
    public void AnEncryptionOnlyCertificateIsNotTreatedAsASigningKey()
    {
        using var certificate = NewCertificate();

        var (certificates, _, _, error) = SamlAssertion.ParseMetadata(Metadata(certificate, use: "encryption"));

        // Accepting it would widen the set of keys that can mint a valid assertion.
        Assert.Empty(certificates);
        Assert.NotNull(error);
    }

    [Fact]
    public void MetadataWithNoIdpDescriptorIsReported()
    {
        var (_, _, _, error) = SamlAssertion.ParseMetadata(
            """<EntityDescriptor xmlns="urn:oasis:names:tc:SAML:2.0:metadata" entityID="x"/>""");

        Assert.Contains("IDPSSODescriptor", error!);
    }

    [Fact]
    public void MalformedMetadataIsReportedRatherThanThrowing()
    {
        var (_, _, _, error) = SamlAssertion.ParseMetadata("<not xml");

        Assert.Contains("well-formed", error!);
    }

    // --- AuthnRequest ------------------------------------------------------------------------

    [Fact]
    public void TheAuthnRequestIsDeflatedBase64AndUrlEncodedPerTheRedirectBinding()
    {
        var (url, requestId) = SamlAssertion.BuildAuthnRequestUrl("https://idp.acme.com/sso",
            SpEntityId, Acs, "relay-state", Now);

        Assert.StartsWith("https://idp.acme.com/sso?SAMLRequest=", url);
        Assert.Contains("RelayState=relay-state", url);

        // An xsd:ID may not start with a digit, so the underscore prefix is not cosmetic.
        Assert.StartsWith("_", requestId);

        var encoded = Uri.UnescapeDataString(url.Split("SAMLRequest=")[1].Split('&')[0]);

        using var input = new System.IO.MemoryStream(Convert.FromBase64String(encoded));
        using var inflate = new System.IO.Compression.DeflateStream(input,
            System.IO.Compression.CompressionMode.Decompress);
        using var reader = new System.IO.StreamReader(inflate);

        var request = reader.ReadToEnd();

        Assert.Contains("AuthnRequest", request);
        Assert.Contains(requestId, request);
        Assert.Contains(Acs, request);
    }

    [Fact]
    public void AnSsoUrlThatAlreadyHasAQueryGetsAnAmpersand()
    {
        var (url, _) = SamlAssertion.BuildAuthnRequestUrl("https://idp.acme.com/sso?tenant=acme",
            SpEntityId, Acs, null, Now);

        Assert.Contains("?tenant=acme&SAMLRequest=", url);
    }
}

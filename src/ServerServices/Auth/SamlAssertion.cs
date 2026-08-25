using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using Model.Authentication.Federation;

namespace ServerServices.Auth;

/// <summary>
/// SAML 2.0 response parsing and validation in the service-provider role
/// (Track 4 milestone 4.3.1).
///
/// Written against <c>SignedXml</c> rather than by hand: XML signature verification depends on
/// canonicalization, and a hand-rolled comparison of element text is the classic SAML bypass. The
/// checks here are the ones whose absence has been a real-world vulnerability:
///
///  * the signature verifies against a certificate from the IdP's metadata — not against a
///    certificate embedded in the response, which an attacker controls;
///  * the signed element is the one being read, so an attacker cannot wrap a valid assertion around
///    an unsigned one (XML signature wrapping);
///  * conditions, audience and <c>InResponseTo</c> are enforced with a bounded clock skew.
/// </summary>
internal static class SamlAssertion
{
    private const string Protocol = "urn:oasis:names:tc:SAML:2.0:protocol";
    private const string Assertion = "urn:oasis:names:tc:SAML:2.0:assertion";

    internal record ValidationOutcome(bool Valid, string? Error, FederatedIdentity? Identity, string? InResponseTo);

    /// <summary>
    /// Loads a SAML response with entity resolution and DTD processing disabled.
    ///
    /// Not optional. A SAML response is attacker-supplied XML; leaving DTDs on is an XXE that reads
    /// server files, and leaving the resolver on is an SSRF.
    /// </summary>
    internal static XmlDocument LoadSecurely(string xml)
    {
        var document = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = false
        });

        document.Load(reader);
        return document;
    }

    /// <summary>
    /// Validates a base64 SAML response and extracts the identity it asserts.
    ///
    /// Returns a value rather than throwing because every failure mode here is something an
    /// administrator has to be told precisely — "the assertion was not signed" and "the audience did
    /// not match" have completely different fixes.
    /// </summary>
    internal static ValidationOutcome Validate(string base64Response, IReadOnlyList<X509Certificate2> idpCertificates,
        string? expectedAudience, string? expectedInResponseTo, ClaimMapping mapping,
        bool requireSignature, int clockSkewSeconds, DateTime nowUtc)
    {
        string xml;

        try
        {
            xml = Encoding.UTF8.GetString(Convert.FromBase64String(base64Response.Trim()));
        }
        catch (FormatException)
        {
            return new ValidationOutcome(false, "The SAML response was not valid base64.", null, null);
        }

        XmlDocument document;

        try
        {
            document = LoadSecurely(xml);
        }
        catch (XmlException ex)
        {
            return new ValidationOutcome(false, $"The SAML response was not well-formed XML: {ex.Message}",
                null, null);
        }

        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("p", Protocol);
        namespaces.AddNamespace("a", Assertion);
        namespaces.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

        var response = document.DocumentElement;

        if (response == null || response.LocalName != "Response" || response.NamespaceURI != Protocol)
            return new ValidationOutcome(false, "The document is not a SAML 2.0 Response.", null, null);

        var inResponseTo = response.GetAttribute("InResponseTo");

        // Checked before anything expensive: an unsolicited response is either a misconfiguration or
        // an injected assertion, and neither deserves further processing.
        if (!string.IsNullOrEmpty(expectedInResponseTo)
            && !string.Equals(inResponseTo, expectedInResponseTo, StringComparison.Ordinal))
            return new ValidationOutcome(false,
                "The SAML response does not answer the request NetRisk sent (InResponseTo mismatch).",
                null, inResponseTo);

        var statusCode = response.SelectSingleNode("p:Status/p:StatusCode", namespaces) as XmlElement;
        var status = statusCode?.GetAttribute("Value");

        if (status != null && !status.EndsWith(":Success", StringComparison.Ordinal))
        {
            var message = response.SelectSingleNode("p:Status/p:StatusMessage", namespaces)?.InnerText;
            return new ValidationOutcome(false,
                $"The identity provider refused the sign-in ({status}){(message == null ? "" : $": {message}")}",
                null, inResponseTo);
        }

        var assertion = response.SelectSingleNode("a:Assertion", namespaces) as XmlElement;

        if (assertion == null)
            return new ValidationOutcome(false,
                "The SAML response carries no assertion. An encrypted assertion (EncryptedAssertion) is "
                + "not supported; configure the IdP to sign rather than encrypt.", null, inResponseTo);

        if (requireSignature)
        {
            // Either the response or the assertion may carry the signature; what matters is that the
            // element whose contents are read is inside a verified signature.
            var signedAssertion = VerifySignedElement(document, assertion, idpCertificates, namespaces);
            var signedResponse = VerifySignedElement(document, response, idpCertificates, namespaces);

            if (!signedAssertion && !signedResponse)
                return new ValidationOutcome(false,
                    "The SAML assertion signature could not be verified against the identity provider's "
                    + "metadata certificate.", null, inResponseTo);
        }

        var conditions = assertion.SelectSingleNode("a:Conditions", namespaces) as XmlElement;
        var skew = TimeSpan.FromSeconds(Math.Clamp(clockSkewSeconds, 0, 3600));

        if (conditions != null)
        {
            var notBefore = ParseInstant(conditions.GetAttribute("NotBefore"));
            var notOnOrAfter = ParseInstant(conditions.GetAttribute("NotOnOrAfter"));

            if (notBefore != null && nowUtc + skew < notBefore)
                return new ValidationOutcome(false,
                    $"The assertion is not valid until {notBefore:O}; the server clock may be wrong.",
                    null, inResponseTo);

            if (notOnOrAfter != null && nowUtc - skew >= notOnOrAfter)
                return new ValidationOutcome(false,
                    $"The assertion expired at {notOnOrAfter:O}.", null, inResponseTo);

            if (!string.IsNullOrEmpty(expectedAudience))
            {
                var audiences = conditions
                    .SelectNodes("a:AudienceRestriction/a:Audience", namespaces)?
                    .Cast<XmlNode>().Select(n => n.InnerText.Trim()).ToList() ?? [];

                // An assertion minted for another service provider must not be accepted here; that is
                // the whole purpose of the audience restriction.
                if (audiences.Count > 0
                    && !audiences.Any(a => string.Equals(a, expectedAudience, StringComparison.Ordinal)))
                    return new ValidationOutcome(false,
                        $"The assertion's audience ({string.Join(", ", audiences)}) is not this service "
                        + $"provider ({expectedAudience}).", null, inResponseTo);
            }
        }

        var identity = ExtractIdentity(assertion, namespaces, mapping);

        if (string.IsNullOrEmpty(identity.Subject))
            return new ValidationOutcome(false,
                "The assertion carries no NameID and no subject attribute, so there is no account to sign in.",
                identity, inResponseTo);

        return new ValidationOutcome(true, null, identity, inResponseTo);
    }

    /// <summary>
    /// Verifies that <paramref name="element"/> is covered by a signature that checks out against one
    /// of the IdP's certificates.
    ///
    /// The reference URI is compared to the element's own id: a signature that validates but covers a
    /// *different* element is the signature-wrapping attack, and <c>CheckSignature</c> alone does not
    /// catch it.
    /// </summary>
    private static bool VerifySignedElement(XmlDocument document, XmlElement element,
        IReadOnlyList<X509Certificate2> certificates, XmlNamespaceManager namespaces)
    {
        var signature = element.SelectSingleNode("ds:Signature", namespaces) as XmlElement;
        if (signature == null) return false;

        var id = element.GetAttribute("ID");
        if (string.IsNullOrEmpty(id)) return false;

        var signedXml = new SignedXml(document);

        try
        {
            signedXml.LoadXml(signature);
        }
        catch (CryptographicException)
        {
            return false;
        }

        if (signedXml.SignedInfo?.References.Count != 1) return false;

        var reference = (Reference)signedXml.SignedInfo.References[0]!;

        // "" would mean the whole document, which no SAML IdP produces and which would make the
        // reference check meaningless.
        if (reference.Uri != "#" + id) return false;

        // Certificates from the metadata only. A KeyInfo certificate inside the response is chosen by
        // whoever sent it, so trusting it would make every signature self-validating.
        return certificates.Any(certificate =>
        {
            using var key = certificate.GetRSAPublicKey();
            if (key == null) return false;

            try
            {
                return signedXml.CheckSignature(key);
            }
            catch (CryptographicException)
            {
                return false;
            }
        });
    }

    private static FederatedIdentity ExtractIdentity(XmlElement assertion, XmlNamespaceManager namespaces,
        ClaimMapping mapping)
    {
        var identity = new FederatedIdentity();

        var nameId = assertion.SelectSingleNode("a:Subject/a:NameID", namespaces)?.InnerText?.Trim();

        foreach (var node in assertion.SelectNodes("a:AttributeStatement/a:Attribute", namespaces)?
                                .Cast<XmlElement>() ?? [])
        {
            var name = node.GetAttribute("Name");
            if (string.IsNullOrEmpty(name)) continue;

            var values = node.SelectNodes("a:AttributeValue", namespaces)?
                             .Cast<XmlNode>().Select(v => v.InnerText.Trim())
                             .Where(v => v.Length > 0).ToList() ?? [];

            if (values.Count == 0) continue;

            // Multi-valued attributes are joined for the diagnostic dump and read individually below;
            // groups are the only attribute where multiplicity carries meaning.
            identity.Claims[name] = string.Join(", ", values);

            // Also indexed by the short form of a URI-style attribute name, because operators configure
            // "emailaddress" far more often than the full schemas.xmlsoap.org URI.
            var shortName = ShortNameOf(name);
            if (shortName != name && !identity.Claims.ContainsKey(shortName))
                identity.Claims[shortName] = identity.Claims[name];

            if (Matches(name, mapping.Email)) identity.Email = values[0];
            if (Matches(name, mapping.Name)) identity.Name = values[0];
            if (mapping.Login != null && Matches(name, mapping.Login)) identity.Login = values[0];
            if (Matches(name, mapping.Subject)) identity.Subject = values[0];
            if (Matches(name, mapping.Groups)) identity.Groups.AddRange(values);
        }

        // NameID is the fallback subject and, for the very common case of an emailAddress NameID
        // format, the fallback email as well.
        if (string.IsNullOrEmpty(identity.Subject)) identity.Subject = nameId ?? string.Empty;
        if (string.IsNullOrEmpty(identity.Email) && nameId?.Contains('@') == true) identity.Email = nameId;

        return identity;
    }

    private static bool Matches(string attributeName, string configured) =>
        string.Equals(attributeName, configured, StringComparison.OrdinalIgnoreCase)
        || string.Equals(ShortNameOf(attributeName), configured, StringComparison.OrdinalIgnoreCase);

    private static string ShortNameOf(string name)
    {
        var slash = name.LastIndexOf('/');
        return slash >= 0 && slash < name.Length - 1 ? name[(slash + 1)..] : name;
    }

    private static DateTime? ParseInstant(string? value) =>
        DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Reads the signing certificates out of IdP metadata, along with the SSO endpoint and entity id.
    ///
    /// Certificates with <c>use="encryption"</c> are skipped: signing with an encryption key is not a
    /// thing IdPs do, and accepting one widens the set of keys that can mint a valid assertion.
    /// </summary>
    internal static (List<X509Certificate2> Certificates, string? SsoUrl, string? EntityId, string? Error)
        ParseMetadata(string metadataXml)
    {
        var certificates = new List<X509Certificate2>();

        XmlDocument document;

        try
        {
            document = LoadSecurely(metadataXml);
        }
        catch (XmlException ex)
        {
            return (certificates, null, null, $"The metadata was not well-formed XML: {ex.Message}");
        }

        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("md", "urn:oasis:names:tc:SAML:2.0:metadata");
        namespaces.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

        var descriptor = document.SelectSingleNode("//md:IDPSSODescriptor", namespaces) as XmlElement;

        if (descriptor == null)
            return (certificates, null, null,
                "The metadata contains no IDPSSODescriptor, so it is not identity-provider metadata.");

        var entityId = (descriptor.ParentNode as XmlElement)?.GetAttribute("entityID");

        foreach (var keyDescriptor in descriptor.SelectNodes("md:KeyDescriptor", namespaces)?
                                          .Cast<XmlElement>() ?? [])
        {
            var use = keyDescriptor.GetAttribute("use");
            if (string.Equals(use, "encryption", StringComparison.OrdinalIgnoreCase)) continue;

            var raw = keyDescriptor.SelectSingleNode("ds:KeyInfo/ds:X509Data/ds:X509Certificate", namespaces)
                ?.InnerText;

            if (string.IsNullOrWhiteSpace(raw)) continue;

            try
            {
                certificates.Add(X509CertificateLoader.LoadCertificate(
                    Convert.FromBase64String(raw.Replace("\r", "").Replace("\n", "").Trim())));
            }
            catch (Exception)
            {
                // One unreadable certificate should not discard the others.
            }
        }

        string? ssoUrl = null;

        foreach (var service in descriptor.SelectNodes("md:SingleSignOnService", namespaces)?
                                    .Cast<XmlElement>() ?? [])
        {
            // HTTP-Redirect is the binding the AuthnRequest builder produces, so it is preferred; POST
            // is accepted as a fallback so a redirect-less IdP is still configurable.
            var binding = service.GetAttribute("Binding");

            if (binding.EndsWith("HTTP-Redirect", StringComparison.Ordinal))
            {
                ssoUrl = service.GetAttribute("Location");
                break;
            }

            ssoUrl ??= service.GetAttribute("Location");
        }

        if (certificates.Count == 0)
            return (certificates, ssoUrl, entityId,
                "The metadata carries no signing certificate, so assertions could not be verified.");

        return (certificates, ssoUrl, entityId, null);
    }

    /// <summary>
    /// Builds a SAML 2.0 <c>AuthnRequest</c> for the HTTP-Redirect binding: deflate-compressed,
    /// base64, URL-encoded, exactly as the binding specifies.
    /// </summary>
    internal static (string Url, string RequestId) BuildAuthnRequestUrl(string ssoUrl, string spEntityId,
        string acsUrl, string? relayState, DateTime nowUtc)
    {
        // An xsd:ID may not start with a digit, so the conventional underscore prefix is not cosmetic.
        var requestId = "_" + Guid.NewGuid().ToString("N");

        var request =
            $"""<samlp:AuthnRequest xmlns:samlp="{Protocol}" xmlns:saml="{Assertion}" ID="{requestId}" Version="2.0" IssueInstant="{nowUtc:yyyy-MM-ddTHH:mm:ssZ}" Destination="{Escape(ssoUrl)}" ProtocolBinding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" AssertionConsumerServiceURL="{Escape(acsUrl)}"><saml:Issuer>{Escape(spEntityId)}</saml:Issuer><samlp:NameIDPolicy Format="urn:oasis:names:tc:SAML:2.0:nameid-format:emailAddress" AllowCreate="true"/></samlp:AuthnRequest>""";

        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(output,
                   System.IO.Compression.CompressionMode.Compress, true))
        {
            var bytes = Encoding.UTF8.GetBytes(request);
            deflate.Write(bytes, 0, bytes.Length);
        }

        var encoded = Uri.EscapeDataString(Convert.ToBase64String(output.ToArray()));

        var separator = ssoUrl.Contains('?') ? "&" : "?";
        var url = $"{ssoUrl}{separator}SAMLRequest={encoded}";

        if (!string.IsNullOrEmpty(relayState)) url += "&RelayState=" + Uri.EscapeDataString(relayState);

        return (url, requestId);
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

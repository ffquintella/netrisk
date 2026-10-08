using System;
using System.Linq;
using System.Text;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.ThirdParties;
using Tools.ThirdParties;
using Xunit;

namespace Tools.Tests.ThirdParties;

/// <summary>
/// Stage 9.10 (S51 §4.6, D9, §8 S1–S11) — the SBOM parser, written for hostile input: CycloneDX and SPDX JSON only, size,
/// depth and component caps refused rather than truncated, control characters stripped, column widths enforced, and a
/// file name reduced to metadata that can never name a path.
/// </summary>
[TestSubject(typeof(SbomParser))]
public class SbomParserTest
{
    private const string CycloneDx = """
        {
          "bomFormat": "CycloneDX",
          "specVersion": "1.5",
          "serialNumber": "urn:uuid:3e671687-395b-41f5-a30f-a58921a69b79",
          "metadata": { "component": { "name": "Moodle LMS", "version": "4.3.2" } },
          "components": [
            {
              "name": "log4j-core", "version": "2.17.1", "purl": "pkg:maven/org.apache.logging.log4j/log4j-core@2.17.1",
              "licenses": [ { "license": { "id": "Apache-2.0" } } ],
              "components": [ { "name": "log4j-api", "version": "2.17.1", "licenses": [ { "expression": "Apache-2.0 OR MIT" } ] } ]
            },
            { "name": "openssl", "version": "3.0.13", "licenses": [ { "license": { "name": "OpenSSL License" } } ] },
            { "name": "openssl", "version": "3.0.13" }
          ]
        }
        """;

    private const string Spdx = """
        {
          "spdxVersion": "SPDX-2.3",
          "name": "Canvas document",
          "documentNamespace": "https://vendor.example/spdx/canvas-2026.1",
          "documentDescribes": [ "SPDXRef-canvas" ],
          "packages": [
            { "SPDXID": "SPDXRef-canvas", "name": "Canvas", "versionInfo": "2026.1", "licenseConcluded": "NOASSERTION", "licenseDeclared": "AGPL-3.0" },
            { "SPDXID": "SPDXRef-rails", "name": "rails", "versionInfo": "7.1.3",
              "externalRefs": [ { "referenceCategory": "PACKAGE-MANAGER", "referenceType": "purl", "referenceLocator": "pkg:gem/rails@7.1.3" } ],
              "licenseConcluded": "MIT" }
          ]
        }
        """;

    private static InvalidParameterException Refused(string? document) =>
        Assert.Throws<InvalidParameterException>(() => SbomParser.Parse(document));

    /// <summary>S1 — CycloneDX: nested components flattened, licenses joined, the described product, hash and size.</summary>
    [Fact]
    public void TestS1_CycloneDxIsReadWhole()
    {
        var result = SbomParser.Parse(CycloneDx);

        Assert.Equal(SbomFormat.CycloneDxJson, result.Format);
        Assert.Equal(("1.5", "urn:uuid:3e671687-395b-41f5-a30f-a58921a69b79"), (result.SpecVersion, result.SerialNumber));
        Assert.Equal(("Moodle LMS", "4.3.2"), (result.DescribedName, result.DescribedVersion));

        Assert.Equal(new[] { "log4j-core", "openssl", "log4j-api" }, result.Components.Select(c => c.Name));
        var core = result.Components[0];
        Assert.Equal(("2.17.1", "pkg:maven/org.apache.logging.log4j/log4j-core@2.17.1", "Apache-2.0"),
            (core.Version, core.Purl, core.License));
        Assert.Equal("OpenSSL License", result.Components[1].License);
        Assert.Equal("Apache-2.0 OR MIT", result.Components[2].License);

        Assert.Matches("^[0-9a-f]{64}$", result.Sha256);
        Assert.Equal(Encoding.UTF8.GetByteCount(CycloneDx), result.SizeBytes);
        Assert.Equal(result.Sha256, SbomParser.Parse(CycloneDx).Sha256);
    }

    /// <summary>S2 — SPDX: versions, the purl reference, NOASSERTION falling back to the declared license, the described package.</summary>
    [Fact]
    public void TestS2_SpdxIsReadWhole()
    {
        var result = SbomParser.Parse(Spdx);

        Assert.Equal(SbomFormat.SpdxJson, result.Format);
        Assert.Equal(("SPDX-2.3", "https://vendor.example/spdx/canvas-2026.1"), (result.SpecVersion, result.SerialNumber));
        Assert.Equal(("Canvas", "2026.1"), (result.DescribedName, result.DescribedVersion));
        Assert.Equal(2, result.Components.Count);
        Assert.Equal("AGPL-3.0", result.Components[0].License);
        Assert.Equal(("rails", "7.1.3", "pkg:gem/rails@7.1.3", "MIT"),
            (result.Components[1].Name, result.Components[1].Version, result.Components[1].Purl, result.Components[1].License));
    }

    /// <summary>S3 — larger than 5 MiB of UTF-8 is refused before parsing — measured in bytes, not characters.</summary>
    [Fact]
    public void TestS3_AnOversizedDocumentIsRefused()
    {
        const string head = "{\"bomFormat\":\"CycloneDX\",\"x\":\"";
        const string tail = "\"}";
        var fill = ThirdPartyLimits.MaxSbomDocumentBytes - head.Length - tail.Length;

        Assert.Equal(ThirdPartyLimits.MaxSbomDocumentBytes, SbomParser.Parse(head + new string('a', fill) + tail).SizeBytes);
        Assert.Contains("bytes", Refused(head + new string('a', fill + 1) + tail).Message);

        // Two bytes per 'é': half as many characters already exceed the cap.
        Assert.Contains("bytes", Refused(head + new string('é', fill / 2 + 1) + tail).Message);
    }

    /// <summary>S4 — XML is refused unparsed (no XXE, no entity expansion), whatever it claims to be.</summary>
    [Theory]
    [InlineData("<?xml version=\"1.0\"?><bom xmlns=\"http://cyclonedx.org/schema/bom/1.5\"/>")]
    [InlineData("   <!DOCTYPE x [<!ENTITY a \"aaaa\">]><bom>&a;</bom>")]
    public void TestS4_XmlIsRefused(string document) => Assert.Contains("XML", Refused(document).Message);

    /// <summary>S5 — empty, not JSON, not an object, or neither format: refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("{ not json")]
    [InlineData("[ { \"bomFormat\": \"CycloneDX\" } ]")]
    [InlineData("{ \"bomFormat\": \"SWID\" }")]
    [InlineData("{ \"spdxVersion\": \"2.3\" }")]
    [InlineData("{ \"name\": \"no format at all\" }")]
    [InlineData("{ \"bomFormat\": \"CycloneDX\", } ")]
    [InlineData("{ /* comment */ \"bomFormat\": \"CycloneDX\" }")]
    public void TestS5_AMalformedDocumentIsRefused(string? document) => Refused(document);

    /// <summary>S6 — nesting past 64 levels is refused by the reader, before any walk.</summary>
    [Fact]
    public void TestS6_DeepNestingIsRefused()
    {
        string Nested(int depth) => "{\"bomFormat\":\"CycloneDX\",\"x\":" + new string('[', depth) + new string(']', depth) + "}";

        Assert.Empty(SbomParser.Parse(Nested(ThirdPartyLimits.MaxSbomJsonDepth - 2)).Components);
        Assert.Contains("deeper", Refused(Nested(200)).Message);
    }

    /// <summary>S7 — ten thousand components pass; one more — counting nested ones — is refused, never truncated.</summary>
    [Fact]
    public void TestS7_TheComponentCapIsRefusedNotTruncated()
    {
        string Bom(int top, int nested) =>
            "{\"bomFormat\":\"CycloneDX\",\"components\":[" +
            string.Join(",", Enumerable.Range(0, top).Select(i =>
                i == 0 && nested > 0
                    ? "{\"name\":\"c0\",\"components\":[" +
                      string.Join(",", Enumerable.Range(0, nested).Select(j => $"{{\"name\":\"n{j}\"}}")) + "]}"
                    : $"{{\"name\":\"c{i}\"}}")) +
            "]}";

        Assert.Equal(ThirdPartyLimits.MaxSbomComponents, SbomParser.Parse(Bom(ThirdPartyLimits.MaxSbomComponents, 0)).Components.Count);
        Assert.Contains("more than", Refused(Bom(ThirdPartyLimits.MaxSbomComponents + 1, 0)).Message);
        Assert.Contains("more than", Refused(Bom(ThirdPartyLimits.MaxSbomComponents, 1)).Message);
    }

    /// <summary>S8 — a nameless component and a wrongly typed field are refused, naming where.</summary>
    [Theory]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"components\":[{\"version\":\"1\"}]}", "components[0] has no name")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"components\":[{\"name\":\"a\",\"components\":[{\"name\":\"  \"}]}]}", "components[0].components[0] has no name")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"components\":[{\"name\":42}]}", "components[0].name must be a string")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"components\":{\"name\":\"a\"}}", "components must be an array")]
    [InlineData("{\"bomFormat\":\"CycloneDX\",\"components\":[\"a\"]}", "components[0] is not an object")]
    [InlineData("{\"spdxVersion\":\"SPDX-2.3\",\"packages\":[{\"SPDXID\":\"x\"}]}", "packages[0] has no name")]
    public void TestS8_AMalformedComponentIsRefusedWithItsPosition(string document, string message) =>
        Assert.Contains(message, Refused(document).Message);

    /// <summary>S9 — control characters stripped, strings cut to their column, a surrogate pair never split.</summary>
    [Fact]
    public void TestS9_StringsAreCleanedAndCut()
    {
        var longName = new string('x', ThirdPartyLimits.MaxStoredComponentNameLength + 50);
        var document = "{\"bomFormat\":\"CycloneDX\",\"components\":[" +
                       "{\"name\":\"evil\\u0000\\u001b[31mname\\r\\n\"}," +
                       $"{{\"name\":\"{longName}\"}}]}}";

        var result = SbomParser.Parse(document);

        Assert.Equal("evil[31mname", result.Components[0].Name);
        Assert.Equal(ThirdPartyLimits.MaxStoredComponentNameLength, result.Components[1].Name.Length);

        var emoji = new string('a', 9) + "😀";
        Assert.Equal(new string('a', 9), SbomParser.Clean(emoji, 10));
        Assert.Null(SbomParser.Clean("\u0001\u0002 ", 10));
    }

    /// <summary>S10 — the same component listed twice is stored once.</summary>
    [Fact]
    public void TestS10_DuplicatesAreStoredOnce() =>
        Assert.Single(SbomParser.Parse(CycloneDx).Components, c => c.Name == "openssl");

    /// <summary>S11 — the file name is the last segment, cleaned; a traversal or a dot is not a name.</summary>
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\sbom.json", "sbom.json")]
    [InlineData("C:\\temp\\vendor sbom.json", "vendor sbom.json")]
    [InlineData("a\u0000b.json", "ab.json")]
    [InlineData("..", null)]
    [InlineData("../", null)]
    [InlineData("sbom/.", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void TestS11_TheFileNameIsMetadataOnly(string? given, string? stored)
    {
        Assert.Equal(stored, SbomParser.SafeFileName(given));
        if (stored is not null) Assert.DoesNotContain('/', stored);
    }

    /// <summary>S11b — an over-long file name is cut to its column.</summary>
    [Fact]
    public void TestS11b_ALongFileNameIsCut() =>
        Assert.Equal(ThirdPartyLimits.MaxSbomFileNameLength, SbomParser.SafeFileName(new string('f', 400) + ".json")!.Length);
}

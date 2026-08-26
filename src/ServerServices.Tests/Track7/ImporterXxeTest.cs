using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Contracts.Importers;
using JetBrains.Annotations;
using ServerServices.Importers;
using Xunit;

namespace ServerServices.Tests.Track7;

/// <summary>
/// Track 7 milestone 7.1.2 — the file-import attack surface, verified rather than assumed.
///
/// A scan report is the most untrusted input NetRisk accepts: it arrives as XML, from a tool, and
/// nobody reads it before uploading. The importers set <c>DtdProcessing = Prohibit</c> and
/// <c>XmlResolver = null</c>, and the comments beside those lines say why — but a comment is not
/// evidence, and this whole track exists because of controls that were documented and absent. These
/// tests are the evidence.
///
/// Three payloads, covering the three things a hostile XML file tries:
///  * an external entity pointing at a local file (classic XXE, file disclosure);
///  * an external entity pointing at a URL (SSRF through the parser);
///  * nested internal entities (the "billion laughs" expansion denial of service).
///
/// Each one must be refused as a parse failure — not parsed with the entity dropped, which would
/// still mean the DTD was processed.
/// </summary>
public class ImporterXxeTest
{
    private const string TargetFileMarker = "netrisk-xxe-canary";

    /// <summary>External-entity file read, wrapped in a document each importer would otherwise accept.</summary>
    private static string ExternalFileEntity(string root, string body, string targetFile) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <!DOCTYPE {root} [ <!ENTITY xxe SYSTEM "file://{targetFile}"> ]>
         <{root}>{body}</{root}>
         """;

    /// <summary>External-entity fetch of a URL — SSRF via the parser.</summary>
    private static string ExternalHttpEntity(string root, string body) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <!DOCTYPE {root} [ <!ENTITY xxe SYSTEM "http://169.254.169.254/latest/meta-data/"> ]>
         <{root}>{body}</{root}>
         """;

    /// <summary>Nested internal entities. No external access at all, so a resolver check misses it.</summary>
    private static string BillionLaughs(string root, string body) =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <!DOCTYPE {root} [
           <!ENTITY a "aaaaaaaaaa">
           <!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">
           <!ENTITY c "&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;">
           <!ENTITY d "&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;">
         ]>
         <{root}>{body}</{root}>
         """;

    private static Stream Stream(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));

    private static ImportContext Context() => new()
    {
        FileName = "hostile.xml",
        IgnoreNegligible = true,
        UserId = 1,
        ImportedAt = new DateTime(2026, 8, 26, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>
    /// A file on disk the payload tries to read, so a successful exfiltration would be observable
    /// rather than merely "the parse succeeded".
    /// </summary>
    private static string WriteCanary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netrisk-xxe-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, TargetFileMarker);
        return path;
    }

    private static async Task<Exception?> TryImportAsync(IVulnerabilityReportImporter importer, string payload)
    {
        try
        {
            await using var stream = Stream(payload);
            await importer.ImportAsync(stream, Context(), CancellationToken.None);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>The importers and the document element each one expects.</summary>
    public static TheoryData<string> Importers() => new("nessus", "openvas", "burp");

    private static IVulnerabilityReportImporter Importer(string name) => name switch
    {
        "nessus" => new NessusReportImporter(),
        "openvas" => new OpenVasImporter(),
        "burp" => new BurpImporter(),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static (string Root, string Body) Document(string name) => name switch
    {
        "nessus" => ("NessusClientData_v2", "<Report name=\"r\"><ReportHost name=\"h\">&xxe;</ReportHost></Report>"),
        "openvas" => ("report", "<results><result><name>&xxe;</name></result></results>"),
        "burp" => ("issues", "<issue><name>&xxe;</name></issue>"),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    /// <summary>The regression assertion for the DTD prohibition, per importer.</summary>
    [Theory]
    [MemberData(nameof(Importers))]
    public async Task AnExternalFileEntityIsRefused(string importerName)
    {
        var canary = WriteCanary();

        try
        {
            var (root, body) = Document(importerName);
            var thrown = await TryImportAsync(Importer(importerName), ExternalFileEntity(root, body, canary));

            Assert.NotNull(thrown);
            // The parser refused the DTD outright, rather than resolving it and silently succeeding.
            Assert.Contains("DTD", ThrownText(thrown!), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TargetFileMarker, ThrownText(thrown!));
        }
        finally
        {
            File.Delete(canary);
        }
    }

    [Theory]
    [MemberData(nameof(Importers))]
    public async Task AnExternalHttpEntityIsRefused(string importerName)
    {
        var (root, body) = Document(importerName);

        var thrown = await TryImportAsync(Importer(importerName), ExternalHttpEntity(root, body));

        Assert.NotNull(thrown);
        Assert.Contains("DTD", ThrownText(thrown!), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The one a resolver check alone would miss: no external access, just exponential expansion of
    /// internal entities.
    /// </summary>
    [Theory]
    [MemberData(nameof(Importers))]
    public async Task NestedInternalEntitiesAreRefused(string importerName)
    {
        var (root, body) = Document(importerName);

        var thrown = await TryImportAsync(Importer(importerName), BillionLaughs(root, body));

        Assert.NotNull(thrown);
        Assert.Contains("DTD", ThrownText(thrown!), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The XML reader settings the hardened legacy path uses, asserted directly. The legacy
    /// <c>NessusImporter</c> is not reachable from a controller today (nothing calls
    /// <c>GetImporter</c>), so its parse cannot be exercised end to end here without a database and a
    /// job manager — but the settings it now applies are the point, and they are the same three the
    /// contract importers use.
    /// </summary>
    [Fact]
    public void TheHardenedReaderSettingsProhibitDtdsAndEntities()
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0
        };

        using var stream = Stream(ExternalFileEntity("NessusClientData_v2", "<Report/>", "/etc/hosts"));

        var thrown = Assert.ThrowsAny<XmlException>(() =>
        {
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read()) { }
        });

        Assert.Contains("DTD", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Flattens the exception chain, since the importers wrap parse failures.</summary>
    private static string ThrownText(Exception exception)
    {
        var text = new StringBuilder();
        for (Exception? current = exception; current != null; current = current.InnerException)
            text.Append(current.Message).Append(" | ");

        return text.ToString();
    }
}

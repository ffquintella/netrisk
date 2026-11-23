using System.Collections.Generic;
using System.IO;
using Xunit;
using ClientServices.Services.Importers;

namespace ClientServices.Tests.Services;

/// <summary>
/// The SecureScoreCard importer reads its export with CsvHelper, whose 34.x rewrite removed
/// CsvConfiguration. These pin the behaviour the importer relies on: header normalisation,
/// comma separation, quoted fields and columns missing from the file.
/// </summary>
public class SecureScoreCardCsvTest
{
    private static List<SSCVulnerability> Read(string csv) => SecureScoreCardImporter.ReadRecords(new StringReader(csv));

    [Fact]
    public void ReadRecords_MatchesHeadersIgnoringCaseAndPunctuation()
    {
        var records = Read("ISSUE ID,Issue Type Severity,Ip Addresses,CVE\n" +
                           "abc-1,POSITIVE,10.0.0.1,CVE-2024-0001\n");

        var record = Assert.Single(records);
        Assert.Equal("abc-1", record.IssueId);
        Assert.Equal("POSITIVE", record.IssueTypeSeverity);
        Assert.Equal("10.0.0.1", record.IpAddresses);
        Assert.Equal("CVE-2024-0001", record.Cve);
    }

    [Fact]
    public void ReadRecords_KeepsCommasAndLineBreaksInsideQuotedFields()
    {
        var records = Read("Issue Id,Description\n" +
                           "1,\"Weak cipher, TLS 1.0\nenabled\"\n" +
                           "2,plain\n");

        Assert.Equal(2, records.Count);
        Assert.Equal("Weak cipher, TLS 1.0\nenabled", records[0].Description);
        Assert.Equal("plain", records[1].Description);
    }

    [Fact]
    public void ReadRecords_LeavesColumnsMissingFromTheFileAtTheirDefault()
    {
        var records = Read("Issue Id\n1\n");

        var record = Assert.Single(records);
        Assert.Equal("1", record.IssueId);
        Assert.Equal("", record.Hostname);
    }

    [Fact]
    public void ReadRecords_DoesNotDetectASemicolonAsTheSeparator()
    {
        var records = Read("Issue Id,Description\n1,a;b;c\n");

        Assert.Equal("a;b;c", Assert.Single(records).Description);
    }
}

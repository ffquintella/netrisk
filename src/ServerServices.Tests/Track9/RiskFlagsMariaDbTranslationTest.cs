using System;
using System.Linq;
using DAL.Context;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.Governance;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// The flag 3 read of the Gate A derivation, translated for MariaDB with the real (Microting MySQL) provider — no
/// connection is opened.
///
/// Regression: the read projected the outer risk's id inside the <c>SelectMany</c> collection selector, which EF can only
/// translate as CROSS APPLY, and MariaDB has no LATERAL join. Translation threw <see cref="InvalidOperationException"/>
/// before any SQL ran, so on the production database every Gate A check — and with it "Archive risk", which answered
/// 500 ("Error calling /RiskArchive/Risks/337") — failed, while every in-memory test passed. The same read on a real
/// MariaDB, through the archive, is <c>DAL.IntegrationTests/Track9RiskArchiveWriteTests</c> (needs Docker).
/// </summary>
[TestSubject(typeof(RiskFlagsService))]
public class RiskFlagsMariaDbTranslationTest
{
    private static AuditableContext MariaDbContext() => new(new DbContextOptionsBuilder<NRDbContext>()
        .UseMySql("server=localhost;database=none;uid=x;pwd=x", ServerVersion.Parse("10.11.0-mariadb"))
        .Options);

    /// <summary>One risk (a Gate A check) and every open risk (the daily sweep): both reads translate, as a join.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestT1_TheLinkedFindingsReadTranslatesForMariaDbWithoutALateralJoin(bool oneRisk)
    {
        using var db = MariaDbContext();
        var risks = oneRisk
            ? db.Risks.AsNoTracking().Where(r => r.Id == 337)
            : db.Risks.AsNoTracking().Where(r => r.Status != "Closed");

        var sql = RiskFlagsService.LinkedOpenFindings(risks).ToQueryString();

        Assert.DoesNotContain("LATERAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM `risks_to_vulnerabilities`", sql, StringComparison.Ordinal);
        Assert.Contains("ON `r`.`id` = `s`.`risk_id`", sql, StringComparison.Ordinal);
        Assert.Contains("`status_id` IN (1, 2)", sql, StringComparison.Ordinal);
    }
}

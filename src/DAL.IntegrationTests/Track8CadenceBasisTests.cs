using System;
using System.Threading.Tasks;
using DAL.Context;
using MySqlConnector;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// The review-cadence basis (Track 8 milestone 8.2.2) end to end over the real lookup tables.
///
/// This exists because the in-memory suite cannot reach it: <c>risk_levels</c> and <c>review_levels</c>
/// are keyless entities (<c>HasNoKey</c>) and the EF in-memory provider refuses to track them, so
/// <c>ServerServices.Tests.Track8.ResidualAndQuantitativeInMemoryTest</c> asserts on the pure
/// <c>SelectCadenceScore</c> helper and points here for the rest of the path. The part only a real
/// database proves is the join from a score to a band to an interval: <c>risk_levels.display_name</c>
/// matched against <c>review_levels.name</c>, over the values Data/1.sql actually seeds.
///
/// The behaviour under test is the one <c>next_review_date_uses</c> was re-created for in version 80.
/// The setting shipped in version 1, was deleted in version 29, and never selected anything while it
/// existed.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track8CadenceBasisTests(MariaDbContainerFixture fixture)
{
    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) =>
            f.NewContext();

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private MgmtReviewsService NewService() =>
        new(Substitute.For<Serilog.ILogger>(), new ContainerDal(fixture));

    /// <summary>
    /// One risk scored 8.0 inherent / 2.0 residual. Against the seeded bands (Low 0, Medium 4,
    /// High 7, Very High 10.1) that is a High inherent risk and a Low residual one, and the seeded
    /// review intervals for those bands are 90 and 240 days — so the two bases are distinguishable
    /// rather than coincidentally equal.
    /// </summary>
    private async Task SeedOneTreatedRiskAsync(string basis, DateTime submittedUtc)
    {
        await fixture.InitializeNumberedSchemaAsync(82);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`," +
            "`role_id`,`admin`,`login`) VALUES (910,1,0,'local','Cadence','cad@x.test','s'," +
            "REPEAT('x',60),1,1,'cadence');");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`," +
            "`submission_date`,`last_update`,`risk_catalog_mapping`,`threat_catalog_mapping`," +
            "`template_group_id`,`submitted_by`) VALUES (910,'New','Treated risk','R-910','',''," +
            $"'{submittedUtc:yyyy-MM-dd HH:mm:ss}','{submittedUtc:yyyy-MM-dd HH:mm:ss}','','',1,910);");

        await MariaDbContainerFixture.ExecAsync(conn,
            "INSERT INTO `risk_scoring` (`id`,`scoring_method`,`calculated_risk`,`residual_risk`) " +
            "VALUES (910,1,8.0,2.0);");

        await MariaDbContainerFixture.ExecAsync(conn,
            $"UPDATE `settings` SET `value` = '{basis}' WHERE `name` = 'next_review_date_uses';");
    }

    [Fact]
    public async Task TheInherentBasisResolvesTheBandOfTheUntreatedScore()
    {
        await SeedOneTreatedRiskAsync("InherentRisk", new DateTime(2026, 1, 1, 0, 0, 0));

        var level = await NewService().GetRiskReviewLevelAsync(910);

        Assert.Equal("High", level.Name);
        Assert.Equal(90, level.Value);
    }

    [Fact]
    public async Task TheResidualBasisResolvesTheBandOfThePostTreatmentScore()
    {
        await SeedOneTreatedRiskAsync("ResidualRisk", new DateTime(2026, 1, 1, 0, 0, 0));

        var level = await NewService().GetRiskReviewLevelAsync(910);

        // The whole point of the setting: the same risk, reviewed three times less often once the
        // controls it already has are credited.
        Assert.Equal("Low", level.Name);
        Assert.Equal(240, level.Value);
    }

    /// <summary>
    /// An unrecognised value is inherent, not an error. The setting is user-editable and an
    /// installation that types "residual" into it should get the conservative cadence rather than a
    /// crash in the morning notification job.
    /// </summary>
    [Fact]
    public async Task AnUnrecognisedBasisFallsBackToInherent()
    {
        await SeedOneTreatedRiskAsync("residual", new DateTime(2026, 1, 1, 0, 0, 0));

        Assert.Equal("High", (await NewService().GetRiskReviewLevelAsync(910)).Name);
    }

    /// <summary>
    /// A risk with no residual score yet — every risk in an existing installation, the morning after
    /// the upgrade — must not silently score 0 and land in the Low band. It falls back to inherent.
    /// </summary>
    [Fact]
    public async Task ARiskWithNoResidualScoreYetIsBandedByItsInherentScore()
    {
        await fixture.InitializeNumberedSchemaAsync(82);

        await using (var conn = new MySqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();

            await MariaDbContainerFixture.ExecAsync(conn,
                "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`," +
                "`password`,`role_id`,`admin`,`login`) VALUES (911,1,0,'local','Cad2','cad2@x.test'," +
                "'s',REPEAT('x',60),1,1,'cadence2');");

            await MariaDbContainerFixture.ExecAsync(conn,
                "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`," +
                "`submission_date`,`last_update`,`risk_catalog_mapping`,`threat_catalog_mapping`," +
                "`template_group_id`,`submitted_by`) VALUES (911,'New','Untreated','R-911','',''," +
                "'2026-01-01 00:00:00','2026-01-01 00:00:00','','',1,911);");

            await MariaDbContainerFixture.ExecAsync(conn,
                "INSERT INTO `risk_scoring` (`id`,`scoring_method`,`calculated_risk`) " +
                "VALUES (911,1,8.0);");

            await MariaDbContainerFixture.ExecAsync(conn,
                "UPDATE `settings` SET `value` = 'ResidualRisk' WHERE `name` = 'next_review_date_uses';");
        }

        var level = await NewService().GetRiskReviewLevelAsync(911);

        Assert.Equal("High", level.Name);
    }

    /// <summary>
    /// The daily sweep the notification job runs (8.5.1), over the same real bands. Submitted
    /// 2026-01-01, asked on 2026-06-01: 151 days, which is past the 90-day High interval and short of
    /// the 240-day Low one. So the basis decides whether anybody is told.
    /// </summary>
    [Fact]
    public async Task TheOverdueSweepHonoursTheConfiguredBasis()
    {
        var asOf = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        await SeedOneTreatedRiskAsync("InherentRisk", new DateTime(2026, 1, 1, 0, 0, 0));

        var inherent = await NewService().GetOverdueReviewsAsync(asOf);
        var flagged = Assert.Single(inherent);
        Assert.Equal(910, flagged.RiskId);
        Assert.Equal(90, flagged.CadenceDays);
        Assert.Equal(61, flagged.DaysOverdue);
        Assert.Equal(8.0f, flagged.Score);

        await SeedOneTreatedRiskAsync("ResidualRisk", new DateTime(2026, 1, 1, 0, 0, 0));

        Assert.Empty(await NewService().GetOverdueReviewsAsync(asOf));
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Continuity;
using Model.DecisionCycle;
using Model.Exceptions;
using MySqlConnector;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// The archive (Stage 9.9) and the Gate A check every archive, closure, acceptance and committee decision passes through,
/// run through the real services on MariaDB at the target schema version — what the in-memory provider cannot show.
///
/// The defect these pin: <c>RiskFlagsService.LoadWorldAsync</c> projected the outer risk's id inside the
/// <c>SelectMany</c> collection selector, which EF can only translate as CROSS APPLY — and MariaDB has no LATERAL join, so
/// the query threw <see cref="InvalidOperationException"/> ("could not be translated") before reaching the database.
/// <c>DecisionCycleErrors</c> maps nothing for that, so "Archive risk" in the desktop answered 500 and the client showed
/// "Error calling /RiskArchive/Risks/{id}". The in-memory provider evaluates the same LINQ happily, which is why
/// <c>RiskArchiveServiceInMemoryTest</c> stayed green.
///
/// The contexts are built with the production options (<c>DalService</c>: the retrying execution strategy included).
/// Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9RiskArchiveWriteTests(MariaDbContainerFixture fixture)
{
    private const int Actor = 990;
    private const int Owner = 991;
    private const int RiskId = 9911;
    private const int FindingId = 9921;
    private const int BelongsToThirdParty = 3;
    private const string KevCve = "CVE-2024-3400";

    private sealed class ContainerDal(MariaDbContainerFixture f, int userId) : IDalService
    {
        private readonly ServerVersion _version = ServerVersion.AutoDetect(f.ConnectionString);

        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false)
        {
            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseMySql(f.ConnectionString + "ConvertZeroDateTime=True;", _version,
                    o => o.EnableRetryOnFailure(15, TimeSpan.FromSeconds(60), null))
                .Options;
            return new AuditableContext(options)
                { UserId = withIdentity ? userId : 0, EntityScope = EntityScope.Unrestricted };
        }

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private static Task ExecAsync(MySqlConnection conn, string sql) => MariaDbContainerFixture.ExecAsync(conn, sql);

    private static async Task<long> CountAsync(MySqlConnection conn, string sql)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// The latest schema; user 990 acts, user 991 submits, owns and manages risk 9911 ("Mgmt Reviewed", with a review that
    /// settled it, so the Track 8 state machine lets it close). With <paramref name="kevFinding"/>, an active finding 9921
    /// linked to the risk carries a CVE listed in KEV — the basis of flag 3, a Gate A condition.
    /// </summary>
    private async Task<MySqlConnection> SeedAsync(bool kevFinding = false)
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);
        var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        // The legacy columns the seed leaves out keep their legacy defaults, as the fixture's own init does.
        await ExecAsync(conn,
            "SET SESSION sql_mode = '';" +
            "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
            $"VALUES ({Actor},1,0,'local','U{Actor}','u{Actor}@x.test','s',REPEAT('x',60),1,1,'user{Actor}')," +
            $"({Owner},1,0,'local','U{Owner}','u{Owner}@x.test','s',REPEAT('x',60),1,0,'user{Owner}');" +
            "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`,`submission_date`,`last_update`," +
            "`risk_catalog_mapping`,`threat_catalog_mapping`,`template_group_id`,`owner`,`manager`,`submitted_by`) VALUES " +
            $"({RiskId},'Mgmt Reviewed','Incapacidade de desenvolver software','R-{RiskId}','','',UTC_TIMESTAMP()," +
            $"UTC_TIMESTAMP(),'','',1,{Owner},{Owner},{Owner});" +
            "INSERT INTO mgmt_reviews (risk_id, submission_date, review, reviewer, next_step, comments, next_review) " +
            $"VALUES ({RiskId}, UTC_TIMESTAMP(), 1, {Actor}, 4, 'Settled', CURDATE() + INTERVAL 1 YEAR);");

        if (kevFinding)
            await ExecAsync(conn,
                "SET SESSION sql_mode = '';" +
                $"INSERT INTO vulnerabilities (Id, Title, Cves, status_id) VALUES ({FindingId}, 'PAN-OS', '{KevCve}', 1);" +
                $"INSERT INTO risks_to_vulnerabilities (risk_id, vulnerability_id) VALUES ({RiskId}, {FindingId});" +
                "INSERT INTO kev_entries (cve_id, date_added, created_at) " +
                $"VALUES ('{KevCve}', UTC_TIMESTAMP(), UTC_TIMESTAMP());");

        return conn;
    }

    private static (RiskArchiveService Archives, RiskFlagsService Flags) Services(IDalService dal)
    {
        var logger = Substitute.For<Serilog.ILogger>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var continuity = Substitute.For<IContinuityService>();
        continuity.GetCriticalProcessThreatsAsync().Returns(new List<CriticalProcessThreatDto>());

        var flags = new RiskFlagsService(logger, dal, continuity, notifications);
        var workflow = new RiskWorkflowService(logger, dal, flags);
        return (new RiskArchiveService(logger, dal, workflow, notifications), flags);
    }

    /// <summary>The request the "Archive and reviews" screen sends for the reported case.</summary>
    private static RiskArchiveRequest GuiRequest() => new()
    {
        Justification = "Operated end to end by the supplier under contract.",
        CloseReason = BelongsToThirdParty,
        Conditions =
        [
            new RiskArchiveConditionRequest
            {
                TriggerType = ReassessmentTriggerType.ArchitectureOrTechnologyChange,
                Description = "The platform is brought back in house."
            }
        ]
    };

    /// <summary>
    /// W1 — the reported case: archiving with "Belongs to third party", a justification and the architecture condition
    /// with its text closes the risk and writes the archive, its condition, the closure and the decision.
    /// </summary>
    [Fact]
    public async Task TestW1_ArchivingTheReportedCaseWritesOnMariaDb()
    {
        await using var conn = await SeedAsync();

        var dto = await Services(new ContainerDal(fixture, Actor)).Archives.ArchiveAsync(RiskId, GuiRequest(), Actor);

        Assert.Equal(RiskArchiveState.Live, dto.State);
        Assert.Equal(1L, await CountAsync(conn,
            $"SELECT COUNT(*) FROM risks r JOIN closures c ON c.id = r.close_id WHERE r.id = {RiskId} " +
            $"AND r.status = 'Closed' AND c.close_reason = {BelongsToThirdParty} AND c.user_id = {Actor}"));
        Assert.Equal(1L, await CountAsync(conn,
            "SELECT COUNT(*) FROM risk_archives a JOIN risk_archive_conditions k ON k.archive_id = a.id " +
            $"WHERE a.risk_id = {RiskId} AND a.status = 1 AND a.previous_status = 'Mgmt Reviewed' " +
            "AND k.trigger_type = 1 AND k.description = 'The platform is brought back in house.'"));
        Assert.Equal(1L, await CountAsync(conn,
            $"SELECT COUNT(*) FROM risk_decisions WHERE risk_id = {RiskId} AND decision = 4 AND decided_by_id = {Actor}"));
    }

    /// <summary>
    /// W2 — the derivation the query feeds reads the real join: a linked active finding whose CVE is in KEV derives flag 3
    /// for the right risk, and Gate A refuses the archive with its rule instead of a 500, writing nothing of it.
    /// </summary>
    [Fact]
    public async Task TestW2_AKevFindingRaisesGateAAndTheArchiveIsRefusedNotBroken()
    {
        await using var conn = await SeedAsync(kevFinding: true);

        var refusal = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Services(new ContainerDal(fixture, Actor)).Archives.ArchiveAsync(RiskId, GuiRequest(), Actor));

        Assert.Equal(RiskFlagsService.GateARule, refusal.RuleName);
        Assert.Equal(1L, await CountAsync(conn,
            $"SELECT COUNT(*) FROM risk_flags WHERE risk_id = {RiskId} AND flag = 3 AND derived = 1 " +
            $"AND derived_basis LIKE '%{KevCve} on finding #{FindingId}%'"));
        Assert.Equal(0L, await CountAsync(conn, $"SELECT COUNT(*) FROM risk_archives WHERE risk_id = {RiskId}"));
        Assert.Equal(0L, await CountAsync(conn, $"SELECT COUNT(*) FROM closures WHERE risk_id = {RiskId}"));
        Assert.Equal(1L, await CountAsync(conn,
            $"SELECT COUNT(*) FROM risks WHERE id = {RiskId} AND status = 'Mgmt Reviewed' AND close_id IS NULL"));
    }

    /// <summary>W3 — the daily sweep reads the same join over every open risk (<c>riskId: null</c>) and derives flag 3.</summary>
    [Fact]
    public async Task TestW3_TheSweepOverEveryOpenRiskRunsOnMariaDb()
    {
        await using var conn = await SeedAsync(kevFinding: true);

        var summary = await Services(new ContainerDal(fixture, Actor)).Flags.RefreshAllAsync();

        Assert.Equal((1, 1, 1), (summary.RisksEvaluated, summary.FlagsRaised, summary.GateAOnsets));
        Assert.Equal(1L, await CountAsync(conn,
            $"SELECT COUNT(*) FROM risk_flags WHERE risk_id = {RiskId} AND flag = 3 AND derived = 1"));
    }
}

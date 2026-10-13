using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Continuity;
using Model.DecisionCycle;
using MySqlConnector;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// Stage 9.9 (S50 §4.6) — a risk-committee decision submitted and decided on a real MariaDB, through the real services and
/// the production context options (the retrying execution strategy included).
///
/// The in-memory suite (<c>RiskCommitteesServiceInMemoryTest</c>) cannot see a query MariaDB refuses to run: submitting a
/// decision runs Gate A, whose flag derivation (<c>RiskFlagsService.LoadWorldAsync</c>) read the risk's open findings with a
/// correlated projection that EF translates as CROSS APPLY — a LATERAL join, which MariaDB does not have. The translation
/// threw, the controller answered 500, and the desktop showed "Error calling /RiskCommittees/1/Decisions" for every
/// submission. These tests submit exactly what the desktop sends and then decide it, so both Gate A checks of the cycle —
/// on submission and on the deciding vote — run against real SQL. Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class Track9RiskCommitteeDecisionTests(MariaDbContainerFixture fixture)
{
    private const int CommitteeId = 1;
    private const int RiskId = 9912;
    private const int Submitter = 993;
    private static readonly int[] Members = [990, 991, 992];

    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        private readonly ServerVersion _version = ServerVersion.AutoDetect(f.ConnectionString);

        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false)
        {
            // DalService.GetDbContextOptionsBuilder's options, the retrying strategy included.
            var options = new DbContextOptionsBuilder<NRDbContext>()
                .UseMySql(f.ConnectionString + "ConvertZeroDateTime=True;", _version,
                    o => o.EnableRetryOnFailure(15, TimeSpan.FromSeconds(60), null))
                .Options;
            return new AuditableContext(options) { EntityScope = EntityScope.Unrestricted };
        }

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    /// <summary>
    /// The organization's committee (no entity) of three members requiring two approvals, and a risk the submitter owns,
    /// manages and submitted — so no member is recused.
    /// </summary>
    private async Task SeedAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();

        var users = string.Join(",", Members.Append(Submitter).Select(u =>
            $"({u},1,0,'local','U{u}','u{u}@x.test','s',REPEAT('x',60),1,{(u == Submitter ? 1 : 0)},'user{u}')"));

        await MariaDbContainerFixture.ExecAsync(conn,
            "SET SESSION sql_mode = '';" +
            "INSERT INTO `user` (`value`,`enabled`,`lockout`,`type`,`name`,`email`,`salt`,`password`,`role_id`,`admin`,`login`) " +
            $"VALUES {users};" +
            "INSERT INTO `risks` (`id`,`status`,`subject`,`reference_id`,`assessment`,`notes`,`submission_date`,`last_update`," +
            "`risk_catalog_mapping`,`threat_catalog_mapping`,`template_group_id`,`owner`,`manager`,`submitted_by`) VALUES " +
            $"({RiskId},'New','Incapacidade de desenvolver software','R-{RiskId}','','',UTC_TIMESTAMP(),UTC_TIMESTAMP()," +
            $"'','',1,{Submitter},{Submitter},{Submitter});" +
            "INSERT INTO risk_committees (id, name, required_approvals, created_at) " +
            $"VALUES ({CommitteeId}, 'Comitê de Risco de TI', 2, UTC_TIMESTAMP());" +
            "INSERT INTO risk_committee_members (committee_id, user_id, created_at) VALUES " +
            string.Join(",", Members.Select(m => $"({CommitteeId}, {m}, UTC_TIMESTAMP())")) + ";");
    }

    /// <summary>The real Gate A chain: committee → workflow → flags. Notifications and the BIA threats are not under test.</summary>
    private RiskCommitteesService Service()
    {
        var dal = new ContainerDal(fixture);
        var logger = Substitute.For<Serilog.ILogger>();
        var notifications = Substitute.For<INotificationEventPublisher>();
        var continuity = Substitute.For<IContinuityService>();
        continuity.GetCriticalProcessThreatsAsync().Returns(new List<CriticalProcessThreatDto>());

        var flags = new RiskFlagsService(logger, dal, continuity, notifications);
        var workflow = new RiskWorkflowService(logger, dal, flags);
        var acceptances = new RiskAcceptancesService(logger, dal, workflow, Substitute.For<IPermissionsService>());
        return new RiskCommitteesService(logger, dal, workflow, acceptances, notifications);
    }

    /// <summary>
    /// What the desktop's "Submit decision" sends for an acceptance: no acceptance to renew, and the optional compensating
    /// controls and minutes reference as empty strings (bound text boxes), the expiry as a UTC instant.
    /// </summary>
    private static RiskCommitteeDecisionRequest AsTheDesktopSendsIt() => new()
    {
        RiskId = RiskId, Kind = RiskCommitteeDecisionKind.Accept, RenewsAcceptanceId = null, Name = "xdxcvb",
        BusinessJustification = "xcbxcvbxcvb", CompensatingControls = string.Empty,
        ExpiresAt = new DateTimeOffset(DateTime.UtcNow.Date.AddYears(1)).UtcDateTime, MinutesReference = string.Empty
    };

    /// <summary>D1 — the acceptance submitted from the desktop opens a decision, with the empty optional fields stored as null.</summary>
    [Fact]
    public async Task TestD1_AnAcceptanceSubmittedAsTheDesktopSendsItOpensADecisionOnMariaDb()
    {
        await SeedAsync();

        var dto = await Service().OpenDecisionAsync(CommitteeId, AsTheDesktopSendsIt(), Submitter);

        Assert.Equal((RiskCommitteeDecisionStatus.Open, RiskCommitteeDecisionKind.Accept, RiskId, 2, 3),
            (dto.Status, dto.Kind, dto.RiskId, dto.RequiredApprovals, dto.EligibleVoters));
        Assert.Equal(("xdxcvb", (string?)null, (string?)null, (int?)null),
            (dto.Name, dto.CompensatingControls, dto.MinutesReference, dto.RenewsAcceptanceId));
        Assert.Single(await Service().GetDecisionsAsync(CommitteeId, null, openOnly: true));
    }

    /// <summary>
    /// D2 — the deciding vote: the second approval passes Gate A again on real SQL and creates the acceptance in the same
    /// write, naming the deciding member.
    /// </summary>
    [Fact]
    public async Task TestD2_TheSecondApprovalCreatesTheAcceptanceOnMariaDb()
    {
        await SeedAsync();
        var service = Service();
        var decision = await service.OpenDecisionAsync(CommitteeId, AsTheDesktopSendsIt(), Submitter);

        var first = await service.VoteAsync(decision.Id,
            new RiskCommitteeVoteRequest { Choice = RiskCommitteeVoteChoice.Approve, Comment = "" }, Members[0]);
        Assert.Equal((RiskCommitteeDecisionStatus.Open, 1, (int?)null), (first.Status, first.Approvals, first.AcceptanceId));

        var deciding = await service.VoteAsync(decision.Id,
            new RiskCommitteeVoteRequest { Choice = RiskCommitteeVoteChoice.Approve }, Members[1]);

        Assert.Equal((RiskCommitteeDecisionStatus.Approved, 2), (deciding.Status, deciding.Approvals));
        Assert.NotNull(deciding.AcceptanceId);

        await using var db = new ContainerDal(fixture).GetContext();
        var acceptance = await db.RiskAcceptances.AsNoTracking().SingleAsync(a => a.Id == deciding.AcceptanceId);
        Assert.Equal((RiskId, RiskAcceptanceStatus.Active, Members[1], (int?)Submitter),
            (acceptance.RiskId, acceptance.Status, acceptance.AuthorizingManagerId, acceptance.RequestedById));
    }
}

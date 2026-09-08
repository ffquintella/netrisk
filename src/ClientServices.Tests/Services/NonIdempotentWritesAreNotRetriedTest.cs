using System;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DTO;
using Model.Governance;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// One invariant, asserted once per REST service: a non-idempotent request leaves this client
/// exactly once, however the server answers.
///
/// The bug this file exists for was found on the integrations screen — the sync log showed eleven
/// Vision One runs for a connection that has one, all started within three seconds of a single click
/// on "Sync now". The cause was not in the integrations code: every service in
/// <c>ClientServices.Services</c> sent its writes through <c>RestService.GetReliableClient</c>, which
/// retries anything answering 500, 502, 503 or 504 — and the wrapper it returns retries eleven times
/// with no delay between attempts, swallowing each exception on the way, on top of the Polly policy
/// around it. On a sync that is eleven duplicate jobs. On <c>HostsRestService.Create</c> or
/// <c>VulnerabilitiesRestService.CreateAsync</c> it is eleven rows, which is worse: the jobs
/// eventually finish, the rows stay.
///
/// A 5xx after a write is ambiguous by nature — the server may well have committed the row before
/// failing to say so — so the client cannot resolve it by trying again. It has to report it. Writes
/// therefore go through <c>RestServiceBase.MutatingClient</c>, which is the same client without the
/// retry policy; reads stay on the reliable one, which is the case retrying was built for and is
/// what the <c>…IsStillRetried</c> tests below pin down, so this fix cannot be "corrected" into
/// dropping retries altogether.
///
/// Every test here is red on the pre-fix code with a request count of eleven, not two: the count
/// assertion is the test, and <see cref="StubRestBackend.RetriesLikeProduction"/> is what makes it
/// visible by wrapping the stub in the production <c>ReliableRestClientWrapper</c>.
/// </summary>
[TestSubject(typeof(RestServiceBase))]
public class NonIdempotentWritesAreNotRetriedTest : BaseServiceTest
{
    private static StubRestBackend Retrying() => new() { RetriesLikeProduction = true };

    /// <summary>
    /// The assertion every test in this file ends with, named so a failure reads as the defect rather
    /// than as an off-by-ten. The exception the write ends with is deliberately not asserted: which
    /// one surfaces depends on the service's own status handling, and the point here is the number of
    /// times the server was asked to do the work.
    /// </summary>
    private static async Task SentOnce(StubRestBackend backend, Func<Task> write)
    {
        try
        {
            await write();
        }
        catch (Exception)
        {
            // A 5xx has to fail the call. That it does is covered by each service's own tests.
        }

        Assert.Single(backend.Requests);
    }

    private static async Task Retried(StubRestBackend backend, Func<Task> read)
    {
        try
        {
            await read();
        }
        catch (Exception)
        {
            // Ditto — the attempt count is the subject.
        }

        Assert.True(backend.Requests.Count > 1,
            $"A read should still be retried, but only {backend.Requests.Count} request(s) went out.");
    }

    // --- HostsRestService ---------------------------------------------------------------------
    //
    // The most expensive of these to get wrong: a retried POST /Hosts is a duplicate host row, and
    // the importers key later findings off the host they matched.

    [Fact]
    public async Task CreatingAHostIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IHostsService>(backend);

        backend.OnStatus(Method.Post, "/Hosts", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.Create(AHost()));
    }

    [Fact]
    public async Task CreatingAHostServiceIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IHostsService>(backend);

        backend.OnStatus(Method.Post, "/Hosts/1/Services", HttpStatusCode.ServiceUnavailable);

        await SentOnce(backend, () => service.CreateAndAddServiceAsync(1,
            new HostsServiceDto { Name = "ssh", Port = 22, Protocol = "tcp" }));
    }

    [Fact]
    public async Task ListingHostServicesIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IHostsService>(backend);

        backend.OnStatus(Method.Get, "/Hosts/1/Services", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetAllHostServiceAsync(1));
    }

    // --- VulnerabilitiesRestService -----------------------------------------------------------

    [Fact]
    public async Task CreatingAVulnerabilityIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IVulnerabilitiesService>(backend);

        backend.OnStatus(Method.Post, "/Vulnerabilities", HttpStatusCode.InternalServerError);

        await SentOnce(backend, () => service.CreateAsync(AVulnerability()));
    }

    [Fact]
    public async Task StartingAnImportIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IVulnerabilitiesService>(backend);

        // The ASPM half of the same partial class, and the one write here that starts a background
        // job on the server: eleven of those import the same file eleven times.
        backend.OnStatus(Method.Post, "/Vulnerabilities/import/auto/f-1", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.StartImportAsync("auto", "f-1"));
    }

    [Fact]
    public async Task ListingImportersIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IVulnerabilitiesService>(backend);

        backend.OnStatus(Method.Get, "/Vulnerabilities/importers", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetImportersAsync());
    }

    // --- IncidentsRestService -----------------------------------------------------------------

    [Fact]
    public async Task CreatingAnIncidentIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IIncidentsService>(backend);

        backend.OnStatus(Method.Post, "/Incidents", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.CreateAsync(
            new Incident { Name = "Phishing wave", Description = "Reported by three users" }));
    }

    [Fact]
    public async Task DeletingAnIncidentIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IIncidentsService>(backend);

        backend.OnStatus(Method.Delete, "/Incidents/4", HttpStatusCode.GatewayTimeout);

        await SentOnce(backend, () => service.DeleteAsync(4));
    }

    [Fact]
    public async Task ListingIncidentsIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IIncidentsService>(backend);

        backend.OnStatus(Method.Get, "/Incidents", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetAllAsync());
    }

    // --- IncidentResponsePlansRestService -----------------------------------------------------

    [Fact]
    public async Task CreatingAPlanIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IIncidentResponsePlansService>(backend);

        backend.OnStatus(Method.Post, "/IncidentResponsePlans", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.CreateAsync(APlan()));
    }

    [Fact]
    public async Task AddingATaskDependencyIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IIncidentResponsePlansService>(backend);

        // This one goes out through ExecuteAsync(request, Method.Post) rather than PostAsync, which
        // is a different path into the wrapper and so worth pinning separately.
        backend.OnStatus(Method.Post, "/IncidentResponsePlans/1/Tasks/2/Dependencies/3",
            HttpStatusCode.ServiceUnavailable);

        await SentOnce(backend, () => service.AddDependencyAsync(1, 2, 3));
    }

    [Fact]
    public async Task ListingPlansIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IIncidentResponsePlansService>(backend);

        backend.OnStatus(Method.Get, "/IncidentResponsePlans", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetAllAsync());
    }

    // --- IrpTemplatesRestService --------------------------------------------------------------

    [Fact]
    public async Task CreatingATemplateIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IIrpTemplatesService>(backend);

        backend.OnStatus(Method.Post, "/IrpTemplates", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.CreateAsync(ATemplate()));
    }

    [Fact]
    public async Task CloningATemplateIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IIrpTemplatesService>(backend);

        backend.OnStatus(Method.Post, "/IrpTemplates/1/Clone", HttpStatusCode.InternalServerError);

        await SentOnce(backend, () => service.CloneAsync(1));
    }

    [Fact]
    public async Task ListingTemplatesIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IIrpTemplatesService>(backend);

        backend.OnStatus(Method.Get, "/IrpTemplates", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetAllAsync());
    }

    // --- RisksRestService ---------------------------------------------------------------------

    [Fact]
    public async Task AssociatingARiskToAPlanIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IRisksService>(backend);

        backend.OnStatus(Method.Patch, "/Risks/1/IncidentResponsePlan/2", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.AssociateRiskToIncidentResponsePlanAsync(1, 2));
    }

    [Fact]
    public async Task ReadingARisksPlanIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IRisksService>(backend);

        backend.OnStatus(Method.Get, "/Risks/1/IncidentResponsePlan", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetIncidentResponsePlanAsync(1));
    }

    // --- RiskGovernanceRestService ------------------------------------------------------------

    [Fact]
    public async Task CreatingARiskAcceptanceIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IRiskGovernanceService>(backend);

        backend.OnStatus(Method.Post, "/Risks/7/Acceptances", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.CreateAcceptanceAsync(7, new RiskAcceptanceRequest
        {
            BusinessJustification = "Compensating monitoring is in place.",
            ExpiresAt = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)
        }));
    }

    [Fact]
    public async Task ListingRiskAcceptancesIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IRiskGovernanceService>(backend);

        backend.OnStatus(Method.Get, "/Risks/7/Acceptances", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetAcceptancesAsync(7));
    }

    // --- FindingsAdminRestService -------------------------------------------------------------

    [Fact]
    public async Task SavingAnSlaConfigurationIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IFindingsAdminService>(backend);

        backend.OnStatus(Method.Post, "/SlaConfigurations", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.SetSlaConfigurationAsync(new SlaConfiguration()));
    }

    [Fact]
    public async Task ListingSlaConfigurationsIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IFindingsAdminService>(backend);

        backend.OnStatus(Method.Get, "/SlaConfigurations", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetSlaConfigurationsAsync());
    }

    // --- UserAccessRestService ----------------------------------------------------------------

    [Fact]
    public async Task AssigningAnEntityRoleIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IUserAccessService>(backend);

        backend.OnStatus(Method.Post, "/UserAccess/users/7/entity-roles", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.AssignEntityRoleAsync(7, 5, 2));
    }

    [Fact]
    public async Task RevokingAnEntityRoleIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IUserAccessService>(backend);

        backend.OnStatus(Method.Delete, "/UserAccess/user-entity-roles/9",
            HttpStatusCode.ServiceUnavailable);

        await SentOnce(backend, () => service.RevokeEntityRoleAsync(9));
    }

    [Fact]
    public async Task ListingEntityRolesIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<IUserAccessService>(backend);

        backend.OnStatus(Method.Get, "/UserAccess/users/7/entity-roles", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetUserEntityRolesAsync(7));
    }

    // --- CommentsRestService ------------------------------------------------------------------

    [Fact]
    public async Task CreatingACommentIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<ICommentsService>(backend);

        backend.OnStatus(Method.Post, "/Comments", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.CreateCommentAsync(
            new Comment { Type = "FixRequest", Text = "Patched in tonight's window", FixRequestId = 1 }));
    }

    [Fact]
    public async Task ListingCommentsIsStillRetried()
    {
        var backend = Retrying();
        var service = ResolveWith<ICommentsService>(backend);

        backend.OnStatus(Method.Get, "/Comments/fixrequest/1", HttpStatusCode.BadGateway);

        await Retried(backend, () => service.GetFixRequestCommentsAsync(1));
    }

    // --- EmailsRestService --------------------------------------------------------------------
    //
    // Both methods here send mail. A retry is eleven messages to the fix team, and unlike a
    // duplicated row nobody can delete them afterwards.

    [Fact]
    public async Task SendingAFixRequestMailIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IEmailsService>(backend);

        backend.OnStatus(Method.Post, "/Email/Vulnerability/FixRequest", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.SendVulnerabilityFixRequestMailAsync(new FixRequestDto
        {
            VulnerabilityId = 12,
            Comments = "please patch",
            Destination = "ops@example.org",
            FixTeamId = 4,
            Identifier = "FR-0001"
        }));
    }

    [Fact]
    public async Task SendingAnUpdateMailIsSentOnce()
    {
        var backend = Retrying();
        var service = ResolveWith<IEmailsService>(backend);

        backend.OnStatus(Method.Post, "/Email/Vulnerability/Update/12", HttpStatusCode.BadGateway);

        await SentOnce(backend, () => service.SendVulnerabilityUpdateMailAsync(12, "patched"));
    }

    // --- fixtures -----------------------------------------------------------------------------

    private static Host AHost() => new()
    {
        Id = 0,
        Ip = "10.0.0.1",
        HostName = "alpha",
        Status = 1,
        Source = "manual",
        RegistrationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static Vulnerability AVulnerability() => new()
    {
        Id = 0,
        Title = "Open port",
        Severity = "High",
        Status = 1,
        Score = 7.5,
        DetectionCount = 1,
        FirstDetection = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastDetection = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static IncidentResponsePlan APlan() => new()
    {
        Id = 0,
        Name = "Containment",
        Description = "How we contain it",
        CreationDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedById = 3,
        Status = 1
    };

    private static IrpTemplate ATemplate() => new()
    {
        Id = 0,
        Name = "Ransomware",
        Description = "Playbook",
        MatchingRulesJson = "{\"category\":\"malware\"}",
        IsEnabled = true
    };
}

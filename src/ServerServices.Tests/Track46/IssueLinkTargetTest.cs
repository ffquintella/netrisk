using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track46;

/// <summary>
/// Issue links beyond findings (Track 4 milestone 4.6): the "exactly one target" invariant, and the
/// deliberate limitation that inbound status actions apply to findings only.
///
/// The invariant is enforced three times — by <see cref="FindingIssueLink.Validate"/>, by a service
/// guard, and by a <c>CHECK</c> constraint in the schema. The first two are covered here; the third is
/// covered by <c>DAL.IntegrationTests</c>, which has a real MariaDB to enforce it.
/// </summary>
[TestSubject(typeof(FindingIssueLink))]
public class IssueLinkTargetTest : InMemoryServiceTestBase
{
    private readonly IJiraIntegrationService _jira;
    private readonly IIssueTrackerService _trackers;

    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    public IssueLinkTargetTest()
    {
        _jira = GetService<IJiraIntegrationService>();
        _trackers = GetService<IIssueTrackerService>();

        Seed(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = 1, Name = "analyst", Login = "analyst", Enabled = true, Type = "local",
                Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = "analyst@acme.com"
            });

            ctx.Incidents.Add(new Incident
            {
                Id = 7, Name = "2026-0007", Description = "Payment outage.",
                CreationDate = Now, LastUpdate = Now, CreatedById = 1,
                Status = (int)IntStatus.Open
            });
        });
    }

    // --- the entity's own invariant ----------------------------------------------------------

    [Fact]
    public void SetTargetPointsAtOneRecordAndClearsTheOthers()
    {
        var link = new FindingIssueLink { ConnectionId = 1, IssueKey = "SD-1" };

        link.SetTarget(IssueLinkTargetKind.Finding, 42);
        Assert.Equal(42, link.VulnerabilityId);
        Assert.Null(link.IncidentId);
        Assert.Null(link.RiskId);
        Assert.Equal(42, link.TargetId);

        // Re-pointing must clear the previous column, or the link claims to be an incident link while
        // still carrying a vulnerability id — and the CHECK constraint would reject the row.
        link.SetTarget(IssueLinkTargetKind.Incident, 7);
        Assert.Null(link.VulnerabilityId);
        Assert.Equal(7, link.IncidentId);
        Assert.Equal(7, link.TargetId);
        Assert.Null(link.Validate());

        link.SetTarget(IssueLinkTargetKind.Risk, 9);
        Assert.Null(link.IncidentId);
        Assert.Equal(9, link.RiskId);
        Assert.Equal(9, link.TargetId);
        Assert.Null(link.Validate());
    }

    [Fact]
    public void ALinkWithNoTargetIsInvalid()
    {
        var problem = new FindingIssueLink { ConnectionId = 1, IssueKey = "SD-1" }.Validate();

        Assert.NotNull(problem);
        Assert.Contains("exactly one", problem);
    }

    [Fact]
    public void ALinkWithTwoTargetsIsInvalid()
    {
        var link = new FindingIssueLink
        {
            ConnectionId = 1, IssueKey = "SD-1", VulnerabilityId = 42, IncidentId = 7
        };

        Assert.NotNull(link.Validate());
    }

    /// <summary>
    /// A discriminator that disagrees with the column that is set is invalid.
    ///
    /// This is the state a hand-assigned link ends up in, and it is why <see cref="FindingIssueLink.SetTarget"/>
    /// exists: the row would satisfy a naive "exactly one column" check and still be read as the wrong
    /// kind by every query that filters on <c>target_kind</c>.
    /// </summary>
    [Fact]
    public void ADiscriminatorThatDisagreesWithTheColumnIsInvalid()
    {
        var link = new FindingIssueLink
        {
            ConnectionId = 1, IssueKey = "SD-1",
            TargetKind = IssueLinkTargetKind.Incident,
            VulnerabilityId = 42
        };

        var problem = link.Validate();

        Assert.NotNull(problem);
        Assert.Contains("Incident", problem);
    }

    [Fact]
    public void ADefaultLinkIsAFindingLink()
    {
        // The column default is what makes the 4.6 migration additive: every row written before it is
        // a finding link and reads correctly with no backfill.
        Assert.Equal(IssueLinkTargetKind.Finding, new FindingIssueLink().TargetKind);
    }

    // --- the service --------------------------------------------------------------------------

    private async Task<int> ConnectionAsync()
    {
        var created = await _trackers.CreateConnectionAsync(new IssueTrackerConnection
        {
            Name = "Service desk",
            Provider = IssueTrackerProviderKind.Jira,
            BaseUrl = "https://acme.atlassian.net",
            ProjectKey = "SD",
            AuthUser = "bot@acme.com",
            Enabled = true,
            PollIntervalMinutes = 15
        }, "api-token", null, 1);

        return created.Id;
    }

    private const string IssueJson = """
        {
          "id": "10501",
          "key": "SD-4711",
          "fields": {
            "summary": "Payment gateway is timing out",
            "status": { "name": "Waiting for support", "statusCategory": { "key": "indeterminate" } },
            "updated": "2026-08-25T10:00:00.000+0000"
          }
        }
        """;

    [Fact]
    public async Task AnIncidentCanBeLinkedToAnExistingIssue()
    {
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SD-4711", IssueJson);

        var id = await ConnectionAsync();

        var link = await _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 7, "SD-4711", 1);

        Assert.Equal(IssueLinkTargetKind.Incident, link.TargetKind);
        Assert.Equal(7, link.TargetId);
        Assert.Equal("SD-4711", link.IssueKey);
        // FindingId is 0 for a non-finding link, and the finding panel reads that field.
        Assert.Equal(0, link.FindingId);

        var links = await _jira.GetLinksForRecordAsync(IssueLinkTargetKind.Incident, 7);
        Assert.Single(links);
    }

    /// <summary>
    /// Linking to an issue that does not exist fails here, where somebody is watching and can fix the
    /// typo, rather than on every later sync.
    /// </summary>
    [Fact]
    public async Task LinkingToAnIssueThatDoesNotExistIsRefused()
    {
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SD-9999", "{}", 404);

        var id = await ConnectionAsync();

        await Assert.ThrowsAsync<DataNotFoundException>(
            () => _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 7, "SD-9999", 1));
    }

    [Fact]
    public async Task LinkingARecordThatDoesNotExistIsRefused()
    {
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SD-4711", IssueJson);

        var id = await ConnectionAsync();

        await Assert.ThrowsAsync<DataNotFoundException>(
            () => _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 999, "SD-4711", 1));
    }

    [Fact]
    public async Task LinkingTheSameRecordTwiceReturnsTheExistingLink()
    {
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SD-4711", IssueJson);

        var id = await ConnectionAsync();

        var first = await _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 7, "SD-4711", 1);
        var second = await _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 7, "SD-4711", 1);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await _jira.GetLinksForRecordAsync(IssueLinkTargetKind.Incident, 7));
    }

    /// <summary>
    /// An issue already linked elsewhere reports where, naming the right kind.
    ///
    /// Before 4.6 this message could only say "finding #N"; saying that about an incident link would
    /// send somebody looking for a finding that does not exist.
    /// </summary>
    [Fact]
    public async Task AnIssueAlreadyLinkedToAnotherRecordNamesThatRecordsKind()
    {
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SD-4711", IssueJson);

        var id = await ConnectionAsync();

        await _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 7, "SD-4711", 1);

        Seed(ctx => ctx.Incidents.Add(new Incident
        {
            Id = 8, Name = "2026-0008", Description = "Another.",
            CreationDate = Now, LastUpdate = Now, CreatedById = 1, Status = (int)IntStatus.Open
        }));

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 8, "SD-4711", 1));

        Assert.Contains("incident #7", ex.Message);
    }

    /// <summary>
    /// Findings go through the finding endpoint, which also applies the auto-create policy and feeds
    /// the conflict queue. Routing them here instead would quietly skip both.
    /// </summary>
    [Fact]
    public async Task CreatingAnIssueForAFindingThroughTheRecordPathIsRefused()
    {
        var id = await ConnectionAsync();

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _jira.CreateIssueForRecordAsync(id, IssueLinkTargetKind.Finding, 42, 1));

        Assert.Contains("finding-issues endpoint", ex.Message);
    }

    /// <summary>
    /// The milestone's stated limitation, asserted rather than documented.
    ///
    /// An incident's external status is mirrored — the poll records the change and says so — and the
    /// incident itself is untouched. Transitioning it would be a policy nobody has specified, and this
    /// repository has three times shipped a control that was documented as working and was not.
    /// </summary>
    [Fact]
    public async Task AnInboundStatusChangeOnAnIncidentIsMirroredAndAppliesNoAction()
    {
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SD-4711", IssueJson);

        var id = await ConnectionAsync();

        await _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 7, "SD-4711", 1);

        // The connection maps Done to MarkMitigated, which is a finding action.
        await _trackers.SetStatusMappingsAsync(id,
            [new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }]);

        FakeOutboundHttpClient.Reset();
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SD-4711",
            IssueJson.Replace("Waiting for support", "Done").Replace("indeterminate", "done"));

        var result = await _trackers.PollConnectionAsync(id, 1);

        Assert.Equal(1, result.Examined);
        Assert.Equal(1, result.Changed);
        // Recorded, and nothing applied.
        Assert.Equal(0, result.Applied);
        Assert.Contains("no action is applied", string.Join(" ", result.Messages));

        var link = Assert.Single(await _jira.GetLinksForRecordAsync(IssueLinkTargetKind.Incident, 7));
        Assert.Equal("Done", link.LastSyncedStatus);

        Read(ctx => Assert.Equal((int)IntStatus.Open, ctx.Incidents.Single(i => i.Id == 7).Status));
    }

    /// <summary>
    /// A finding's links do not pick up an incident link on the same connection, and vice versa. The
    /// widening had to leave the finding queries narrower than the table.
    /// </summary>
    [Fact]
    public async Task AFindingsLinkListDoesNotIncludeAnIncidentsLink()
    {
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SD-4711", IssueJson);

        var id = await ConnectionAsync();

        await _jira.LinkRecordAsync(id, IssueLinkTargetKind.Incident, 7, "SD-4711", 1);

        Assert.Empty(await _trackers.GetLinksForFindingAsync(7));
        Assert.Single(await _jira.GetLinksForRecordAsync(IssueLinkTargetKind.Incident, 7));
    }

    private void Read(Action<DAL.Context.AuditableContext> assert)
    {
        using var context = OpenContext();
        assert(context);
    }
}

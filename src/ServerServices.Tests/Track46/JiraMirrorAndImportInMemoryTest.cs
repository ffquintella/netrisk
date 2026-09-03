using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model;
using Model.Integrations;
using ServerServices.Integrations.IssueTrackers.Jira;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track46;

/// <summary>
/// The Service Management mirror and the Assets register import end to end, against recorded Jira
/// payloads (Track 4 milestone 4.6).
///
/// This is where the milestone's acceptance criteria live: a configured queue populates the mirror with
/// each request's SLA cycles; an object type mapped to <c>Host</c> imports servers with name,
/// responsible, environment and active state; a second import updates the same row instead of creating
/// a duplicate; and a dry run writes nothing.
///
/// Every call goes through <see cref="Mock.FakeOutboundHttpClient"/>, so nothing here can reach
/// Atlassian.
/// </summary>
[TestSubject(typeof(JiraIntegrationService))]
public class JiraMirrorAndImportInMemoryTest : InMemoryServiceTestBase
{
    private readonly IJiraIntegrationService _svc;
    private readonly IIssueTrackerService _trackers;

    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string Workspace = "b2c3d4e5-0000-1111-2222-333344445555";

    public JiraMirrorAndImportInMemoryTest()
    {
        _svc = GetService<IJiraIntegrationService>();
        _trackers = GetService<IIssueTrackerService>();

        Seed(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = 1, Name = "analyst", Login = "analyst", Enabled = true, Type = "local",
                Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = "analyst@acme.com"
            });

            // A host the scanners already know about, with a MAC and no external id. The Assets
            // import must recognise it rather than creating a second row for the same machine.
            ctx.Hosts.Add(new Host
            {
                Id = 1, HostName = "srv-old-name", Ip = "10.0.0.5",
                MacAddress = "AA-BB-CC-DD-EE-FF", Source = "nessus",
                RegistrationDate = Now.AddYears(-1), Status = (short)IntStatus.Active
            });
        });
    }

    /// <summary>Asserts against the database directly. The base exposes a context, not a reader.</summary>
    private void Read(Action<DAL.Context.AuditableContext> assert)
    {
        using var context = OpenContext();
        assert(context);
    }

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

    // --- the Service Management mirror ------------------------------------------------------

    private const string QueueIssuesJson = """
        { "values": [ { "key": "SD-4711" } ], "isLastPage": true, "size": 1 }
        """;

    private const string RequestJson = """
        {
          "issueId": "10501",
          "issueKey": "SD-4711",
          "requestType": { "id": "77", "name": "Report a system problem", "serviceDeskId": "3" },
          "reporter": { "accountId": "5b10a", "displayName": "Alice Silva" },
          "requestFieldValues": [
            { "fieldId": "summary", "value": "Payment gateway is timing out" }
          ],
          "currentStatus": { "status": "Waiting for support", "statusCategory": "IN_PROGRESS" },
          "createdDate": { "iso8601": "2026-08-25T09:12:00+00:00" },
          "updatedDate": { "iso8601": "2026-08-25T10:00:00+00:00" },
          "_links": { "web": "https://acme.atlassian.net/servicedesk/customer/portal/3/SD-4711" }
        }
        """;

    private const string SlaJson = """
        {
          "values": [
            {
              "id": "1",
              "name": "Time to first response",
              "completedCycles": [],
              "ongoingCycle": {
                "startTime": { "iso8601": "2026-08-25T09:12:00+00:00" },
                "breached": true,
                "paused": false,
                "goalDuration":  { "millis": 3600000 },
                "elapsedTime":   { "millis": 9000000 },
                "remainingTime": { "millis": -5400000 }
              }
            }
          ]
        }
        """;

    /// <summary>
    /// Rules for the mirror.
    ///
    /// The SLA rule is registered *before* the request rule on purpose: the fake matches on a URL
    /// substring and first match wins, and <c>/request/SD-4711/sla</c> also contains
    /// <c>/request/SD-4711</c>.
    /// </summary>
    private void GivenAServiceDesk()
    {
        FakeOutboundHttpClient.RuleFor("/queue/10/issue", QueueIssuesJson);
        FakeOutboundHttpClient.RuleFor("/request/SD-4711/sla", SlaJson);
        FakeOutboundHttpClient.RuleFor("/request/SD-4711", RequestJson);
    }

    private async Task<int> ConnectionWithAQueueAsync()
    {
        var id = await ConnectionAsync();

        var settings = await _svc.GetSettingsAsync(id);
        settings.JsmEnabled = true;
        settings.ServiceDeskId = 3;
        settings.ImportSlas = true;
        settings.QueueImports =
            [new JiraQueueImportView { ServiceDeskId = 3, QueueId = 10, QueueName = "Open" }];

        await _svc.SaveSettingsAsync(id, settings);

        return id;
    }

    [Fact]
    public async Task AConfiguredQueuePopulatesTheMirrorWithTheRequestAndItsSla()
    {
        GivenAServiceDesk();
        var id = await ConnectionWithAQueueAsync();

        var result = await _svc.SyncServiceManagementAsync(id, 1);

        Assert.Equal(1, result.QueuesExamined);
        Assert.Equal(1, result.RequestsExamined);
        Assert.Equal(1, result.RequestsCreated);
        Assert.Equal(1, result.SlaCyclesRecorded);
        Assert.Equal(1, result.Breaches);
        Assert.Equal(0, result.Errors);

        var mirrored = await _svc.GetMirroredRequestsAsync(id);
        var request = Assert.Single(mirrored);

        Assert.Equal("SD-4711", request.IssueKey);
        Assert.Equal("Payment gateway is timing out", request.Summary);
        Assert.Equal("Waiting for support", request.StatusName);
        Assert.Equal("Report a system problem", request.RequestTypeName);
        Assert.Equal("Alice Silva", request.ReporterDisplayName);
        Assert.False(request.IsClosed);
        Assert.True(request.AnySlaBreached);

        var sla = Assert.Single(request.Slas);
        Assert.Equal("Time to first response", sla.MetricName);
        Assert.True(sla.Breached);
        Assert.True(sla.IsOngoing);
        Assert.Equal(-5_400_000, sla.RemainingMs);
    }

    /// <summary>
    /// The mirror is an upsert, and the same breach is a breach once.
    ///
    /// Both halves matter. Without the upsert a re-sync appends a second copy of every request, which
    /// is the failure mode of every mirror written without one. And re-notifying every sync for a cycle
    /// that breached last week is how a notification channel gets muted, which is worse than no channel.
    /// </summary>
    [Fact]
    public async Task ASecondSyncUpdatesTheSameRowAndDoesNotReportTheBreachAgain()
    {
        GivenAServiceDesk();
        var id = await ConnectionWithAQueueAsync();

        await _svc.SyncServiceManagementAsync(id, 1);
        var second = await _svc.SyncServiceManagementAsync(id, 1);

        Assert.Equal(1, second.RequestsExamined);
        Assert.Equal(0, second.RequestsCreated);
        Assert.Equal(1, second.RequestsUpdated);
        Assert.Equal(0, second.Breaches);

        Assert.Single(await _svc.GetMirroredRequestsAsync(id));
        Assert.Single((await _svc.GetMirroredRequestAsync(id, "SD-4711")).Slas);
    }

    [Fact]
    public async Task TheBreachedFilterNarrowsTheMirror()
    {
        GivenAServiceDesk();
        var id = await ConnectionWithAQueueAsync();

        await _svc.SyncServiceManagementAsync(id, 1);

        Assert.Single(await _svc.GetMirroredRequestsAsync(id, breachedOnly: true));
    }

    /// <summary>
    /// A key the Service Desk API does not know is skipped, not counted as an error.
    ///
    /// A Jira Software issue on the same site is invisible to that API, and treating each one as a
    /// failure would fill the log with errors for issues NetRisk was never going to mirror.
    /// </summary>
    [Fact]
    public async Task AKeyThatIsNotACustomerRequestIsSkippedQuietly()
    {
        FakeOutboundHttpClient.RuleFor("/queue/10/issue",
            """{ "values": [ { "key": "PROJ-1" } ], "isLastPage": true }""");
        FakeOutboundHttpClient.RuleFor("/request/PROJ-1", "{}", 404);

        var id = await ConnectionWithAQueueAsync();

        var result = await _svc.SyncServiceManagementAsync(id, 1);

        Assert.Equal(0, result.RequestsExamined);
        Assert.Equal(0, result.Errors);
    }

    /// <summary>
    /// SLA is an enrichment, so a failed SLA read costs the cycles and not the status change that came
    /// with it.
    /// </summary>
    [Fact]
    public async Task AFailedSlaReadStillMirrorsTheRequest()
    {
        FakeOutboundHttpClient.RuleFor("/queue/10/issue", QueueIssuesJson);
        FakeOutboundHttpClient.RuleFor("/request/SD-4711/sla", "boom", 500);
        FakeOutboundHttpClient.RuleFor("/request/SD-4711", RequestJson);

        var id = await ConnectionWithAQueueAsync();

        var result = await _svc.SyncServiceManagementAsync(id, 1);

        Assert.Equal(1, result.RequestsCreated);
        Assert.Equal(0, result.SlaCyclesRecorded);
        Assert.Equal(0, result.Errors);
    }

    // --- the Assets register import ---------------------------------------------------------

    private const string AttributesJson = """
        [
          { "id": 231, "name": "Hostname", "label": true,  "type": 0 },
          { "id": 232, "name": "Owner",    "label": false, "type": 2 },
          { "id": 233, "name": "Environment", "label": false, "type": 0 },
          { "id": 234, "name": "Status",   "label": false, "type": 7 },
          { "id": 235, "name": "MAC",      "label": false, "type": 0 }
        ]
        """;

    private const string ObjectsJson = """
        {
          "values": [
            {
              "id": "1042",
              "objectKey": "ITSM-88",
              "label": "srv-prod-01",
              "created": "2026-01-04T10:00:00.000Z",
              "updated": "2026-08-20T14:31:00.000Z",
              "objectType": { "id": 23, "name": "Server" },
              "attributes": [
                { "objectTypeAttributeId": 231, "objectAttributeValues": [ { "displayValue": "srv-prod-01" } ] },
                { "objectTypeAttributeId": 232, "objectAttributeValues": [ { "user": { "displayName": "Alice Silva" } } ] },
                { "objectTypeAttributeId": 233, "objectAttributeValues": [ { "displayValue": "Production" } ] },
                { "objectTypeAttributeId": 234, "objectAttributeValues": [ { "displayValue": "In Production" } ] },
                { "objectTypeAttributeId": 235, "objectAttributeValues": [ { "displayValue": "aa:bb:cc:dd:ee:ff" } ] }
              ]
            }
          ],
          "isLast": true,
          "total": 1
        }
        """;

    private void GivenAnAssetsWorkspace(string objects = ObjectsJson)
    {
        FakeOutboundHttpClient.RuleFor("/rest/servicedeskapi/assets/workspace",
            "{ \"values\": [ { \"workspaceId\": \"" + Workspace + "\" } ], \"isLastPage\": true }");
        FakeOutboundHttpClient.RuleFor("/objecttype/23/attributes", AttributesJson);
        FakeOutboundHttpClient.RuleFor("/object/aql", objects);
    }

    private async Task<int> ConnectionWithAServerMappingAsync(
        JiraAssetTargetKind kind = JiraAssetTargetKind.Host, bool deactivateMissing = false)
    {
        var id = await ConnectionAsync();

        var settings = await _svc.GetSettingsAsync(id);
        settings.AssetsEnabled = true;
        settings.AssetsSchemaId = 5;
        await _svc.SaveSettingsAsync(id, settings);

        await _svc.SetObjectMappingsAsync(id,
        [
            new JiraObjectMappingView
            {
                ObjectTypeId = 23,
                ObjectTypeName = "Server",
                TargetKind = kind,
                Enabled = true,
                CreateMissing = true,
                UpdateExisting = true,
                DeactivateMissing = deactivateMissing,
                MatchStrategy = AssetMatchStrategy.ExternalIdThenIdentity,
                AttributeMappings =
                [
                    new JiraObjectAttributeMappingView
                    {
                        SourceAttributeId = 231, SourceAttributeName = "Hostname",
                        TargetField = MappableFields.Name, IsIdentity = true
                    },
                    new JiraObjectAttributeMappingView
                    {
                        SourceAttributeId = 232, SourceAttributeName = "Owner",
                        TargetField = MappableFields.Owner
                    },
                    new JiraObjectAttributeMappingView
                    {
                        SourceAttributeId = 233, SourceAttributeName = "Environment",
                        TargetField = MappableFields.Environment
                    },
                    new JiraObjectAttributeMappingView
                    {
                        SourceAttributeId = 234, SourceAttributeName = "Status",
                        TargetField = MappableFields.Active,
                        Transform = JiraAttributeTransform.TruthyBoolean
                    },
                    .. kind == JiraAssetTargetKind.Host
                        ? new[]
                        {
                            new JiraObjectAttributeMappingView
                            {
                                SourceAttributeId = 235, SourceAttributeName = "MAC",
                                TargetField = "MacAddress", IsIdentity = true
                            }
                        }
                        : []
                ]
            }
        ], 1);

        return id;
    }

    /// <summary>
    /// The milestone's asset acceptance criterion: a mapped object type imports servers with name,
    /// responsible, environment and active state — and reconciles with the machine NetRisk already
    /// had.
    ///
    /// The seeded host was found by a scanner under a different hostname and has no external id, so the
    /// only thing that can match it is the MAC. That is the case reusing 4.4.2's identity chain exists
    /// for: a name-only match would have made this a second row for the same box.
    /// </summary>
    [Fact]
    public async Task AMappedServerTypeImportsTheFourFieldsOntoTheHostTheScannerAlreadyFound()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(1, result.Examined);
        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Updated);
        Assert.Equal(0, result.Errors);

        var imported = Assert.Single(await _svc.GetAssetObjectsAsync(id));

        Assert.Equal("srv-prod-01", imported.MappedName);
        Assert.Equal("Alice Silva", imported.MappedOwner);
        Assert.Equal("Production", imported.MappedEnvironment);
        Assert.True(imported.MappedActive);
        Assert.Equal("mac", imported.MatchReason);
        Assert.Equal(1, imported.TargetHostId);

        Read(ctx =>
        {
            var host = ctx.Hosts.Single(h => h.Id == 1);

            Assert.Equal("srv-prod-01", host.HostName);
            Assert.Equal("Production", host.Environment);
            Assert.Equal("Alice Silva", host.Owner);
            Assert.Equal(JiraIntegrationService.ExternalProvider, host.ExternalProvider);
            Assert.Equal("1042", host.ExternalId);
            // The active state maps onto the status the hosts screen already renders, not onto a
            // parallel boolean that could disagree with it.
            Assert.Equal((short)IntStatus.Active, host.Status);
            // Not in the mapping, so it is left alone rather than cleared: "the register says
            // nothing" is not the same statement as "the register says empty".
            Assert.Equal("10.0.0.5", host.Ip);
        });
    }

    [Fact]
    public async Task ASecondImportUpdatesTheSameHostRatherThanCreatingAnother()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);
        var second = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Updated);

        Read(ctx => Assert.Single(ctx.Hosts));
        Assert.Single(await _svc.GetAssetObjectsAsync(id));
    }

    /// <summary>
    /// The dry run is the same code path with the writes skipped, and it writes nothing at all — not
    /// the host, not even the audit row. A preview that leaves a trail is a preview nobody trusts.
    /// </summary>
    [Fact]
    public async Task ADryRunReportsWhatWouldHappenAndWritesNothing()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        var result = await _svc.ImportAssetsAsync(id, dryRun: true, 1);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.Examined);
        Assert.Equal(1, result.Updated);

        var sample = Assert.Single(result.Sample);
        Assert.Equal("srv-prod-01", sample.MappedName);
        Assert.Equal("Production", sample.MappedEnvironment);

        Assert.Empty(await _svc.GetAssetObjectsAsync(id));

        Read(ctx =>
        {
            var host = ctx.Hosts.Single(h => h.Id == 1);
            Assert.Equal("srv-old-name", host.HostName);
            Assert.Null(host.Environment);
            Assert.Null(host.ExternalId);
        });
    }

    /// <summary>
    /// The workspace id is discovered from the site and cached, because it cannot be derived from the
    /// site URL and a typed one produces 404s from <c>api.atlassian.com</c> that read as bad
    /// credentials.
    /// </summary>
    [Fact]
    public async Task TheAssetsWorkspaceIsDiscoveredAndCached()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        await _svc.ImportAssetsAsync(id, dryRun: true, 1);

        Assert.Equal(Workspace, (await _svc.GetSettingsAsync(id)).AssetsWorkspaceId);

        // And every Assets call goes to Atlassian's API host, not to the connection's own site.
        Assert.Contains(FakeOutboundHttpClient.Requests,
            r => r.Url.StartsWith("https://api.atlassian.com/jsm/assets/workspace/"));
    }

    /// <summary>
    /// An object the mapping cannot name produces an audit row saying so rather than a silent skip.
    ///
    /// Without the row, "why is that server not in NetRisk" has no answer except re-running the import
    /// and watching.
    /// </summary>
    [Fact]
    public async Task AnObjectThatProducesNoNameIsRecordedAsAnError()
    {
        GivenAnAssetsWorkspace("""
            {
              "values": [
                {
                  "id": "2000",
                  "objectKey": "ITSM-99",
                  "objectType": { "id": 23, "name": "Server" },
                  "attributes": []
                }
              ],
              "isLast": true
            }
            """);

        var id = await ConnectionWithAServerMappingAsync();

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(1, result.Examined);
        Assert.Equal(1, result.Errors);
        Assert.Equal(0, result.Created);

        var audited = Assert.Single(await _svc.GetAssetObjectsAsync(id));
        Assert.Contains("no name", audited.ImportError);
    }

    /// <summary>
    /// Retiring is opt-in.
    ///
    /// A typo in an AQL filter returns nothing, and an import that decommissions production on a typo
    /// is worse than one that leaves a stale row — so with the flag off the host that the register no
    /// longer returns stays exactly as it was.
    /// </summary>
    [Fact]
    public async Task WithRetireOffAnObjectTheRegisterNoLongerReturnsIsLeftAlone()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        FakeOutboundHttpClient.Reset();
        GivenAnAssetsWorkspace("""{ "values": [], "isLast": true, "total": 0 }""");

        var second = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(0, second.Examined);
        Assert.Equal(0, second.Deactivated);

        Read(ctx => Assert.Equal((short)IntStatus.Active, ctx.Hosts.Single(h => h.Id == 1).Status));
    }

    [Fact]
    public async Task WithRetireOnAnObjectTheRegisterNoLongerReturnsIsRetired()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync(deactivateMissing: true);

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        FakeOutboundHttpClient.Reset();
        GivenAnAssetsWorkspace("""{ "values": [], "isLast": true, "total": 0 }""");

        var second = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(1, second.Deactivated);

        Read(ctx => Assert.Equal((short)IntStatus.Retired, ctx.Hosts.Single(h => h.Id == 1).Status));
    }

    /// <summary>
    /// An object type mapped to an application becomes an <c>application</c> entity, and an owner that
    /// matches no person entity is reported rather than invented.
    ///
    /// Creating a person row from a CMDB string is how a directory fills up with near-duplicates of
    /// real people, so the value is kept on the import row and the operator is told.
    /// </summary>
    [Fact]
    public async Task AnApplicationTypeBecomesAnEntityAndAnUnmatchedResponsibleIsReported()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync(JiraAssetTargetKind.ApplicationEntity);

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Errors);
        Assert.Contains("no person entity matches", string.Join(" ", result.Messages));

        var imported = Assert.Single(await _svc.GetAssetObjectsAsync(id));
        Assert.NotNull(imported.TargetEntityId);
        // The owner is still recorded, so the information the register carried is not lost.
        Assert.Equal("Alice Silva", imported.MappedOwner);

        Read(ctx =>
        {
            var entity = ctx.Entities.Single(e => e.DefinitionName == "application");
            var properties = ctx.EntitiesProperties.Where(p => p.Entity == entity.Id).ToList();

            Assert.Equal("srv-prod-01", properties.Single(p => p.Type == "name").Value);
            Assert.Equal("Production", properties.Single(p => p.Type == "environment").Value);
            Assert.Equal("True", properties.Single(p => p.Type == "active").Value);
            Assert.DoesNotContain(properties, p => p.Type == "responsible");
        });
    }

    /// <summary>
    /// Re-importing an application updates its properties instead of adding a second <c>name</c>.
    ///
    /// A blind create would do exactly that on every run, and the definition's single-valued rule would
    /// then start refusing the import that had been working.
    /// </summary>
    [Fact]
    public async Task ASecondApplicationImportUpdatesThePropertiesRatherThanDuplicatingThem()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync(JiraAssetTargetKind.ApplicationEntity);

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        FakeOutboundHttpClient.Reset();
        GivenAnAssetsWorkspace(ObjectsJson.Replace("Production", "Homolog"));

        var second = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Updated);

        Read(ctx =>
        {
            var entity = Assert.Single(ctx.Entities, e => e.DefinitionName == "application");
            var properties = ctx.EntitiesProperties.Where(p => p.Entity == entity.Id).ToList();

            Assert.Single(properties, p => p.Type == "name");
            Assert.Equal("Homolog", properties.Single(p => p.Type == "environment").Value);
        });
    }

    /// <summary>
    /// The imported row carries a link to the object's page on the Jira site.
    ///
    /// Keyed on the object *key* and not the numeric id: Atlassian's documentation for this route says
    /// so, and the id is the plausible wrong guess — it appears in the payload right beside the key and
    /// produces a URL that looks correct and 404s.
    /// </summary>
    [Fact]
    public async Task AnImportedObjectLinksBackToItsPageOnTheJiraSite()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        var imported = Assert.Single(await _svc.GetAssetObjectsAsync(id));

        Assert.Equal("https://acme.atlassian.net/jira/servicedesk/assets/object/ITSM-88",
            imported.ObjectUrl);
    }

    /// <summary>The dry run's sample carries the same link, so the preview offers what the import will.</summary>
    [Fact]
    public async Task ADryRunsSampleAlsoCarriesTheLink()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        var result = await _svc.ImportAssetsAsync(id, dryRun: true, 1);

        Assert.Equal("https://acme.atlassian.net/jira/servicedesk/assets/object/ITSM-88",
            Assert.Single(result.Sample).ObjectUrl);
    }

    /// <summary>
    /// An object with no key gets no link rather than a URL that would 404.
    ///
    /// Assets always assigns a key in practice, but the field is optional in the payload, and a button
    /// that reliably fails is worse than a plain cell.
    /// </summary>
    [Fact]
    public async Task AnObjectWithNoKeyGetsNoLink()
    {
        GivenAnAssetsWorkspace(ObjectsJson.Replace("\"objectKey\": \"ITSM-88\",", ""));

        var id = await ConnectionWithAServerMappingAsync();

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        var imported = Assert.Single(await _svc.GetAssetObjectsAsync(id));

        Assert.Null(imported.ObjectKey);
        Assert.Null(imported.ObjectUrl);
    }

    /// <summary>
    /// The link is built from the connection's base URL on read, not stored.
    ///
    /// So a site that is renamed does not leave every previously imported row pointing at the old
    /// host — which a stored URL would.
    /// </summary>
    [Fact]
    public async Task TheLinkFollowsTheConnectionsBaseUrlWhenItChanges()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        var connection = await _trackers.GetConnectionAsync(id);

        await _trackers.UpdateConnectionAsync(new IssueTrackerConnection
        {
            Id = id,
            Name = connection.Name,
            Provider = IssueTrackerProviderKind.Jira,
            BaseUrl = "https://renamed.atlassian.net",
            ProjectKey = connection.ProjectKey,
            AuthUser = connection.AuthUser,
            Enabled = true,
            PollIntervalMinutes = 15
        }, null, null, 1);

        var imported = Assert.Single(await _svc.GetAssetObjectsAsync(id));

        Assert.Equal("https://renamed.atlassian.net/jira/servicedesk/assets/object/ITSM-88",
            imported.ObjectUrl);
    }

    /// <summary>An object key with a character that needs escaping does not produce a broken URL.</summary>
    [Theory]
    [InlineData("ITSM-88", "ITSM-88")]
    [InlineData("IT SM/88", "IT%20SM%2F88")]
    public void TheObjectKeyIsEscapedIntoTheUrl(string key, string expected)
    {
        Assert.Equal($"https://acme.atlassian.net/jira/servicedesk/assets/object/{expected}",
            JiraIntegrationService.AssetObjectUrl("https://acme.atlassian.net", key));
    }

    [Fact]
    public void NoBaseUrlOrNoKeyMeansNoUrl()
    {
        Assert.Null(JiraIntegrationService.AssetObjectUrl(null, "ITSM-88"));
        Assert.Null(JiraIntegrationService.AssetObjectUrl("https://acme.atlassian.net", null));
        Assert.Null(JiraIntegrationService.AssetObjectUrl("https://acme.atlassian.net", "  "));
    }

    /// <summary>A trailing slash on the base URL does not produce a double slash in the path.</summary>
    [Fact]
    public void ATrailingSlashOnTheBaseUrlIsNotDoubled()
    {
        Assert.Equal("https://acme.atlassian.net/jira/servicedesk/assets/object/ITSM-88",
            JiraIntegrationService.AssetObjectUrl("https://acme.atlassian.net/", "ITSM-88"));
    }

    [Fact]
    public async Task TheImportRecordsAnIntegrationSyncLogRow()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Read(ctx =>
        {
            var log = Assert.Single(ctx.IntegrationSyncLogs,
                l => l.Integration == IntegrationKind.JiraAssets);

            Assert.Equal(IntegrationSyncStatus.Succeeded, log.Status);
            Assert.Equal(1, log.UpdatedCount);
        });
    }

    [Fact]
    public async Task ADryRunDoesNotRecordASyncLogRow()
    {
        GivenAnAssetsWorkspace();
        var id = await ConnectionWithAServerMappingAsync();

        await _svc.ImportAssetsAsync(id, dryRun: true, 1);

        Read(ctx => Assert.DoesNotContain(ctx.IntegrationSyncLogs,
            l => l.Integration == IntegrationKind.JiraAssets));
    }

    [Fact]
    public async Task TheMirrorRecordsAnIntegrationSyncLogRow()
    {
        GivenAServiceDesk();
        var id = await ConnectionWithAQueueAsync();

        await _svc.SyncServiceManagementAsync(id, 1);

        Read(ctx =>
        {
            var log = Assert.Single(ctx.IntegrationSyncLogs,
                l => l.Integration == IntegrationKind.JiraServiceManagement);

            Assert.Equal(IntegrationSyncStatus.Succeeded, log.Status);
            Assert.Equal(1, log.CreatedCount);
        });
    }
}

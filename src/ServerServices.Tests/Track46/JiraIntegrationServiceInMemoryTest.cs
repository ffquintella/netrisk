using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Integrations.IssueTrackers.Jira;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track46;

/// <summary>
/// Jira Service Management configuration, the request mirror, and the Assets register import
/// (Track 4 milestone 4.6).
///
/// The milestone's acceptance criteria are here: a configured queue populates the mirror with each
/// request's SLA, an Assets object type mapped to <c>Host</c> imports servers with name, responsible,
/// environment and active state, and re-importing updates the same row rather than creating a second
/// one. So are the guards that make the mapping editor trustworthy — every configuration that would be
/// saved and then silently do nothing is refused with a sentence.
/// </summary>
[TestSubject(typeof(JiraIntegrationService))]
public class JiraIntegrationServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IJiraIntegrationService _svc;
    private readonly IIssueTrackerService _trackers;

    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    public JiraIntegrationServiceInMemoryTest()
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
        });
    }

    private async Task<int> JiraConnectionAsync(string name = "Service desk")
    {
        var created = await _trackers.CreateConnectionAsync(new IssueTrackerConnection
        {
            Name = name,
            Provider = IssueTrackerProviderKind.Jira,
            BaseUrl = "https://acme.atlassian.net",
            ProjectKey = "SD",
            AuthUser = "bot@acme.com",
            Enabled = true,
            PollIntervalMinutes = 15
        }, "api-token", "webhook-secret", 1);

        return created.Id;
    }

    private async Task<int> GitHubConnectionAsync()
    {
        var created = await _trackers.CreateConnectionAsync(new IssueTrackerConnection
        {
            Name = "Product GitHub",
            Provider = IssueTrackerProviderKind.GitHub,
            BaseUrl = "https://api.github.com",
            ProjectKey = "acme/product",
            Enabled = true,
            PollIntervalMinutes = 15
        }, "pat", null, 1);

        return created.Id;
    }

    private static JiraObjectMappingView ServerMapping(
        JiraAssetTargetKind kind = JiraAssetTargetKind.Host,
        params JiraObjectAttributeMappingView[] extra)
    {
        var mapping = new JiraObjectMappingView
        {
            ObjectTypeId = 23,
            ObjectTypeName = "Server",
            TargetKind = kind,
            Enabled = true,
            CreateMissing = true,
            UpdateExisting = true,
            AttributeMappings =
            [
                new JiraObjectAttributeMappingView
                {
                    SourceAttributeId = 231, SourceAttributeName = "Hostname",
                    TargetField = MappableFields.Name, IsIdentity = true
                }
            ]
        };

        mapping.AttributeMappings.AddRange(extra);

        return mapping;
    }

    // --- the connection facet ---------------------------------------------------------------

    /// <summary>
    /// The facet is created on first read.
    ///
    /// So that no caller and no screen carries a "not configured yet" branch — that branch is a second
    /// layout only the first operator to open the tab ever sees, which is where the bugs live.
    /// </summary>
    [Fact]
    public async Task ReadingTheSettingsOfAFreshConnectionCreatesThemWithDefaults()
    {
        var id = await JiraConnectionAsync();

        var settings = await _svc.GetSettingsAsync(id);

        Assert.Equal(id, settings.ConnectionId);
        Assert.Equal(JiraDeployment.Cloud, settings.Deployment);
        Assert.False(settings.JsmEnabled);
        Assert.False(settings.AssetsEnabled);
        Assert.True(settings.ImportSlas);
        Assert.Empty(settings.QueueImports);
    }

    [Fact]
    public async Task TheQueueSelectionIsReplacedWholesale()
    {
        var id = await JiraConnectionAsync();

        var settings = await _svc.GetSettingsAsync(id);
        settings.JsmEnabled = true;
        settings.ServiceDeskId = 3;
        settings.QueueImports =
        [
            new JiraQueueImportView { ServiceDeskId = 3, QueueId = 10, QueueName = "Open", MaxRequests = 50 },
            new JiraQueueImportView { ServiceDeskId = 3, QueueId = 11, QueueName = "Escalated" }
        ];

        var saved = await _svc.SaveSettingsAsync(id, settings);
        Assert.Equal(2, saved.QueueImports.Count);

        saved.QueueImports = [saved.QueueImports.First(q => q.QueueId == 11)];

        var second = await _svc.SaveSettingsAsync(id, saved);

        // Wholesale, because the selection is edited as a checkbox list: a per-row save leaves a
        // selection that is neither the old one nor the one the operator chose.
        Assert.Single(second.QueueImports);
        Assert.Equal(11, second.QueueImports[0].QueueId);
    }

    [Fact]
    public async Task APerQueueCeilingIsClampedRatherThanTrusted()
    {
        var id = await JiraConnectionAsync();

        var settings = await _svc.GetSettingsAsync(id);
        settings.ServiceDeskId = 3;
        settings.QueueImports =
        [
            new JiraQueueImportView { ServiceDeskId = 3, QueueId = 10, MaxRequests = 0 },
            new JiraQueueImportView { ServiceDeskId = 3, QueueId = 11, MaxRequests = 1_000_000 }
        ];

        var saved = await _svc.SaveSettingsAsync(id, settings);

        // A 0 would import nothing and a million would hold a job for an hour, and neither is what
        // the operator who typed it meant.
        Assert.Equal(1, saved.QueueImports.Single(q => q.QueueId == 10).MaxRequests);
        Assert.Equal(5000, saved.QueueImports.Single(q => q.QueueId == 11).MaxRequests);
    }

    /// <summary>
    /// Service Management and Assets are Jira features, and a connection to another tracker is refused
    /// rather than answered with an empty configuration screen.
    /// </summary>
    [Fact]
    public async Task ANonJiraConnectionIsRefused()
    {
        var id = await GitHubConnectionAsync();

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.GetServiceDesksAsync(id));

        Assert.Contains("GitHub", ex.Message);
    }

    /// <summary>
    /// Data Center is recognised and refused, not half-served.
    ///
    /// Its Assets equivalent is Insight at <c>/rest/insight/1.0/</c> with a different object model, so
    /// pointing the Cloud client at it produces 404s from <c>api.atlassian.com</c> that read as "your
    /// credentials are wrong" — and an operator would rotate a token that was never the problem.
    /// </summary>
    [Fact]
    public async Task ADataCenterConnectionIsRefusedWithAReasonRatherThanFailingUpstream()
    {
        var id = await JiraConnectionAsync();

        var settings = await _svc.GetSettingsAsync(id);
        settings.Deployment = JiraDeployment.DataCenter;
        await _svc.SaveSettingsAsync(id, settings);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.GetAssetSchemasAsync(id));

        Assert.Contains("Data Center", ex.Message);
    }

    [Fact]
    public async Task AnUnknownConnectionIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetSettingsAsync(9999));
    }

    // --- field mapping guards ---------------------------------------------------------------

    [Fact]
    public async Task AFieldMappingRoundTrips()
    {
        var id = await JiraConnectionAsync();

        var saved = await _svc.SetFieldMappingsAsync(id,
        [
            new JiraFieldMappingView
            {
                Direction = JiraFieldMappingDirection.Outbound,
                NetRiskField = "Severity",
                JiraFieldId = "customfield_10012",
                JiraFieldName = "Security severity",
                JiraFieldType = "option",
                Enabled = true
            }
        ]);

        var mapping = Assert.Single(saved);
        Assert.Equal("Severity", mapping.NetRiskField);
        Assert.Equal("customfield_10012", mapping.JiraFieldId);
        Assert.Equal("Security severity", mapping.JiraFieldName);
    }

    /// <summary>
    /// A source the mapper does not understand is refused, and the message lists what is available.
    ///
    /// Stored-and-skipped is the failure this guard exists for: the operator sees "saved" and then a
    /// ticket with a blank custom field, and nothing anywhere says why.
    /// </summary>
    [Fact]
    public async Task AFieldMappingWithAnUnknownNetRiskSourceIsRefused()
    {
        var id = await JiraConnectionAsync();

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.SetFieldMappingsAsync(id,
            [
                new JiraFieldMappingView { NetRiskField = "Whatever", JiraFieldId = "customfield_1" }
            ]));

        Assert.Contains("Whatever", ex.Message);
        Assert.Contains("Severity", ex.Message);
    }

    [Fact]
    public async Task AFieldMappingThatWouldWriteNothingIsRefused()
    {
        var id = await JiraConnectionAsync();

        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.SetFieldMappingsAsync(id,
                [new JiraFieldMappingView { NetRiskField = "", JiraFieldId = "customfield_1" }]));
    }

    [Fact]
    public async Task AnEmptySourceWithAConstantIsAcceptedBecauseItWritesTheConstant()
    {
        var id = await JiraConnectionAsync();

        var saved = await _svc.SetFieldMappingsAsync(id,
        [
            new JiraFieldMappingView
            {
                NetRiskField = "", JiraFieldId = "customfield_9", ConstantValue = "Platform"
            }
        ]);

        Assert.Equal("Platform", Assert.Single(saved).ConstantValue);
    }

    [Fact]
    public async Task TwoMappingsOnOneJiraFieldAreRefused()
    {
        var id = await JiraConnectionAsync();

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.SetFieldMappingsAsync(id,
            [
                new JiraFieldMappingView { NetRiskField = "Severity", JiraFieldId = "customfield_1" },
                new JiraFieldMappingView { NetRiskField = "Cvss", JiraFieldId = "customfield_1" }
            ]));

        // One writer per field, or the result depends on row order — which gets reported as "the
        // integration is flaky".
        Assert.Contains("customfield_1", ex.Message);
    }

    // --- object mapping guards --------------------------------------------------------------

    [Fact]
    public async Task AnObjectMappingRoundTripsWithItsAttributeRows()
    {
        var id = await JiraConnectionAsync();

        var saved = await _svc.SetObjectMappingsAsync(id,
        [
            ServerMapping(extra:
            [
                new JiraObjectAttributeMappingView
                {
                    SourceAttributeId = 232, SourceAttributeName = "Owner",
                    TargetField = MappableFields.Owner
                },
                new JiraObjectAttributeMappingView
                {
                    SourceAttributeId = 233, SourceAttributeName = "Environment",
                    TargetField = MappableFields.Environment
                }
            ])
        ], 1);

        var mapping = Assert.Single(saved);
        Assert.Equal("Server", mapping.ObjectTypeName);
        Assert.Equal(JiraAssetTargetKind.Host, mapping.TargetKind);
        Assert.Equal(3, mapping.AttributeMappings.Count);
        // Sort order is assigned on save, so the grid does not reshuffle itself every time.
        Assert.Equal([0, 1, 2], mapping.AttributeMappings.Select(a => a.SortOrder));
    }

    /// <summary>
    /// A mapping with no Name target matches nothing and creates rows with no name, so it is refused
    /// rather than stored and then reporting zero on every import.
    /// </summary>
    [Fact]
    public async Task AMappingWithNoNameTargetIsRefused()
    {
        var id = await JiraConnectionAsync();

        var mapping = ServerMapping();
        mapping.AttributeMappings =
        [
            new JiraObjectAttributeMappingView
            {
                SourceAttributeName = "Environment", TargetField = MappableFields.Environment
            }
        ];

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.SetObjectMappingsAsync(id, [mapping], 1));

        Assert.Contains("Name", ex.Message);
    }

    /// <summary>
    /// A target field that does not exist for the kind is skipped by the projector, so the editor is
    /// not allowed to save one. <c>MacAddress</c> on an application is the case.
    /// </summary>
    [Fact]
    public async Task ATargetFieldThatDoesNotExistForTheKindIsRefused()
    {
        var id = await JiraConnectionAsync();

        var mapping = ServerMapping(JiraAssetTargetKind.ApplicationEntity, new JiraObjectAttributeMappingView
        {
            SourceAttributeName = "MAC", TargetField = "MacAddress"
        });

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.SetObjectMappingsAsync(id, [mapping], 1));

        Assert.Contains("MacAddress", ex.Message);
        Assert.Contains("ApplicationEntity", ex.Message);
    }

    [Fact]
    public async Task TwoAttributesWritingOneFieldAreRefused()
    {
        var id = await JiraConnectionAsync();

        var mapping = ServerMapping(extra:
        [
            new JiraObjectAttributeMappingView
            {
                SourceAttributeName = "Env", TargetField = MappableFields.Environment
            },
            new JiraObjectAttributeMappingView
            {
                SourceAttributeName = "Zone", TargetField = MappableFields.Environment
            }
        ]);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.SetObjectMappingsAsync(id, [mapping], 1));

        // Otherwise the imported value depends on which row the importer happened to read last.
        Assert.Contains("environment", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnAttributeRowThatReadsNothingIsRefused()
    {
        var id = await JiraConnectionAsync();

        var mapping = ServerMapping(extra: new JiraObjectAttributeMappingView
        {
            SourceAttributeName = "", SourceAttributeId = null, TargetField = MappableFields.Owner
        });

        await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.SetObjectMappingsAsync(id, [mapping], 1));
    }

    [Fact]
    public async Task MappingOneObjectTypeTwiceIsRefused()
    {
        var id = await JiraConnectionAsync();

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(
            () => _svc.SetObjectMappingsAsync(id, [ServerMapping(), ServerMapping()], 1));

        Assert.Contains("23", ex.Message);
    }

    // --- the published field catalog ---------------------------------------------------------

    /// <summary>
    /// The catalog is served rather than duplicated in the client, so the picker cannot offer a target
    /// the projector does not implement.
    /// </summary>
    [Fact]
    public void TheMappableFieldCatalogIsPerTargetKind()
    {
        var host = _svc.GetMappableFields(JiraAssetTargetKind.Host).Select(f => f.Name).ToList();
        var app = _svc.GetMappableFields(JiraAssetTargetKind.ApplicationEntity)
            .Select(f => f.Name).ToList();

        foreach (var common in new[]
                 {
                     MappableFields.Name, MappableFields.Owner, MappableFields.Environment,
                     MappableFields.Active
                 })
        {
            Assert.Contains(common, host);
            Assert.Contains(common, app);
        }

        Assert.Contains("MacAddress", host);
        Assert.DoesNotContain("MacAddress", app);
        Assert.Contains("Technology", app);
        Assert.DoesNotContain("Technology", host);
    }

    [Fact]
    public void TheCatalogWithNoKindOffersEverything()
    {
        var all = _svc.GetMappableFields(null).Select(f => f.Name).ToList();

        Assert.Contains("MacAddress", all);
        Assert.Contains("Technology", all);
    }

    // --- disabled facets do nothing ----------------------------------------------------------

    [Fact]
    public async Task SyncingAConnectionWithServiceManagementOffDoesNothing()
    {
        var id = await JiraConnectionAsync();

        var result = await _svc.SyncServiceManagementAsync(id);

        Assert.Equal(0, result.RequestsExamined);
        Assert.Contains("not enabled", string.Join(" ", result.Messages));
    }

    [Fact]
    public async Task ImportingWithAssetsOffDoesNothing()
    {
        var id = await JiraConnectionAsync();

        var result = await _svc.ImportAssetsAsync(id, dryRun: true);

        Assert.Equal(0, result.Examined);
        Assert.Contains("not enabled", string.Join(" ", result.Messages));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
/// M58, T291 (S39 §3.2 CMDB-INET / CMDB-REL): the Jira Assets import maps an internet-exposure attribute onto
/// <c>hosts.internet_facing</c> and the application ↔ server relation onto <c>host_application_links</c> — written as
/// <c>cmdb</c>, never over a manual value or link, an unrecognised value or reference reported instead of guessed, and the
/// relations resolved after every mapping ran. All calls go through the fake HTTP client.
/// </summary>
[TestSubject(typeof(JiraIntegrationService))]
public class JiraAssetsExposureAndRelationsInMemoryTest : InMemoryServiceTestBase
{
    private const int ServerType = 23, ApplicationType = 24;
    private const int ManualCrm = 900, SeededPortal = 901;
    private const string Workspace = "b2c3d4e5-0000-1111-2222-333344445555";

    private readonly IJiraIntegrationService _svc;
    private readonly IIssueTrackerService _trackers;

    public JiraAssetsExposureAndRelationsInMemoryTest()
    {
        _svc = GetService<IJiraIntegrationService>();
        _trackers = GetService<IIssueTrackerService>();

        Seed(ctx => ctx.Users.Add(new User
        {
            Value = 1, Name = "analyst", Login = "analyst", Enabled = true, Type = "local",
            Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = "analyst@acme.com"
        }));
    }

    // --- payloads ----------------------------------------------------------------------------

    private static string Attribute(int id, string value) =>
        $$"""{ "objectTypeAttributeId": {{id}}, "objectAttributeValues": [ { "displayValue": {{JsonSerializer.Serialize(value)}} } ] }""";

    private static string Reference(int id, params (string Id, string Key, string Label)[] targets)
    {
        var values = targets.Select(t =>
            $$"""{ "displayValue": "{{t.Label}}", "referencedObject": { "id": "{{t.Id}}", "objectKey": "{{t.Key}}", "label": "{{t.Label}}" } }""");
        return $$"""{ "objectTypeAttributeId": {{id}}, "objectAttributeValues": [ {{string.Join(", ", values)}} ] }""";
    }

    private static string Objects(params string[] objects) =>
        $$"""{ "values": [ {{string.Join(", ", objects)}} ], "isLast": true, "total": {{objects.Length}} }""";

    private static string Server(string? exposure = null, params (string Id, string Key, string Label)[] applications)
    {
        var attributes = new List<string> { Attribute(231, "srv-prod-01") };
        if (exposure != null) attributes.Add(Attribute(236, exposure));
        if (applications.Length > 0) attributes.Add(Reference(237, applications));

        return $$"""
            { "id": "1042", "objectKey": "ITSM-88", "label": "srv-prod-01", "objectType": { "id": {{ServerType}}, "name": "Server" },
              "attributes": [ {{string.Join(", ", attributes)}} ] }
            """;
    }

    private static string Application(string id, string key, string name, string? hostName = null)
    {
        var attributes = new List<string> { Attribute(241, name) };
        if (hostName != null) attributes.Add(Attribute(242, hostName));

        return $$"""
            { "id": "{{id}}", "objectKey": "{{key}}", "label": "{{name}}", "objectType": { "id": {{ApplicationType}}, "name": "Application" },
              "attributes": [ {{string.Join(", ", attributes)}} ] }
            """;
    }

    private static readonly (string, string, string) Portal = ("2001", "ITSM-201", "Portal");
    private static readonly (string, string, string) Billing = ("2002", "ITSM-202", "Billing");

    private string DefaultApplications => Objects(Application("2001", "ITSM-201", "Portal"), Application("2002", "ITSM-202", "Billing"));

    private void GivenTheRegister(string servers, string? applications = null)
    {
        FakeOutboundHttpClient.Reset();
        FakeOutboundHttpClient.RuleFor("/rest/servicedeskapi/assets/workspace",
            "{ \"values\": [ { \"workspaceId\": \"" + Workspace + "\" } ], \"isLastPage\": true }");
        FakeOutboundHttpClient.RuleFor($"/objecttype/{ServerType}/attributes", """
            [ { "id": 231, "name": "Hostname", "type": 0 }, { "id": 236, "name": "Exposure", "type": 0 },
              { "id": 237, "name": "Applications", "type": 1 } ]
            """);
        FakeOutboundHttpClient.RuleFor($"/objecttype/{ApplicationType}/attributes", """
            [ { "id": 241, "name": "Name", "type": 0 }, { "id": 242, "name": "Runs on", "type": 0 } ]
            """);
        // The two object types share the AQL route; the query in the body tells them apart.
        FakeOutboundHttpClient.Rules.Add((r => r.Url.Contains("/object/aql") && (r.Body ?? "").Contains("Application"),
            new OutboundHttpResponse { StatusCode = 200, Body = applications ?? DefaultApplications }));
        FakeOutboundHttpClient.RuleFor("/object/aql", servers);
    }

    private async Task<int> ConnectionAsync(bool serverRelation = true, bool applicationRelation = false,
        string name = "Service desk")
    {
        var created = await _trackers.CreateConnectionAsync(new IssueTrackerConnection
        {
            Name = name, Provider = IssueTrackerProviderKind.Jira, BaseUrl = "https://acme.atlassian.net",
            ProjectKey = "SD", AuthUser = "bot@acme.com", Enabled = true, PollIntervalMinutes = 15
        }, "api-token", null, 1);

        var settings = await _svc.GetSettingsAsync(created.Id);
        settings.AssetsEnabled = true;
        settings.AssetsSchemaId = 5;
        await _svc.SaveSettingsAsync(created.Id, settings);

        var server = new List<JiraObjectAttributeMappingView>
        {
            new() { SourceAttributeId = 231, SourceAttributeName = "Hostname", TargetField = MappableFields.Name, IsIdentity = true },
            new() { SourceAttributeId = 236, SourceAttributeName = "Exposure", TargetField = MappableFields.InternetFacing }
        };
        if (serverRelation)
            server.Add(new() { SourceAttributeId = 237, SourceAttributeName = "Applications", TargetField = MappableFields.Applications });

        var application = new List<JiraObjectAttributeMappingView>
        {
            new() { SourceAttributeId = 241, SourceAttributeName = "Name", TargetField = MappableFields.Name }
        };
        if (applicationRelation)
            application.Add(new() { SourceAttributeId = 242, SourceAttributeName = "Runs on", TargetField = MappableFields.Hosts });

        // The server type first: its relations name applications the second mapping has not imported yet.
        await _svc.SetObjectMappingsAsync(created.Id,
        [
            Mapping(ServerType, "Server", JiraAssetTargetKind.Host, server),
            Mapping(ApplicationType, "Application", JiraAssetTargetKind.ApplicationEntity, application)
        ], 1);

        return created.Id;
    }

    private static JiraObjectMappingView Mapping(int type, string name, JiraAssetTargetKind kind,
        List<JiraObjectAttributeMappingView> attributes) => new()
    {
        ObjectTypeId = type, ObjectTypeName = name, TargetKind = kind, Enabled = true, CreateMissing = true,
        UpdateExisting = true, MatchStrategy = AssetMatchStrategy.ExternalIdThenIdentity, AttributeMappings = attributes
    };

    // --- helpers -----------------------------------------------------------------------------

    private T Read<T>(Func<DAL.Context.AuditableContext, T> read)
    {
        using var context = OpenContext();
        return read(context);
    }

    private Host TheServer() => Read(ctx => ctx.Hosts.Single(h => h.HostName == "srv-prod-01"));

    private int ApplicationId(string name) => Read(ctx => ctx.EntitiesProperties
        .Where(p => p.Type == "name" && p.Value == name)
        .Join(ctx.Entities.Where(e => e.DefinitionName == "application"), p => p.Entity, e => e.Id, (_, e) => e.Id)
        .Single());

    private List<HostApplicationLink> Links() => Read(ctx => ctx.HostApplicationLinks.OrderBy(l => l.EntityId).ToList());

    private void SeedApplication(int id, string name) => Seed(ctx =>
    {
        ctx.Entities.Add(new Entity
        {
            Id = id, DefinitionName = "application", DefinitionVersion = "2.5", Status = "active",
            Created = DateTime.UtcNow, Updated = DateTime.UtcNow
        });
        ctx.EntitiesProperties.Add(new EntitiesProperty
            { Id = id * 10, Entity = id, Type = "name", Name = "name", Value = name, OldValue = "" });
    });

    private void SeedServer(Action<Host> configure) => Seed(ctx =>
    {
        var host = new Host
        {
            Id = 1, HostName = "srv-prod-01", Source = "nessus", Status = (short)IntStatus.Active,
            RegistrationDate = DateTime.UtcNow.AddYears(-1)
        };
        configure(host);
        ctx.Hosts.Add(host);
    });

    // --- the mapping vocabulary --------------------------------------------------------------

    [Fact]
    public void TestTheExposureAndRelationTargetsAreOfferedOnTheirKindOnly()
    {
        Assert.True(MappableFields.IsValidAssetTarget(JiraAssetTargetKind.Host, MappableFields.InternetFacing));
        Assert.True(MappableFields.IsValidAssetTarget(JiraAssetTargetKind.Host, MappableFields.Applications));
        Assert.True(MappableFields.IsValidAssetTarget(JiraAssetTargetKind.ApplicationEntity, MappableFields.Hosts));

        Assert.False(MappableFields.IsValidAssetTarget(JiraAssetTargetKind.ApplicationEntity, MappableFields.InternetFacing));
        Assert.False(MappableFields.IsValidAssetTarget(JiraAssetTargetKind.ApplicationEntity, MappableFields.Applications));
        Assert.False(MappableFields.IsValidAssetTarget(JiraAssetTargetKind.Host, MappableFields.Hosts));
    }

    [Theory]
    [InlineData("Public", true)]
    [InlineData(" yes ", true)]
    [InlineData("DMZ", true)]
    [InlineData("true", true)]
    [InlineData("Internal", false)]
    [InlineData("não", false)]
    [InlineData("false", false)]
    [InlineData("Unknown", null)]
    [InlineData("In Production", null)]
    [InlineData("", null)]
    public void TestTheExposureVocabularyReadsYesNoOrNothing(string raw, bool? expected)
    {
        Assert.Equal(expected, AssetAttributeProjector.ParseInternetFacing(raw));
    }

    [Fact]
    public void TestTheParserKeepsTheReferencedObjectsBesideTheirLabels()
    {
        using var document = JsonDocument.Parse(Server(null, Portal, Billing));

        var payload = JiraAssetsClient.ParseObject(document.RootElement);

        Assert.Equal(["Portal", "Billing"], payload.Attributes[237]);
        Assert.Equal([new AssetObjectReference("2001", "ITSM-201", "Portal"), new AssetObjectReference("2002", "ITSM-202", "Billing")],
            payload.References[237]);
        Assert.False(payload.References.ContainsKey(231));
    }

    [Fact]
    public void TestATextAttributeOnARelationBecomesNamesAndAnEmptyOneNamesNothing()
    {
        var payload = new AssetObjectPayload { ObjectId = "1", Label = "app" };
        payload.Attributes[242] = ["srv-a", "srv-b", "srv-a"];
        var hosts = new JiraObjectAttributeMapping { SourceAttributeId = 242, SourceAttributeName = "Runs on", TargetField = MappableFields.Hosts };
        var empty = new JiraObjectAttributeMapping { SourceAttributeId = 999, SourceAttributeName = "Nothing", TargetField = MappableFields.Hosts };

        var projected = AssetAttributeProjector.Project(payload, [hosts]);
        Assert.Equal(["srv-a", "srv-b"], projected.ReferencesOf(MappableFields.Hosts).Select(r => r.Label));
        Assert.All(projected.ReferencesOf(MappableFields.Hosts), r => Assert.Null(r.ObjectId));
        Assert.False(projected.Fields.ContainsKey(MappableFields.Hosts));

        Assert.False(AssetAttributeProjector.Project(payload, [empty]).References.ContainsKey(MappableFields.Hosts));
    }

    // --- internet exposure -------------------------------------------------------------------

    [Fact]
    public async Task TestAnImportedExposureIsWrittenAsCmdbAndALaterNoReplacesIt()
    {
        GivenTheRegister(Objects(Server("Public")));
        var id = await ConnectionAsync(serverRelation: false);

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        var host = TheServer();
        Assert.Equal((true, AssetContextSources.Cmdb), (host.InternetFacing, host.InternetFacingSource));
        Assert.NotNull(host.InternetFacingSeenAt);

        GivenTheRegister(Objects(Server("Internal")));
        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal((false, AssetContextSources.Cmdb), (TheServer().InternetFacing, TheServer().InternetFacingSource));
    }

    [Fact]
    public async Task TestAManualExposureIsNeverOverwrittenByTheImport()
    {
        var stated = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SeedServer(h =>
        {
            h.InternetFacing = false;
            h.InternetFacingSource = AssetContextSources.Manual;
            h.InternetFacingSeenAt = stated;
        });
        GivenTheRegister(Objects(Server("Public")));
        var id = await ConnectionAsync(serverRelation: false);

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(1, result.Updated);
        var host = TheServer();
        Assert.Equal((false, AssetContextSources.Manual, stated), (host.InternetFacing, host.InternetFacingSource, host.InternetFacingSeenAt));
    }

    [Fact]
    public async Task TestAnUnrecognisedExposureIsReportedAndLeavesTheHostAlone()
    {
        GivenTheRegister(Objects(Server("Unknown")));
        var id = await ConnectionAsync(serverRelation: false);

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Contains(result.Messages, m => m.Contains("'Unknown'") && m.Contains("neither yes nor no"));
        Assert.Equal(((bool?)null, (string?)null), (TheServer().InternetFacing, TheServer().InternetFacingSource));
    }

    // --- application ↔ server relations ------------------------------------------------------

    [Fact]
    public async Task TestAServerNamingAnApplicationImportedLaterInTheSameRunIsLinked()
    {
        GivenTheRegister(Objects(Server(null, Portal)));
        var id = await ConnectionAsync();

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal((1, 0, 0), (result.RelationsLinked, result.RelationsRemoved, result.RelationsUnresolved));
        var link = Assert.Single(Links());
        Assert.Equal((TheServer().Id, ApplicationId("Portal"), AssetContextSources.Cmdb, (int?)id),
            (link.HostId, link.EntityId, link.Source, link.ConnectionId));
        Assert.NotNull(link.LastSeenAt);

        // A second identical run refreshes the row rather than adding one.
        var second = await _svc.ImportAssetsAsync(id, dryRun: false, 1);
        Assert.Equal(0, second.RelationsLinked);
        Assert.Single(Links());
    }

    [Fact]
    public async Task TestAReimportRemovesALinkTheRegisterNoLongerNamesButNeverAManualOne()
    {
        SeedApplication(ManualCrm, "Crm");
        GivenTheRegister(Objects(Server(null, Portal)));
        var id = await ConnectionAsync();
        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        var hostId = TheServer().Id;
        Seed(ctx => ctx.HostApplicationLinks.Add(new HostApplicationLink
            { HostId = hostId, EntityId = ManualCrm, Source = AssetContextSources.Manual, CreatedAt = DateTime.UtcNow }));

        GivenTheRegister(Objects(Server(null, Billing)));
        var second = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal((1, 1), (second.RelationsLinked, second.RelationsRemoved));
        Assert.Equal(
            new[] { (ManualCrm, AssetContextSources.Manual), (ApplicationId("Billing"), AssetContextSources.Cmdb) }
                .OrderBy(p => p.Item1),
            Links().Select(l => (l.EntityId, l.Source)).OrderBy(p => p.Item1));
    }

    [Fact]
    public async Task TestAManualLinkForAReportedPairIsNeitherRefreshedNorClaimed()
    {
        SeedApplication(SeededPortal, "Portal");
        SeedServer(_ => { });
        Seed(ctx => ctx.HostApplicationLinks.Add(new HostApplicationLink
            { HostId = 1, EntityId = SeededPortal, Source = AssetContextSources.Manual, CreatedAt = DateTime.UtcNow }));
        GivenTheRegister(Objects(Server(null, Portal)));
        var id = await ConnectionAsync();

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(0, result.RelationsLinked);
        var link = Assert.Single(Links());
        Assert.Equal((SeededPortal, AssetContextSources.Manual, (int?)null, (DateTime?)null),
            (link.EntityId, link.Source, link.ConnectionId, link.LastSeenAt));
    }

    /// <summary>
    /// The defect: a pair two connections both report was taken over by whichever ran last, so the row's connection
    /// flipped on every run, each flip an audited update. A connection now refreshes and removes only its own rows.
    /// </summary>
    [Fact]
    public async Task TestTwoConnectionsReportingTheSamePairDoNotTakeTheLinkFromEachOther()
    {
        GivenTheRegister(Objects(Server(null, Portal)));
        var first = await ConnectionAsync();
        var second = await ConnectionAsync(name: "Second desk");

        await _svc.ImportAssetsAsync(first, dryRun: false, 1);
        var linkedAt = Assert.Single(Links()).LastSeenAt;

        var other = await _svc.ImportAssetsAsync(second, dryRun: false, 1);
        await _svc.ImportAssetsAsync(first, dryRun: false, 1);
        var otherAgain = await _svc.ImportAssetsAsync(second, dryRun: false, 1);

        Assert.Equal((0, 0), (other.RelationsLinked, other.RelationsRemoved));
        Assert.Equal((0, 0), (otherAgain.RelationsLinked, otherAgain.RelationsRemoved));
        var link = Assert.Single(Links());
        Assert.Equal(((int?)first, AssetContextSources.Cmdb, (DateTime?)null), (link.ConnectionId, link.Source, link.UpdatedAt));
        Assert.True(link.LastSeenAt >= linkedAt);
        Assert.DoesNotContain(Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == nameof(HostApplicationLink)).ToList()),
            a => a.Field == nameof(HostApplicationLink.ConnectionId));
    }

    /// <summary>The other side of the same rule: a connection that stops naming the pair does not remove another's row.</summary>
    [Fact]
    public async Task TestAConnectionThatNoLongerNamesAPairLeavesAnotherConnectionsLinkAlone()
    {
        GivenTheRegister(Objects(Server(null, Portal)));
        var first = await ConnectionAsync();
        var second = await ConnectionAsync(name: "Second desk");
        await _svc.ImportAssetsAsync(first, dryRun: false, 1);
        await _svc.ImportAssetsAsync(second, dryRun: false, 1);

        GivenTheRegister(Objects(Server(null, Billing)));
        var moved = await _svc.ImportAssetsAsync(second, dryRun: false, 1);

        Assert.Equal((1, 0), (moved.RelationsLinked, moved.RelationsRemoved));
        Assert.Equal(
            new[] { (ApplicationId("Portal"), (int?)first), (ApplicationId("Billing"), (int?)second) }.OrderBy(p => p.Item1),
            Links().Select(l => (l.EntityId, l.ConnectionId)).OrderBy(p => p.Item1));
    }

    [Fact]
    public async Task TestAnUnresolvedReferenceIsReportedAndLinksNothing()
    {
        GivenTheRegister(Objects(Server(null, ("9999", "ITSM-999", "Ghost"))));
        var id = await ConnectionAsync();

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal((0, 1), (result.RelationsLinked, result.RelationsUnresolved));
        Assert.Contains(result.Messages, m => m.Contains("srv-prod-01") && m.Contains("Ghost"));
        Assert.Empty(Links());
    }

    [Fact]
    public async Task TestTheDryRunCountsTheRelationsAndWritesNone()
    {
        GivenTheRegister(Objects(Server(null, Portal)));
        var id = await ConnectionAsync();
        await _svc.ImportAssetsAsync(id, dryRun: false, 1);
        var before = Assert.Single(Links());

        GivenTheRegister(Objects(Server(null, Billing)));
        var preview = await _svc.ImportAssetsAsync(id, dryRun: true, 1);

        Assert.Equal((1, 1), (preview.RelationsLinked, preview.RelationsRemoved));
        var after = Assert.Single(Links());
        Assert.Equal((before.EntityId, before.LastSeenAt), (after.EntityId, after.LastSeenAt));
    }

    [Fact]
    public async Task TestAnApplicationNamingItsServerByHostNameIsLinked()
    {
        SeedServer(_ => { });
        GivenTheRegister(Objects(Server()), Objects(Application("2001", "ITSM-201", "Portal", hostName: "srv-prod-01")));
        var id = await ConnectionAsync(serverRelation: false, applicationRelation: true);

        var result = await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        Assert.Equal(1, result.RelationsLinked);
        var link = Assert.Single(Links());
        Assert.Equal((1, ApplicationId("Portal"), AssetContextSources.Cmdb), (link.HostId, link.EntityId, link.Source));
    }

    [Fact]
    public async Task TestALinkIsRecordedInTheAuditTrail()
    {
        GivenTheRegister(Objects(Server(null, Portal)));
        var id = await ConnectionAsync();

        await _svc.ImportAssetsAsync(id, dryRun: false, 1);

        var link = Assert.Single(Links());
        Assert.Contains(Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == nameof(HostApplicationLink)).ToList()),
            a => a.EntityId == link.Id && a.Action == AuditLogAction.Create);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Integrations;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The Track 4.6 endpoints: the connection's Jira facet, the metadata behind the mapping editors, the
/// two configurable mappings, the request mirror, the Assets import, and issue links on records that
/// are not findings.
///
/// About the HTTP contract rather than the domain logic — which exception becomes which status code,
/// and what does and does not reach the wire. The ones that would actually hurt if they regressed are
/// the permission check in <see cref="RecordIssuesController"/>, which is in the action rather than an
/// attribute because the required permission is part of the URL, and the preview/import split, which
/// is by route so that a client cannot turn a preview into a write by flipping a flag.
/// </summary>
[TestSubject(typeof(JiraController))]
public class JiraControllersTest : BaseControllerTest
{
    private const int Connection = MockedJiraIntegrationService.KnownConnectionId;
    private const int GitHubConnection = MockedJiraIntegrationService.NonJiraConnectionId;
    private const int Incident = MockedJiraIntegrationService.KnownIncidentId;
    private const int Risk = MockedJiraIntegrationService.KnownRiskId;
    private const string IssueKey = MockedJiraIntegrationService.KnownIssueKey;

    private static JiraController Jira() => ResolveController<JiraController>(_ => { });

    /// <summary>
    /// A record-issues controller whose permission service grants exactly
    /// <paramref name="granted"/>.
    ///
    /// Registered per test rather than as a shared mock, because the whole point of these tests is
    /// what happens when a permission is *missing* — a shared always-allow double would assert the
    /// opposite of the interesting case.
    /// </summary>
    private static RecordIssuesController Records(params string[] granted)
    {
        return ResolveController<RecordIssuesController>(services =>
        {
            var permissions = Substitute.For<IPermissionsService>();

            permissions.UserHasPermission(Arg.Any<User>(), Arg.Any<string>())
                .Returns(call => granted.Contains(call.ArgAt<string>(1)));

            services.AddSingleton(permissions);
        });
    }

    private static TValue Ok<TValue>(ActionResult<TValue> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<TValue>(ok.Value);
    }

    // --- the connection facet ---------------------------------------------------------------

    [Fact]
    public async Task GetSettingsReturnsTheFacet()
    {
        var settings = Ok(await Jira().GetSettings(Connection));

        Assert.Equal(Connection, settings.ConnectionId);
        Assert.Equal(JiraDeployment.Cloud, settings.Deployment);
        Assert.True(settings.JsmEnabled);
        Assert.Equal(3, settings.ServiceDeskId);
    }

    /// <summary>
    /// The facet carries no credential.
    ///
    /// Structural rather than incidental: <see cref="JiraConnectionSettingsView"/> has nowhere to put
    /// a token, so this asserts the shape the endpoint returns is still that type.
    /// </summary>
    [Fact]
    public async Task TheFacetHasNoPlaceForACredential()
    {
        var settings = Ok(await Jira().GetSettings(Connection));

        Assert.Empty(settings.GetType().GetProperties()
            .Where(p => p.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("password", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task GetSettingsOfAnUnknownConnectionIs404()
    {
        Assert.IsType<NotFoundObjectResult>((await Jira().GetSettings(9999)).Result);
    }

    [Fact]
    public async Task SaveSettingsRoundTrips()
    {
        var settings = Ok(await Jira().GetSettings(Connection));
        settings.SlaBreachNotifications = true;

        var saved = Ok(await Jira().SaveSettings(Connection, settings));

        Assert.True(saved.SlaBreachNotifications);
    }

    [Fact]
    public async Task SaveSettingsWithNoBodyDoesNotThrow()
    {
        // A null body binds to null, and an endpoint that throws on it answers 500 for what is a
        // client mistake.
        Assert.IsType<OkObjectResult>((await Jira().SaveSettings(Connection, null!)).Result);
    }

    [Fact]
    public async Task SavingADataCenterDeploymentIs400()
    {
        var settings = Ok(await Jira().GetSettings(Connection));
        settings.Deployment = JiraDeployment.DataCenter;

        Assert.IsType<BadRequestObjectResult>((await Jira().SaveSettings(Connection, settings)).Result);
    }

    // --- metadata ---------------------------------------------------------------------------

    [Fact]
    public async Task TheServiceDeskQueueAndRequestTypePickersReadFromJira()
    {
        Assert.Single(Ok(await Jira().GetServiceDesks(Connection)));
        Assert.Single(Ok(await Jira().GetQueues(Connection, 3)));
        Assert.Single(Ok(await Jira().GetRequestTypes(Connection, 3)));
    }

    /// <summary>
    /// The field picker offers custom fields, which is the whole reason it exists: nobody types
    /// <c>customfield_10012</c> from memory and gets it right.
    /// </summary>
    [Fact]
    public async Task TheFieldPickerIncludesCustomFields()
    {
        var fields = Ok(await Jira().GetFields(Connection));

        Assert.Contains(fields, f => f.Id == "customfield_10012" && f.IsCustom);
    }

    [Fact]
    public async Task ThePriorityAndStatusPickersReadFromJira()
    {
        Assert.Contains("Highest", Ok(await Jira().GetPriorities(Connection)));
        Assert.Contains("Done", Ok(await Jira().GetStatuses(Connection)));
    }

    /// <summary>
    /// A connection that exists but is not Jira is a 400, not a 404.
    ///
    /// The distinction matters to whoever reads the response: the connection is there, it is simply
    /// the wrong kind of tracker for this endpoint.
    /// </summary>
    [Fact]
    public async Task AJiraEndpointOnANonJiraConnectionIs400()
    {
        Assert.IsType<BadRequestObjectResult>((await Jira().GetServiceDesks(GitHubConnection)).Result);
        Assert.IsType<BadRequestObjectResult>((await Jira().GetAssetSchemas(GitHubConnection)).Result);
    }

    /// <summary>
    /// The mappable-field catalog is served by the API so the client's picker cannot offer a target
    /// the mapping engine does not implement.
    /// </summary>
    [Fact]
    public void TheMappableFieldCatalogIsFilteredByTargetKind()
    {
        var host = Assert.IsType<OkObjectResult>(
            Jira().GetMappableFields(JiraAssetTargetKind.Host).Result);

        var app = Assert.IsType<OkObjectResult>(
            Jira().GetMappableFields(JiraAssetTargetKind.ApplicationEntity).Result);

        var hostFields = Assert.IsAssignableFrom<List<MappableFieldView>>(host.Value);
        var appFields = Assert.IsAssignableFrom<List<MappableFieldView>>(app.Value);

        Assert.Contains(hostFields, f => f.Name == "MacAddress");
        Assert.DoesNotContain(appFields, f => f.Name == "MacAddress");
    }

    // --- field mapping ----------------------------------------------------------------------

    [Fact]
    public async Task FieldMappingsRoundTrip()
    {
        Assert.Single(Ok(await Jira().GetFieldMappings(Connection)));

        var saved = Ok(await Jira().SetFieldMappings(Connection,
        [
            new JiraFieldMappingView { NetRiskField = "Cvss", JiraFieldId = "customfield_20" }
        ]));

        Assert.Equal("customfield_20", Assert.Single(saved).JiraFieldId);
    }

    [Fact]
    public async Task AFieldMappingWithNoJiraFieldIs400()
    {
        var result = await Jira().SetFieldMappings(Connection,
            [new JiraFieldMappingView { NetRiskField = "Severity", JiraFieldId = "" }]);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TwoMappingsOnOneJiraFieldIs400()
    {
        var result = await Jira().SetFieldMappings(Connection,
        [
            new JiraFieldMappingView { NetRiskField = "Severity", JiraFieldId = "customfield_1" },
            new JiraFieldMappingView { NetRiskField = "Cvss", JiraFieldId = "customfield_1" }
        ]);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SettingFieldMappingsWithNoBodyClearsThemRatherThanThrowing()
    {
        // Wholesale replacement, so an empty list is a legitimate "remove them all" and a null body
        // must not become a 500.
        Assert.Empty(Ok(await Jira().SetFieldMappings(Connection, null!)));
    }

    // --- object mapping ---------------------------------------------------------------------

    [Fact]
    public async Task AssetSchemasObjectTypesAndAttributesAreReadable()
    {
        Assert.Single(Ok(await Jira().GetAssetSchemas(Connection)));
        Assert.Single(Ok(await Jira().GetAssetObjectTypes(Connection, 5)));
        Assert.Equal(2, Ok(await Jira().GetAssetAttributes(Connection, 23)).Count);
    }

    [Fact]
    public async Task ObjectMappingsRoundTrip()
    {
        var existing = Ok(await Jira().GetObjectMappings(Connection));
        Assert.Single(existing);

        var saved = Ok(await Jira().SetObjectMappings(Connection, existing));

        Assert.Equal(23, Assert.Single(saved).ObjectTypeId);
    }

    [Fact]
    public async Task AnObjectMappingWithNoNameTargetIs400()
    {
        var mapping = Ok(await Jira().GetObjectMappings(Connection))[0];
        mapping.AttributeMappings = [];

        Assert.IsType<BadRequestObjectResult>(
            (await Jira().SetObjectMappings(Connection, [mapping])).Result);
    }

    [Fact]
    public async Task AnObjectMappingWithNoObjectTypeIs400()
    {
        var mapping = Ok(await Jira().GetObjectMappings(Connection))[0];
        mapping.ObjectTypeId = 0;

        Assert.IsType<BadRequestObjectResult>(
            (await Jira().SetObjectMappings(Connection, [mapping])).Result);
    }

    // --- import -----------------------------------------------------------------------------

    /// <summary>
    /// Preview and import are separate routes, and the preview really is a dry run.
    ///
    /// Separate routes rather than one route with a flag, so a client cannot turn a preview into a
    /// write by flipping a parameter — and so the two can carry different audit logging.
    /// </summary>
    [Fact]
    public async Task PreviewIsADryRunAndImportIsNot()
    {
        var preview = Ok(await Jira().PreviewAssetImport(Connection));
        Assert.True(preview.DryRun);
        Assert.NotEmpty(preview.Sample);

        var import = Ok(await Jira().ImportAssets(Connection));
        Assert.False(import.DryRun);
        Assert.Equal(2, import.Created);
    }

    [Fact]
    public async Task ImportingOnAnUnknownConnectionIs404()
    {
        Assert.IsType<NotFoundObjectResult>((await Jira().ImportAssets(9999)).Result);
    }

    [Fact]
    public async Task TheImportedRegisterCarriesTheFourMappedFields()
    {
        var objects = Ok(await Jira().GetAssetObjects(Connection));
        var imported = Assert.Single(objects);

        Assert.Equal("srv-prod-01", imported.MappedName);
        Assert.Equal("Alice Silva", imported.MappedOwner);
        Assert.Equal("Production", imported.MappedEnvironment);
        Assert.True(imported.MappedActive);
        Assert.Equal("mac", imported.MatchReason);
    }

    // --- the mirror -------------------------------------------------------------------------

    [Fact]
    public async Task TheMirrorIsReadableWithItsSlaCycles()
    {
        var requests = Ok(await Jira().GetRequests(Connection));
        var request = Assert.Single(requests);

        Assert.Equal(IssueKey, request.IssueKey);
        Assert.True(request.AnySlaBreached);
        Assert.Equal(-5_400_000, Assert.Single(request.Slas).RemainingMs);
    }

    [Fact]
    public async Task OneMirroredRequestIsReadableAndAnUnknownKeyIs404()
    {
        Assert.Equal(IssueKey, Ok(await Jira().GetRequest(Connection, IssueKey)).IssueKey);

        Assert.IsType<NotFoundObjectResult>((await Jira().GetRequest(Connection, "SD-9999")).Result);
    }

    [Fact]
    public async Task SyncReportsWhatItMirrored()
    {
        var result = Ok(await Jira().Sync(Connection));

        Assert.Equal(4, result.RequestsExamined);
        Assert.Equal(1, result.Breaches);
    }

    // --- record issues ----------------------------------------------------------------------

    [Fact]
    public async Task AnIncidentsLinksAreReadableWithTheIncidentPermission()
    {
        var links = Assert.IsType<OkObjectResult>(
            (await Records("incident_management").GetForRecord("incident", Incident)).Result);

        var list = Assert.IsAssignableFrom<List<FindingIssueLinkView>>(links.Value);

        Assert.Equal(IssueLinkTargetKind.Incident, Assert.Single(list).TargetKind);
    }

    [Fact]
    public async Task ARisksLinksAreReadableWithTheRiskPermission()
    {
        var links = Assert.IsType<OkObjectResult>(
            (await Records("riskmanagement").GetForRecord("risk", Risk)).Result);

        var list = Assert.IsAssignableFrom<List<FindingIssueLinkView>>(links.Value);

        Assert.Equal(IssueLinkTargetKind.Risk, Assert.Single(list).TargetKind);
    }

    /// <summary>
    /// The permission is decided by the record kind in the route, so a caller who may see incidents
    /// cannot read a risk's links.
    ///
    /// This is the reason the check lives in the action: a controller-level attribute would have to
    /// name the union of all three permissions, and holding any one of them would then be enough for
    /// all three.
    /// </summary>
    [Fact]
    public async Task TheIncidentPermissionDoesNotGrantARisksLinks()
    {
        Assert.IsType<ForbidResult>(
            (await Records("incident_management").GetForRecord("risk", Risk)).Result);
    }

    [Fact]
    public async Task NoPermissionIsForbidden()
    {
        Assert.IsType<ForbidResult>(
            (await Records().GetForRecord("incident", Incident)).Result);
    }

    /// <summary>
    /// Writing needs the record's own modify permission: filing a ticket about a risk is a statement
    /// about that risk, so read access to the register is not enough.
    /// </summary>
    [Fact]
    public async Task CreatingAnIssueForARiskNeedsTheModifyPermission()
    {
        var request = new RecordIssueRequest { ConnectionId = Connection };

        Assert.IsType<ForbidResult>(
            (await Records("riskmanagement").Create("risk", Risk, request)).Result);

        var created = Assert.IsType<OkObjectResult>(
            (await Records("modify_risks").Create("risk", Risk, request)).Result);

        Assert.Equal(IssueLinkTargetKind.Risk,
            Assert.IsType<FindingIssueLinkView>(created.Value).TargetKind);
    }

    [Fact]
    public async Task CreatingAnIssueForAFindingThroughTheRecordRouteIs400()
    {
        var request = new RecordIssueRequest { ConnectionId = Connection };

        // Findings go through FindingIssuesController, which also applies the auto-create policy and
        // feeds the conflict queue.
        Assert.IsType<BadRequestObjectResult>(
            (await Records("vulnerabilities_update").Create("finding", 42, request)).Result);
    }

    [Fact]
    public async Task LinkingAnExistingIssueRoundTrips()
    {
        var request = new RecordIssueRequest { ConnectionId = Connection, IssueKey = IssueKey };

        var linked = Assert.IsType<OkObjectResult>(
            (await Records("incident_management").Link("incident", Incident, request)).Result);

        Assert.Equal(IssueKey, Assert.IsType<FindingIssueLinkView>(linked.Value).IssueKey);
    }

    [Fact]
    public async Task LinkingWithNoIssueKeyIs400()
    {
        var request = new RecordIssueRequest { ConnectionId = Connection, IssueKey = "" };

        Assert.IsType<BadRequestObjectResult>(
            (await Records("incident_management").Link("incident", Incident, request)).Result);
    }

    [Fact]
    public async Task LinkingToAnIssueThatDoesNotExistIs404()
    {
        var request = new RecordIssueRequest { ConnectionId = Connection, IssueKey = "SD-9999" };

        Assert.IsType<NotFoundObjectResult>(
            (await Records("incident_management").Link("incident", Incident, request)).Result);
    }

    /// <summary>
    /// A record kind that is not one of the three is a 400 naming the three, and the permission check
    /// is never reached — so an unparseable kind cannot be a way to probe which permissions a caller
    /// holds.
    /// </summary>
    [Theory]
    [InlineData("mitigation")]
    [InlineData("")]
    [InlineData("Finding; DROP TABLE risks")]
    public async Task AnUnknownRecordKindIs400(string kind)
    {
        Assert.IsType<BadRequestObjectResult>(
            (await Records("incident_management").GetForRecord(kind, 1)).Result);
    }

    /// <summary>The kind in the route is matched case-insensitively, since a URL is not a C# enum.</summary>
    [Theory]
    [InlineData("incident")]
    [InlineData("Incident")]
    [InlineData("INCIDENT")]
    public async Task TheRecordKindIsCaseInsensitive(string kind)
    {
        Assert.IsType<OkObjectResult>(
            (await Records("incident_management").GetForRecord(kind, Incident)).Result);
    }
}

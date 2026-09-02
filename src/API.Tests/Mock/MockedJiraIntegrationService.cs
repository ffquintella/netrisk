using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Enums;
using Model.Exceptions;
using Model.Integrations;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// The Jira Service Management and Assets double (Track 4 milestone 4.6).
///
/// Registered automatically by <see cref="DI.ServiceRegistration"/>, which binds every
/// <c>Mocked*.Create()</c> in this namespace against the interface it returns — so covering the two
/// new controllers needed no edit to any shared file.
///
/// It answers deterministically for <see cref="KnownConnectionId"/> and throws the domain exceptions
/// the controllers map onto status codes for everything else. The guard behaviours are reproduced
/// rather than stubbed away, because the controller tests are the place the 400s are asserted.
/// </summary>
public static class MockedJiraIntegrationService
{
    public const int KnownConnectionId = 1;

    public const int KnownIncidentId = 7;

    public const int KnownRiskId = 9;

    public const string KnownIssueKey = "SD-4711";

    /// <summary>A connection that is a GitHub connection, so the Jira surface refuses it.</summary>
    public const int NonJiraConnectionId = 2;

    public static IJiraIntegrationService Create()
    {
        var service = Substitute.For<IJiraIntegrationService>();

        service.GetSettingsAsync(Arg.Any<int>()).Returns(call => Known(call.ArgAt<int>(0))
            ? Task.FromResult(Settings())
            : Missing<JiraConnectionSettingsView>(call.ArgAt<int>(0)));

        service.SaveSettingsAsync(Arg.Any<int>(), Arg.Any<JiraConnectionSettingsView>())
            .Returns(call =>
            {
                if (!Known(call.ArgAt<int>(0)))
                    return Missing<JiraConnectionSettingsView>(call.ArgAt<int>(0));

                var settings = call.ArgAt<JiraConnectionSettingsView>(1);

                if (settings.Deployment == JiraDeployment.DataCenter)
                    throw new InvalidParameterException("connectionId",
                        "Service Management and Assets are implemented for Jira Cloud only.");

                settings.ConnectionId = KnownConnectionId;

                return Task.FromResult(settings);
            });

        service.GetServiceDesksAsync(Arg.Any<int>()).Returns(call => Jira(call.ArgAt<int>(0),
            new List<JiraServiceDeskView>
            {
                new() { Id = 3, ProjectKey = "SD", ProjectName = "Service desk" }
            }));

        service.GetRequestTypesAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            Jira(call.ArgAt<int>(0), new List<JiraRequestTypeView>
            {
                new() { Id = "77", Name = "Report a system problem" }
            }));

        service.GetQueuesAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            Jira(call.ArgAt<int>(0), new List<JiraQueueView>
            {
                new() { Id = 10, Name = "Open", IssueCount = 42 }
            }));

        service.GetJiraFieldsAsync(Arg.Any<int>()).Returns(call =>
            Jira(call.ArgAt<int>(0), new List<JiraFieldView>
            {
                new() { Id = "priority", Name = "Priority", Type = "priority" },
                new() { Id = "customfield_10012", Name = "Security severity", Type = "option", IsCustom = true }
            }));

        service.GetJiraPrioritiesAsync(Arg.Any<int>()).Returns(call =>
            Jira(call.ArgAt<int>(0), new List<string> { "Highest", "High", "Medium", "Low" }));

        service.GetJiraStatusesAsync(Arg.Any<int>()).Returns(call =>
            Jira(call.ArgAt<int>(0), new List<string> { "Done", "In Progress", "Waiting for support" }));

        service.GetMappableFields(Arg.Any<JiraAssetTargetKind?>()).Returns(call =>
        {
            var kind = call.ArgAt<JiraAssetTargetKind?>(0);

            var fields = new List<MappableFieldView>
            {
                new() { Name = "Name", Label = "Name" },
                new() { Name = "Owner", Label = "Responsible" },
                new() { Name = "Environment", Label = "Environment" },
                new() { Name = "Active", Label = "Active state" }
            };

            if (kind != JiraAssetTargetKind.ApplicationEntity)
                fields.Add(new MappableFieldView
                {
                    Name = "MacAddress", Label = "MAC address",
                    AppliesTo = [JiraAssetTargetKind.Host]
                });

            return fields;
        });

        service.GetFieldMappingsAsync(Arg.Any<int>()).Returns(call =>
            Known(call.ArgAt<int>(0))
                ? Task.FromResult(new List<JiraFieldMappingView> { FieldMapping() })
                : Missing<List<JiraFieldMappingView>>(call.ArgAt<int>(0)));

        service.SetFieldMappingsAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<JiraFieldMappingView>>())
            .Returns(call =>
            {
                if (!Known(call.ArgAt<int>(0)))
                    return Missing<List<JiraFieldMappingView>>(call.ArgAt<int>(0));

                var mappings = call.ArgAt<IReadOnlyList<JiraFieldMappingView>>(1);

                // The two configurations that would be stored and then silently do nothing.
                if (mappings.Any(m => string.IsNullOrWhiteSpace(m.JiraFieldId)))
                    throw new InvalidParameterException("JiraFieldId",
                        "Every field mapping needs a Jira field.");

                if (mappings.GroupBy(m => (m.Direction, m.JiraFieldId)).Any(g => g.Count() > 1))
                    throw new InvalidParameterException("mappings",
                        "Two mappings target the same field. One writer per field.");

                return Task.FromResult(mappings.ToList());
            });

        service.GetObjectMappingsAsync(Arg.Any<int>()).Returns(call =>
            Known(call.ArgAt<int>(0))
                ? Task.FromResult(new List<JiraObjectMappingView> { ObjectMapping() })
                : Missing<List<JiraObjectMappingView>>(call.ArgAt<int>(0)));

        service.SetObjectMappingsAsync(Arg.Any<int>(),
                Arg.Any<IReadOnlyList<JiraObjectMappingView>>(), Arg.Any<int?>())
            .Returns(call =>
            {
                if (!Known(call.ArgAt<int>(0)))
                    return Missing<List<JiraObjectMappingView>>(call.ArgAt<int>(0));

                var mappings = call.ArgAt<IReadOnlyList<JiraObjectMappingView>>(1);

                foreach (var mapping in mappings)
                {
                    if (mapping.ObjectTypeId <= 0)
                        throw new InvalidParameterException("ObjectTypeId",
                            "Every object mapping needs an Assets object type.");

                    if (mapping.AttributeMappings.All(a => a.TargetField != "Name"))
                        throw new InvalidParameterException("AttributeMappings",
                            "The mapping has no Name target. Without one nothing can be matched.");
                }

                return Task.FromResult(mappings.ToList());
            });

        service.GetAssetSchemasAsync(Arg.Any<int>()).Returns(call =>
            Jira(call.ArgAt<int>(0), new List<JiraObjectSchemaView>
            {
                new() { Id = 5, Name = "IT infrastructure", ObjectSchemaKey = "ITSM", ObjectCount = 1200 }
            }));

        service.GetAssetObjectTypesAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            Jira(call.ArgAt<int>(0), new List<JiraObjectTypeView>
            {
                new() { Id = 23, Name = "Server", ObjectCount = 310 }
            }));

        service.GetAssetAttributesAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            Jira(call.ArgAt<int>(0), new List<JiraObjectTypeAttributeView>
            {
                new() { Id = 231, Name = "Hostname", Type = "Default", IsLabel = true },
                new() { Id = 232, Name = "Owner", Type = "User" }
            }));

        service.ImportAssetsAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int?>()).Returns(call =>
        {
            if (!Known(call.ArgAt<int>(0))) return Missing<AssetImportResult>(call.ArgAt<int>(0));

            var dryRun = call.ArgAt<bool>(1);

            return Task.FromResult(new AssetImportResult
            {
                DryRun = dryRun,
                Examined = 3,
                Created = dryRun ? 0 : 2,
                Updated = 1,
                Sample = dryRun
                    ? [new JiraAssetObjectView { ObjectId = "1042", MappedName = "srv-prod-01" }]
                    : []
            });
        });

        service.GetAssetObjectsAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            Known(call.ArgAt<int>(0))
                ? Task.FromResult(new List<JiraAssetObjectView>
                {
                    new()
                    {
                        Id = 1, ObjectId = "1042", ObjectKey = "ITSM-88", ObjectTypeName = "Server",
                        MappedName = "srv-prod-01", MappedOwner = "Alice Silva",
                        MappedEnvironment = "Production", MappedActive = true,
                        TargetKind = JiraAssetTargetKind.Host, TargetHostId = 1, MatchReason = "mac"
                    }
                })
                : Missing<List<JiraAssetObjectView>>(call.ArgAt<int>(0)));

        service.GetMirroredRequestsAsync(Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<int>())
            .Returns(call => Known(call.ArgAt<int>(0))
                ? Task.FromResult(new List<JiraServiceRequestView> { Request() })
                : Missing<List<JiraServiceRequestView>>(call.ArgAt<int>(0)));

        service.GetMirroredRequestAsync(Arg.Any<int>(), Arg.Any<string>()).Returns(call =>
            Known(call.ArgAt<int>(0)) && call.ArgAt<string>(1) == KnownIssueKey
                ? Task.FromResult(Request())
                : throw new DataNotFoundException("jira service request", call.ArgAt<string>(1),
                    new Exception("Not mirrored.")));

        service.SyncServiceManagementAsync(Arg.Any<int>(), Arg.Any<int?>()).Returns(call =>
            Known(call.ArgAt<int>(0))
                ? Task.FromResult(new JsmSyncResult
                {
                    QueuesExamined = 1, RequestsExamined = 4, RequestsCreated = 1,
                    RequestsUpdated = 3, SlaCyclesRecorded = 6, Breaches = 1
                })
                : Missing<JsmSyncResult>(call.ArgAt<int>(0)));

        service.GetLinksForRecordAsync(Arg.Any<IssueLinkTargetKind>(), Arg.Any<int>())
            .Returns(call =>
            {
                var kind = call.ArgAt<IssueLinkTargetKind>(0);
                var id = call.ArgAt<int>(1);

                var known = (kind == IssueLinkTargetKind.Incident && id == KnownIncidentId)
                            || (kind == IssueLinkTargetKind.Risk && id == KnownRiskId);

                return Task.FromResult(known
                    ? new List<FindingIssueLinkView> { Link(kind, id) }
                    : new List<FindingIssueLinkView>());
            });

        service.CreateIssueForRecordAsync(Arg.Any<int>(), Arg.Any<IssueLinkTargetKind>(),
                Arg.Any<int>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var kind = call.ArgAt<IssueLinkTargetKind>(1);

                // Findings go through the finding endpoint, which also applies the auto-create policy
                // and feeds the conflict queue.
                if (kind == IssueLinkTargetKind.Finding)
                    throw new InvalidParameterException("targetKind",
                        "Findings are created through the finding-issues endpoint.");

                return Task.FromResult(Link(kind, call.ArgAt<int>(2)));
            });

        service.LinkRecordAsync(Arg.Any<int>(), Arg.Any<IssueLinkTargetKind>(), Arg.Any<int>(),
                Arg.Any<string>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var key = call.ArgAt<string>(3);

                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidParameterException("issueKeyOrUrl",
                        "Give an issue key (SD-4711) or the issue's URL.");

                if (key != KnownIssueKey)
                    throw new DataNotFoundException("issue", key, new Exception("No such issue."));

                return Task.FromResult(Link(call.ArgAt<IssueLinkTargetKind>(1), call.ArgAt<int>(2)));
            });

        return service;
    }

    private static bool Known(int connectionId) =>
        connectionId is KnownConnectionId or NonJiraConnectionId;

    /// <summary>
    /// A live Jira read: not found for an unknown connection, and refused for a connection that is
    /// not Jira — which is a 400 and not a 404, because the connection exists.
    /// </summary>
    private static Task<T> Jira<T>(int connectionId, T value) =>
        connectionId switch
        {
            KnownConnectionId => Task.FromResult(value),
            NonJiraConnectionId => throw new InvalidParameterException("connectionId",
                "Connection 'Product GitHub' is a GitHub connection. Service Management and Assets "
                + "are Jira features."),
            _ => Missing<T>(connectionId)
        };

    private static Task<T> Missing<T>(int connectionId) =>
        throw new DataNotFoundException("issue tracker connection", connectionId.ToString(),
            new Exception($"No issue-tracker connection {connectionId}."));

    private static JiraConnectionSettingsView Settings() => new()
    {
        ConnectionId = KnownConnectionId,
        Deployment = JiraDeployment.Cloud,
        JsmEnabled = true,
        ServiceDeskId = 3,
        ServiceDeskName = "Service desk",
        ImportSlas = true,
        AssetsEnabled = true,
        AssetsWorkspaceId = "b2c3d4e5-0000-1111-2222-333344445555",
        AssetsSchemaId = 5,
        AssetsSchemaName = "IT infrastructure",
        QueueImports = [new JiraQueueImportView { Id = 1, ServiceDeskId = 3, QueueId = 10, QueueName = "Open" }]
    };

    private static JiraFieldMappingView FieldMapping() => new()
    {
        Id = 1, Direction = JiraFieldMappingDirection.Outbound, NetRiskField = "Severity",
        JiraFieldId = "customfield_10012", JiraFieldName = "Security severity", Enabled = true
    };

    private static JiraObjectMappingView ObjectMapping() => new()
    {
        Id = 1, ObjectTypeId = 23, ObjectTypeName = "Server",
        TargetKind = JiraAssetTargetKind.Host, Enabled = true, CreateMissing = true,
        UpdateExisting = true,
        AttributeMappings =
        [
            new JiraObjectAttributeMappingView
            {
                Id = 1, SourceAttributeId = 231, SourceAttributeName = "Hostname",
                TargetField = "Name", IsIdentity = true
            }
        ]
    };

    private static JiraServiceRequestView Request() => new()
    {
        Id = 1, ConnectionId = KnownConnectionId, IssueKey = KnownIssueKey,
        RequestTypeName = "Report a system problem",
        Summary = "Payment gateway is timing out", StatusName = "Waiting for support",
        StatusCategory = "in-progress", ReporterDisplayName = "Alice Silva",
        AnySlaBreached = true,
        Slas =
        [
            new JiraRequestSlaView
            {
                Id = 1, MetricName = "Time to first response", IsOngoing = true, Breached = true,
                RemainingMs = -5_400_000
            }
        ]
    };

    private static FindingIssueLinkView Link(IssueLinkTargetKind kind, int targetId) => new()
    {
        Id = 5, TargetKind = kind, TargetId = targetId,
        FindingId = kind == IssueLinkTargetKind.Finding ? targetId : 0,
        ConnectionId = KnownConnectionId, ConnectionName = "Service desk",
        Provider = IssueTrackerProviderKind.Jira, IssueKey = KnownIssueKey,
        IssueUrl = "https://acme.atlassian.net/browse/SD-4711",
        LastSyncedStatus = "Waiting for support"
    };
}

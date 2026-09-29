using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// Jira Service Management and Assets configuration, mirroring and import
/// (Track 4 milestone 4.6).
///
/// Split across partials by concern — this file holds the connection facet, the discovery
/// passthroughs and the two mapping editors; <c>JiraIntegrationService.Jsm.cs</c> holds the request
/// mirror; <c>JiraIntegrationService.Assets.cs</c> holds the register import; and
/// <c>JiraIntegrationService.Links.cs</c> holds the widening of issue links from findings to
/// incidents and risks. One class because they share the connection resolution and the token
/// decryption, and four services would have meant four copies of both.
/// </summary>
public partial class JiraIntegrationService(
    ILogger logger,
    IDalService dalService,
    ISecretResolver resolver,
    IJiraServiceManagementClient jsm,
    IJiraAssetsClient assets,
    IJiraMetadataClient metadata,
    IIssueTrackerProviderRegistry registry,
    IEntitiesService entities,
    INotificationEventPublisher notifications,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    IIntegrationSyncTracker tracker)
    : ServiceBase(logger, dalService), IJiraIntegrationService
{
    /// <summary>
    /// Resolves a connection and its decrypted token for one Jira facet, refusing a connection the
    /// facet is not implemented for with a sentence that names why.
    ///
    /// Here rather than at each call site because every Service Management, Assets and record-link
    /// read passes through it. The provider kind alone decides — Assets on both deployments, Service
    /// Management and record links on Cloud only — so a settings row can never re-route a call.
    /// </summary>
    private async Task<(IssueTrackerConnection Connection, string? Token, JiraConnectionSettings Settings)>
        ResolveAsync(int connectionId, JiraFacet facet)
    {
        await using var db = DalService.GetContext();

        var connection = await db.IssueTrackerConnections
                             .Include(c => c.JiraSettings)
                             .FirstOrDefaultAsync(c => c.Id == connectionId)
                         ?? throw new DataNotFoundException("issue tracker connection",
                             connectionId.ToString(),
                             new Exception($"No issue-tracker connection {connectionId}."));

        if (Refusal(connection, facet) is { } refusal)
            throw new InvalidParameterException(nameof(connectionId), refusal);

        var settings = connection.JiraSettings
                       ?? await EnsureSettingsAsync(connectionId, connection.Provider);

        return (connection, await resolver.ResolveAsync(connection.EncryptedToken), settings);
    }

    /// <summary>Why <paramref name="facet"/> cannot be used on this connection, or null when it can.</summary>
    internal static string? Refusal(IssueTrackerConnection connection, JiraFacet facet)
    {
        if (!JiraDialect.IsJira(connection.Provider))
            return $"Connection '{connection.Name}' is a {connection.Provider} connection. Service "
                   + "Management and Assets are Jira features.";

        return facet switch
        {
            JiraFacet.Assets => null,
            _ when JiraDialect.SupportsServiceManagement(connection.Provider) => null,
            JiraFacet.ServiceManagement =>
                $"Connection '{connection.Name}' is a Jira Data Center connection. Service Management "
                + "is implemented for Jira Cloud only; Assets is available on Data Center.",
            _ =>
                $"Connection '{connection.Name}' is a Jira Data Center connection. Creating or linking "
                + "issues for incidents and risks is implemented for Jira Cloud only."
        };
    }

    /// <summary>
    /// A connection and its credential for a platform-metadata read — the site's fields, its
    /// priorities, a project's statuses.
    ///
    /// Separate from <see cref="ResolveAsync"/> because these three endpoints need no Jira facet at
    /// all: they are platform API on both deployments. Going through the facet resolve is what used to
    /// make the status picker on the issue-tracker screen answer a Data Center connection with a
    /// sentence about Assets, leaving the operator to type the workflow's status names from memory.
    /// </summary>
    private async Task<(IssueTrackerConnection Connection, string? Token)> ResolveMetadataAsync(
        int connectionId)
    {
        await using var db = DalService.GetContext();

        var connection = await db.IssueTrackerConnections
                             .FirstOrDefaultAsync(c => c.Id == connectionId)
                         ?? throw new DataNotFoundException("issue tracker connection",
                             connectionId.ToString(),
                             new Exception($"No issue-tracker connection {connectionId}."));

        if (connection.Provider is not (IssueTrackerProviderKind.Jira
            or IssueTrackerProviderKind.JiraDataCenter))
            throw new InvalidParameterException(nameof(connectionId),
                $"Connection '{connection.Name}' is a {connection.Provider} connection. Reading "
                + "fields, priorities and statuses is a Jira feature.");

        return (connection, await resolver.ResolveAsync(connection.EncryptedToken));
    }

    /// <summary>
    /// The settings row, created with defaults if it is missing.
    ///
    /// Created rather than returned as null so no caller and no screen has to carry a
    /// "not configured yet" branch — that branch is a second layout that only the first operator to
    /// open the tab ever sees, which is where the bugs live.
    /// </summary>
    private async Task<JiraConnectionSettings> EnsureSettingsAsync(int connectionId,
        IssueTrackerProviderKind provider)
    {
        await using var db = DalService.GetContext();

        var settings = await db.JiraConnectionSettings
            .Include(s => s.QueueImports)
            .FirstOrDefaultAsync(s => s.ConnectionId == connectionId);

        if (settings != null) return settings;

        settings = new JiraConnectionSettings
        {
            ConnectionId = connectionId,
            Deployment = JiraDialect.DeploymentOf(provider),
            CreatedAt = DateTime.UtcNow
        };

        db.JiraConnectionSettings.Add(settings);
        await db.SaveChangesAsync();

        return settings;
    }

    // --- settings ---------------------------------------------------------------------------

    public async Task<JiraConnectionSettingsView> GetSettingsAsync(int connectionId)
    {
        var provider = await RequireProviderAsync(connectionId);

        await using var db = DalService.GetContext();

        var settings = await db.JiraConnectionSettings
            .Include(s => s.QueueImports)
            .FirstOrDefaultAsync(s => s.ConnectionId == connectionId);

        settings ??= await EnsureSettingsAsync(connectionId, provider);

        return ToView(settings, provider);
    }

    public async Task<JiraConnectionSettingsView> SaveSettingsAsync(int connectionId,
        JiraConnectionSettingsView view)
    {
        var provider = await RequireProviderAsync(connectionId);

        // The one save-time refusal: a Data Center desk would be accepted here and then fail on every
        // sync. Assets is not refused — it is implemented for Data Center.
        if (view.JsmEnabled && !JiraDialect.SupportsServiceManagement(provider))
            throw new InvalidParameterException(nameof(view.JsmEnabled),
                "Service Management is implemented for Jira Cloud only; on a Jira Data Center "
                + "connection only Assets can be enabled.");

        await EnsureSettingsAsync(connectionId, provider);

        await using var db = DalService.GetContext();

        var settings = await db.JiraConnectionSettings
            .Include(s => s.QueueImports)
            .FirstAsync(s => s.ConnectionId == connectionId);

        // Written from the kind, never from the client: the view's Deployment is display-only, so a
        // client cannot make the stored value disagree with the provider the registry resolves.
        settings.Deployment = JiraDialect.DeploymentOf(provider);
        settings.JsmEnabled = view.JsmEnabled;
        settings.ServiceDeskId = view.ServiceDeskId;
        settings.ServiceDeskName = Clip(view.ServiceDeskName, 255);
        settings.RequestTypeFilter = Clip(view.RequestTypeFilter, 512);
        settings.ImportSlas = view.ImportSlas;
        settings.SlaBreachNotifications = view.SlaBreachNotifications;
        settings.DefaultLinkTargetKind = view.DefaultLinkTargetKind;
        settings.AssetsEnabled = view.AssetsEnabled;
        settings.AssetsSchemaId = view.AssetsSchemaId;
        settings.AssetsSchemaName = Clip(view.AssetsSchemaName, 255);
        settings.UpdatedAt = DateTime.UtcNow;

        // The workspace id is discovered, never accepted from the client: a typed one produces 404s
        // from api.atlassian.com that look like an authentication failure. If Assets is being turned
        // on and we do not have one yet, ask the site for it now, so the operator finds out here
        // rather than on the first import. Data Center has no workspace, so there is nothing to ask.
        if (JiraDialect.IsDataCenter(provider))
        {
            settings.AssetsWorkspaceId = null;
        }
        else if (view.AssetsEnabled && string.IsNullOrWhiteSpace(settings.AssetsWorkspaceId))
        {
            var connection = await db.IssueTrackerConnections.FirstAsync(c => c.Id == connectionId);

            settings.AssetsWorkspaceId = await jsm.GetAssetsWorkspaceIdAsync(connection,
                await resolver.ResolveAsync(connection.EncryptedToken));

            if (settings.AssetsWorkspaceId == null)
                Logger.Warning(
                    "Assets was enabled on connection {Connection} but the site reported no Assets "
                    + "workspace. Either the plan does not include Assets or the account cannot see it.",
                    connectionId);
        }

        // Queue selection is replaced wholesale. It is edited as a checkbox list, and a per-row save
        // leaves a selection that is neither the old one nor the one the operator chose. A null list
        // means the client never loaded the queue grid, so the stored selection is kept.
        if (view.QueueImports != null)
        {
            db.JiraQueueImports.RemoveRange(settings.QueueImports);

            foreach (var queue in view.QueueImports.Where(q => q.QueueId > 0)
                         .GroupBy(q => q.QueueId).Select(g => g.First()))
                db.JiraQueueImports.Add(new JiraQueueImport
                {
                    ConnectionId = connectionId,
                    ServiceDeskId = queue.ServiceDeskId > 0
                        ? queue.ServiceDeskId
                        : settings.ServiceDeskId ?? 0,
                    QueueId = queue.QueueId,
                    QueueName = Clip(queue.QueueName, 255),
                    Enabled = queue.Enabled,
                    LinkTargetKind = queue.LinkTargetKind,
                    // Clamped rather than trusted: a client sending 0 would import nothing and a client
                    // sending a million would hold a job for an hour, and neither is what the operator
                    // who typed it meant.
                    MaxRequests = Math.Clamp(queue.MaxRequests, 1, 5000),
                    CreatedAt = DateTime.UtcNow
                });
        }

        await db.SaveChangesAsync();

        return await GetSettingsAsync(connectionId);
    }

    // --- discovery --------------------------------------------------------------------------

    public async Task<List<JiraServiceDeskView>> GetServiceDesksAsync(int connectionId)
    {
        var (connection, token, _) = await ResolveAsync(connectionId, JiraFacet.ServiceManagement);
        return await jsm.GetServiceDesksAsync(connection, token);
    }

    public async Task<List<JiraRequestTypeView>> GetRequestTypesAsync(int connectionId, int serviceDeskId)
    {
        var (connection, token, _) = await ResolveAsync(connectionId, JiraFacet.ServiceManagement);
        return await jsm.GetRequestTypesAsync(connection, token, serviceDeskId);
    }

    public async Task<List<JiraQueueView>> GetQueuesAsync(int connectionId, int serviceDeskId)
    {
        var (connection, token, _) = await ResolveAsync(connectionId, JiraFacet.ServiceManagement);
        return await jsm.GetQueuesAsync(connection, token, serviceDeskId);
    }

    public async Task<List<JiraFieldView>> GetJiraFieldsAsync(int connectionId)
    {
        var (connection, token) = await ResolveMetadataAsync(connectionId);
        return await metadata.GetFieldsAsync(connection, token);
    }

    public async Task<List<string>> GetJiraPrioritiesAsync(int connectionId)
    {
        var (connection, token) = await ResolveMetadataAsync(connectionId);
        return await metadata.GetPrioritiesAsync(connection, token);
    }

    public async Task<List<string>> GetJiraStatusesAsync(int connectionId)
    {
        var (connection, token) = await ResolveMetadataAsync(connectionId);
        return await metadata.GetProjectStatusesAsync(connection, token);
    }

    public async Task<List<JiraObjectSchemaView>> GetAssetSchemasAsync(int connectionId)
    {
        var (connection, token, settings) = await ResolveAsync(connectionId, JiraFacet.Assets);
        var workspace = await AssetsWorkspaceAsync(connection, token, settings);

        return await assets.GetSchemasAsync(connection, token, workspace);
    }

    public async Task<List<JiraObjectTypeView>> GetAssetObjectTypesAsync(int connectionId, int schemaId)
    {
        var (connection, token, settings) = await ResolveAsync(connectionId, JiraFacet.Assets);
        var workspace = await AssetsWorkspaceAsync(connection, token, settings);

        return await assets.GetObjectTypesAsync(connection, token, workspace, schemaId);
    }

    public async Task<List<JiraObjectTypeAttributeView>> GetAssetAttributesAsync(int connectionId,
        int objectTypeId)
    {
        var (connection, token, settings) = await ResolveAsync(connectionId, JiraFacet.Assets);
        var workspace = await AssetsWorkspaceAsync(connection, token, settings);

        return await assets.GetAttributesAsync(connection, token, workspace, objectTypeId);
    }

    public List<MappableFieldView> GetMappableFields(JiraAssetTargetKind? targetKind) =>
        targetKind == null
            ? MappableFields.AllAssetTargets()
            : MappableFields.ForAssetTarget(targetKind.Value);

    /// <summary>
    /// The Assets workspace id, discovering and caching it if this is the first Assets call — or null
    /// on Data Center, which serves Assets from the instance itself and has no workspace.
    ///
    /// Discovered lazily rather than only on save, because an operator who configured Assets before
    /// the site had it provisioned would otherwise have a permanently blank workspace and no way to
    /// retry short of toggling the checkbox off and on.
    /// </summary>
    private async Task<string?> AssetsWorkspaceAsync(IssueTrackerConnection connection, string? token,
        JiraConnectionSettings settings)
    {
        if (JiraDialect.IsDataCenter(connection.Provider)) return null;

        if (!string.IsNullOrWhiteSpace(settings.AssetsWorkspaceId)) return settings.AssetsWorkspaceId;

        var workspace = await jsm.GetAssetsWorkspaceIdAsync(connection, token)
                        ?? throw new IntegrationRequestException("Jira Assets",
                            "The site reported no Assets workspace. Assets requires Jira Service "
                            + "Management Premium or Enterprise, and the account must be able to see "
                            + "it.");

        await using var db = DalService.GetContext();

        var stored = await db.JiraConnectionSettings
            .FirstOrDefaultAsync(s => s.ConnectionId == connection.Id);

        if (stored != null)
        {
            stored.AssetsWorkspaceId = workspace;
            stored.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        settings.AssetsWorkspaceId = workspace;

        return workspace;
    }

    // --- field mapping ----------------------------------------------------------------------

    public async Task<List<JiraFieldMappingView>> GetFieldMappingsAsync(int connectionId)
    {
        await EnsureConnectionExistsAsync(connectionId);

        await using var db = DalService.GetContext();

        return (await db.JiraFieldMappings
                .Where(m => m.ConnectionId == connectionId)
                .OrderBy(m => m.Direction).ThenBy(m => m.JiraFieldName ?? m.JiraFieldId)
                .ToListAsync())
            .Select(ToView).ToList();
    }

    public async Task<List<JiraFieldMappingView>> SetFieldMappingsAsync(int connectionId,
        IReadOnlyList<JiraFieldMappingView> mappings)
    {
        await EnsureConnectionExistsAsync(connectionId);

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.JiraFieldId))
                throw new InvalidParameterException(nameof(mapping.JiraFieldId),
                    "Every field mapping needs a Jira field.");

            // Refused rather than stored, because the alternative is a row an operator configured,
            // saw saved, and which the mapper then skips in silence.
            if (!MappableFields.IsValidIssueSource(mapping.NetRiskField))
                throw new InvalidParameterException(nameof(mapping.NetRiskField),
                    $"'{mapping.NetRiskField}' is not a NetRisk field a Jira mapping can read. "
                    + $"Available: {string.Join(", ", MappableFields.IssueSourceFields)}.");

            if (string.IsNullOrWhiteSpace(mapping.NetRiskField)
                && string.IsNullOrWhiteSpace(mapping.ConstantValue))
                throw new InvalidParameterException(nameof(mapping.NetRiskField),
                    $"The mapping for '{mapping.JiraFieldId}' has neither a NetRisk field nor a "
                    + "constant, so it would write nothing.");
        }

        var duplicate = mappings
            .GroupBy(m => (m.Direction, Field: m.JiraFieldId.ToLowerInvariant()))
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate != null)
            throw new InvalidParameterException(nameof(mappings),
                $"Two {duplicate.Key.Direction} mappings both target '{duplicate.Key.Field}'. "
                + "One writer per field, or the result depends on row order.");

        await using var db = DalService.GetContext();

        db.JiraFieldMappings.RemoveRange(
            await db.JiraFieldMappings.Where(m => m.ConnectionId == connectionId).ToListAsync());

        foreach (var mapping in mappings)
            db.JiraFieldMappings.Add(new JiraFieldMapping
            {
                ConnectionId = connectionId,
                Direction = mapping.Direction,
                NetRiskField = Clip(mapping.NetRiskField, 128) ?? string.Empty,
                JiraFieldId = Clip(mapping.JiraFieldId, 128)!,
                JiraFieldName = Clip(mapping.JiraFieldName, 255),
                JiraFieldType = Clip(mapping.JiraFieldType, 64),
                Transform = mapping.Transform,
                ConstantValue = mapping.ConstantValue,
                Enabled = mapping.Enabled
            });

        await db.SaveChangesAsync();

        Logger.Information("Set {Count} Jira field mapping(s) on connection {Connection}",
            mappings.Count, connectionId);

        return await GetFieldMappingsAsync(connectionId);
    }

    // --- object mapping ---------------------------------------------------------------------

    public async Task<List<JiraObjectMappingView>> GetObjectMappingsAsync(int connectionId)
    {
        await EnsureConnectionExistsAsync(connectionId);

        await using var db = DalService.GetContext();

        return (await db.JiraObjectMappings
                .Include(m => m.AttributeMappings)
                .Where(m => m.ConnectionId == connectionId)
                .OrderBy(m => m.ObjectTypeName)
                .ToListAsync())
            .Select(ToView).ToList();
    }

    public async Task<List<JiraObjectMappingView>> SetObjectMappingsAsync(int connectionId,
        IReadOnlyList<JiraObjectMappingView> mappings, int? userId)
    {
        await EnsureConnectionExistsAsync(connectionId);

        foreach (var mapping in mappings) Validate(mapping);

        var duplicate = mappings.GroupBy(m => m.ObjectTypeId).FirstOrDefault(g => g.Count() > 1);

        if (duplicate != null)
            throw new InvalidParameterException(nameof(mappings),
                $"Object type {duplicate.Key} is mapped twice. One mapping per type, or an import "
                + "would read the same objects twice and the second pass would overwrite the first.");

        await using var db = DalService.GetContext();

        var existing = await db.JiraObjectMappings
            .Include(m => m.AttributeMappings)
            .Where(m => m.ConnectionId == connectionId)
            .ToListAsync();

        // Removed wholesale and re-added, but the LastImportedAt of a surviving object type is
        // carried across: it is the operator's only record of when a register last came in, and
        // resetting it on every settings save would make the Assets tab look like nothing ever ran.
        var lastImported = existing
            .Where(m => m.LastImportedAt != null)
            .ToDictionary(m => m.ObjectTypeId, m => m.LastImportedAt);

        db.JiraObjectMappings.RemoveRange(existing);
        await db.SaveChangesAsync();

        foreach (var mapping in mappings)
        {
            var entity = new JiraObjectMapping
            {
                ConnectionId = connectionId,
                ObjectTypeId = mapping.ObjectTypeId,
                ObjectTypeName = Clip(mapping.ObjectTypeName, 255)!,
                TargetKind = mapping.TargetKind,
                AqlFilter = mapping.AqlFilter,
                MatchStrategy = mapping.MatchStrategy,
                Enabled = mapping.Enabled,
                CreateMissing = mapping.CreateMissing,
                UpdateExisting = mapping.UpdateExisting,
                DeactivateMissing = mapping.DeactivateMissing,
                LastImportedAt = lastImported.GetValueOrDefault(mapping.ObjectTypeId),
                CreatedAt = DateTime.UtcNow,
                CreatedById = userId
            };

            var order = 0;

            foreach (var attribute in mapping.AttributeMappings)
                entity.AttributeMappings.Add(new JiraObjectAttributeMapping
                {
                    SourceAttributeId = attribute.SourceAttributeId,
                    SourceAttributeName = Clip(attribute.SourceAttributeName, 255) ?? string.Empty,
                    TargetField = Clip(attribute.TargetField, 128)!,
                    Transform = attribute.Transform,
                    IsIdentity = attribute.IsIdentity,
                    ConstantValue = attribute.ConstantValue,
                    SortOrder = order++
                });

            db.JiraObjectMappings.Add(entity);
        }

        await db.SaveChangesAsync();

        Logger.Information("Set {Count} Assets object mapping(s) on connection {Connection}",
            mappings.Count, connectionId);

        return await GetObjectMappingsAsync(connectionId);
    }

    /// <summary>
    /// The two configurations that would be accepted and then quietly do nothing, refused at save.
    ///
    /// Both were worth a guard rather than a warning: a mapping with no name target matches nothing
    /// and creates rows with no name, and a target field that does not exist for the kind is simply
    /// skipped by the projector — in each case the operator sees "saved" and then an import that
    /// reports zero.
    /// </summary>
    private static void Validate(JiraObjectMappingView mapping)
    {
        if (mapping.ObjectTypeId <= 0)
            throw new InvalidParameterException(nameof(mapping.ObjectTypeId),
                "Every object mapping needs an Assets object type.");

        foreach (var attribute in mapping.AttributeMappings)
        {
            if (!MappableFields.IsValidAssetTarget(mapping.TargetKind, attribute.TargetField))
                throw new InvalidParameterException(nameof(attribute.TargetField),
                    $"'{attribute.TargetField}' is not a field of a {mapping.TargetKind} target. "
                    + "Available: "
                    + string.Join(", ",
                        MappableFields.ForAssetTarget(mapping.TargetKind).Select(f => f.Name))
                    + ".");

            if (string.IsNullOrWhiteSpace(attribute.SourceAttributeName)
                && attribute.SourceAttributeId == null
                && string.IsNullOrWhiteSpace(attribute.ConstantValue))
                throw new InvalidParameterException(nameof(attribute.SourceAttributeName),
                    $"The mapping for '{attribute.TargetField}' names no Assets attribute and no "
                    + "constant, so it would read nothing.");
        }

        var duplicate = mapping.AttributeMappings
            .GroupBy(a => a.TargetField.ToLowerInvariant())
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate != null)
            throw new InvalidParameterException(nameof(mapping.AttributeMappings),
                $"Two attributes both write '{duplicate.Key}' on object type "
                + $"'{mapping.ObjectTypeName}'. One source per field.");

        if (mapping.AttributeMappings.All(a =>
                !string.Equals(a.TargetField, MappableFields.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidParameterException(nameof(mapping.AttributeMappings),
                $"The mapping for '{mapping.ObjectTypeName}' has no Name target. Without one nothing "
                + "can be matched or created.");
    }

    // --- shared helpers ---------------------------------------------------------------------

    /// <summary>The connection's provider kind, or not-found. The kind decides the deployment.</summary>
    private async Task<IssueTrackerProviderKind> RequireProviderAsync(int connectionId)
    {
        await using var db = DalService.GetContext();

        var provider = await db.IssueTrackerConnections
            .Where(c => c.Id == connectionId)
            .Select(c => (IssueTrackerProviderKind?)c.Provider)
            .FirstOrDefaultAsync();

        return provider ?? throw new DataNotFoundException("issue tracker connection",
            connectionId.ToString(), new Exception($"No issue-tracker connection {connectionId}."));
    }

    private async Task EnsureConnectionExistsAsync(int connectionId)
    {
        await using var db = DalService.GetContext();

        if (!await db.IssueTrackerConnections.AnyAsync(c => c.Id == connectionId))
            throw new DataNotFoundException("issue tracker connection", connectionId.ToString(),
                new Exception($"No issue-tracker connection {connectionId}."));
    }

    private string? BaseUrl => configuration["app:baseUrl"]?.TrimEnd('/');

    /// <summary>
    /// Truncates to a column's width instead of letting the database refuse the row.
    ///
    /// These are third-party strings: an Assets object type may be named anything, and a 300-character
    /// queue name losing its tail is a better outcome than a whole sync dying on one row.
    /// </summary>
    private static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null
            : value.Length <= max ? value.Trim() : value.Trim()[..max];

    private static JiraConnectionSettingsView ToView(JiraConnectionSettings settings,
        IssueTrackerProviderKind provider) => new()
    {
        ConnectionId = settings.ConnectionId,
        // From the kind, so a row written before the kind existed cannot report a deployment the
        // connection does not have.
        Deployment = JiraDialect.DeploymentOf(provider),
        JsmEnabled = settings.JsmEnabled,
        ServiceDeskId = settings.ServiceDeskId,
        ServiceDeskName = settings.ServiceDeskName,
        RequestTypeFilter = settings.RequestTypeFilter,
        ImportSlas = settings.ImportSlas,
        SlaBreachNotifications = settings.SlaBreachNotifications,
        DefaultLinkTargetKind = settings.DefaultLinkTargetKind,
        LastJsmSyncAt = settings.LastJsmSyncAt,
        AssetsEnabled = settings.AssetsEnabled,
        AssetsWorkspaceId = settings.AssetsWorkspaceId,
        AssetsSchemaId = settings.AssetsSchemaId,
        AssetsSchemaName = settings.AssetsSchemaName,
        LastAssetsSyncAt = settings.LastAssetsSyncAt,
        QueueImports = (settings.QueueImports ?? new List<JiraQueueImport>())
            .OrderBy(q => q.QueueName)
            .Select(q => new JiraQueueImportView
            {
                Id = q.Id,
                ServiceDeskId = q.ServiceDeskId,
                QueueId = q.QueueId,
                QueueName = q.QueueName,
                Enabled = q.Enabled,
                LinkTargetKind = q.LinkTargetKind,
                MaxRequests = q.MaxRequests
            }).ToList()
    };

    private static JiraFieldMappingView ToView(JiraFieldMapping mapping) => new()
    {
        Id = mapping.Id,
        Direction = mapping.Direction,
        NetRiskField = mapping.NetRiskField,
        JiraFieldId = mapping.JiraFieldId,
        JiraFieldName = mapping.JiraFieldName,
        JiraFieldType = mapping.JiraFieldType,
        Transform = mapping.Transform,
        ConstantValue = mapping.ConstantValue,
        Enabled = mapping.Enabled
    };

    private static JiraObjectMappingView ToView(JiraObjectMapping mapping) => new()
    {
        Id = mapping.Id,
        ObjectTypeId = mapping.ObjectTypeId,
        ObjectTypeName = mapping.ObjectTypeName,
        TargetKind = mapping.TargetKind,
        AqlFilter = mapping.AqlFilter,
        MatchStrategy = mapping.MatchStrategy,
        Enabled = mapping.Enabled,
        CreateMissing = mapping.CreateMissing,
        UpdateExisting = mapping.UpdateExisting,
        DeactivateMissing = mapping.DeactivateMissing,
        LastImportedAt = mapping.LastImportedAt,
        AttributeMappings = (mapping.AttributeMappings ?? new List<JiraObjectAttributeMapping>())
            .OrderBy(a => a.SortOrder)
            .Select(a => new JiraObjectAttributeMappingView
            {
                Id = a.Id,
                SourceAttributeId = a.SourceAttributeId,
                SourceAttributeName = a.SourceAttributeName,
                TargetField = a.TargetField,
                Transform = a.Transform,
                IsIdentity = a.IsIdentity,
                ConstantValue = a.ConstantValue,
                SortOrder = a.SortOrder
            }).ToList()
    };

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}

/// <summary>The Jira surfaces <c>ResolveAsync</c> gates, each with its own deployment support.</summary>
internal enum JiraFacet
{
    ServiceManagement,
    Assets,
    RecordLinks
}

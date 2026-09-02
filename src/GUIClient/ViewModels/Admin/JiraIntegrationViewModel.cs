using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using Model.Integrations;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Admin;

/// <summary>
/// The Jira-specific half of the integrations screen (Track 4 milestone 4.6): the connection's Jira
/// facet, the field mapping, the Service Management mirror, and the Assets object mapping and import.
///
/// Split out of <see cref="IntegrationsViewModel"/> rather than added to it. That view model is
/// already the largest in the client at some 1,500 lines across five tabs, and four more tabs inside
/// it would make this change unreviewable — the diff would touch the same file as every other
/// integration and nothing would show, at a glance, that only Jira changed.
///
/// Owned by <see cref="IntegrationsViewModel"/> and pointed at whichever connection is selected, so
/// there is one place that decides which connection is being edited.
/// </summary>
public class JiraIntegrationViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrFieldMapping { get; } = Localizer["FieldMapping"];
    public string StrServiceManagement { get; } = Localizer["ServiceManagement"];
    public string StrAssets { get; } = Localizer["JiraAssets"];
    public string StrDeployment { get; } = Localizer["JiraDeployment"];
    public string StrServiceDesk { get; } = Localizer["ServiceDesk"];
    public string StrRequestType { get; } = Localizer["RequestType"];
    public string StrQueues { get; } = Localizer["Queues"];
    public string StrQueue { get; } = Localizer["Queue"];
    public string StrImportRequests { get; } = Localizer["ImportRequests"];
    public string StrMaxRequests { get; } = Localizer["MaxRequests"];
    public string StrImportSlas { get; } = Localizer["ImportSlas"];
    public string StrSlaBreachNotifications { get; } = Localizer["SlaBreachNotifications"];
    public string StrEnableJsm { get; } = Localizer["EnableServiceManagement"];
    public string StrEnableAssets { get; } = Localizer["EnableAssets"];
    public string StrAssetsWorkspace { get; } = Localizer["AssetsWorkspace"];
    public string StrAssetsSchema { get; } = Localizer["AssetsSchema"];
    public string StrObjectType { get; } = Localizer["ObjectType"];
    public string StrTargetKind { get; } = Localizer["TargetKind"];
    public string StrAqlFilter { get; } = Localizer["AqlFilter"];
    public string StrMatchStrategy { get; } = Localizer["MatchStrategy"];
    public string StrCreateMissing { get; } = Localizer["CreateMissing"];
    public string StrUpdateExisting { get; } = Localizer["UpdateExisting"];
    public string StrDeactivateMissing { get; } = Localizer["DeactivateMissing"];
    public string StrAttributeMapping { get; } = Localizer["AttributeMapping"];
    public string StrSourceAttribute { get; } = Localizer["SourceAttribute"];
    public string StrTargetField { get; } = Localizer["TargetField"];
    public string StrTransform { get; } = Localizer["Transform"];
    public string StrIsIdentity { get; } = Localizer["IsIdentity"];
    public string StrConstantValue { get; } = Localizer["ConstantValue"];
    public string StrJiraField { get; } = Localizer["JiraField"];
    public string StrNetRiskField { get; } = Localizer["NetRiskField"];
    public string StrDirection { get; } = Localizer["Direction"];
    public string StrTitleTemplate { get; } = Localizer["TitleTemplate"];
    public string StrDescriptionTemplate { get; } = Localizer["DescriptionTemplate"];
    public string StrPriorityMapping { get; } = Localizer["PriorityMapping"];
    public string StrPreview { get; } = Localizer["Preview"];
    public string StrPreviewImport { get; } = Localizer["PreviewImport"];
    public string StrImportNow { get; } = Localizer["ImportNow"];
    public string StrLoadFromJira { get; } = Localizer["LoadFromJira"];
    public string StrMirroredRequests { get; } = Localizer["MirroredRequests"];
    public string StrBreachedOnly { get; } = Localizer["BreachedOnly"];
    public string StrImportedObjects { get; } = Localizer["ImportedObjects"];
    public string StrMetric { get; } = Localizer["SlaMetric"];
    public string StrRemaining { get; } = Localizer["SlaRemaining"];
    public string StrBreached { get; } = Localizer["Breached"];
    public string StrReporter { get; } = Localizer["Reporter"];
    public string StrEnvironment { get; } = Localizer["Environment"];
    public string StrResponsible { get; } = Localizer["Responsible"];
    public string StrActive { get; } = Localizer["Active"];
    public string StrMatchReason { get; } = Localizer["MatchReason"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrDelete { get; } = Localizer["Delete"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrSyncNow { get; } = Localizer["SyncNow"];
    public string StrName { get; } = Localizer["Name"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrTitle { get; } = Localizer["Title"];
    public string StrLastError { get; } = Localizer["LastError"];
    public string StrEnabled { get; } = Localizer["Enabled"];

    /// <summary>
    /// The note beside the status mapping. It exists because the honest behaviour is surprising: an
    /// inbound status change on an incident or a risk is recorded and displayed and nothing is
    /// transitioned, and an operator who expected otherwise should read it here rather than discover
    /// it from an incident that did not close.
    /// </summary>
    public string StrRecordLinkNote { get; } = Localizer["JiraRecordLinkNoteMSG"];

    public string StrAssetsPlanNote { get; } = Localizer["JiraAssetsPlanNoteMSG"];

    #endregion

    private IIntegrationsService Integrations { get; } = GetService<IIntegrationsService>();

    private int _connectionId;

    /// <summary>
    /// True only for a Jira connection. The whole Jira surface is hidden otherwise, because offering a
    /// service-desk picker on a GitHub connection is offering something that cannot work.
    /// </summary>
    private bool _isJira;
    public bool IsJira
    {
        get => _isJira;
        private set => this.RaiseAndSetIfChanged(ref _isJira, value);
    }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        private set => this.RaiseAndSetIfChanged(ref _busy, value);
    }

    // --- the connection facet ---------------------------------------------------------------

    private JiraConnectionSettingsView _settings = new();
    public JiraConnectionSettingsView Settings
    {
        get => _settings;
        private set => this.RaiseAndSetIfChanged(ref _settings, value);
    }

    public ObservableCollection<JiraDeployment> Deployments { get; } =
        new([JiraDeployment.Cloud, JiraDeployment.DataCenter]);

    public ObservableCollection<JiraServiceDeskView> ServiceDesks { get; } = new();

    public ObservableCollection<JiraRequestTypeView> RequestTypes { get; } = new();

    /// <summary>
    /// Every queue the desk has, each row carrying whether it is imported. One list rather than
    /// "available" and "selected": moving rows between two lists is a worse editor for a checkbox
    /// decision, and it loses the issue counts that make the decision.
    /// </summary>
    public ObservableCollection<QueueSelection> Queues { get; } = new();

    // --- field mapping ----------------------------------------------------------------------

    public ObservableCollection<JiraFieldMappingView> FieldMappings { get; } = new();

    public ObservableCollection<JiraFieldView> JiraFields { get; } = new();

    public ObservableCollection<string> JiraPriorities { get; } = new();

    public ObservableCollection<string> NetRiskFields { get; } = new();

    public ObservableCollection<JiraAttributeTransform> Transforms { get; } =
        new(Enum.GetValues<JiraAttributeTransform>());

    public ObservableCollection<JiraFieldMappingDirection> Directions { get; } =
        new(Enum.GetValues<JiraFieldMappingDirection>());

    private JiraFieldMappingView? _selectedFieldMapping;
    public JiraFieldMappingView? SelectedFieldMapping
    {
        get => _selectedFieldMapping;
        set => this.RaiseAndSetIfChanged(ref _selectedFieldMapping, value);
    }

    // --- Assets -----------------------------------------------------------------------------

    public ObservableCollection<JiraObjectSchemaView> AssetSchemas { get; } = new();

    public ObservableCollection<JiraObjectTypeView> AssetObjectTypes { get; } = new();

    public ObservableCollection<JiraObjectTypeAttributeView> AssetAttributes { get; } = new();

    public ObservableCollection<JiraObjectMappingView> ObjectMappings { get; } = new();

    public ObservableCollection<JiraAssetObjectView> ImportedObjects { get; } = new();

    public ObservableCollection<JiraAssetTargetKind> TargetKinds { get; } =
        new(Enum.GetValues<JiraAssetTargetKind>());

    public ObservableCollection<AssetMatchStrategy> MatchStrategies { get; } =
        new(Enum.GetValues<AssetMatchStrategy>());

    public ObservableCollection<MappableFieldView> TargetFields { get; } = new();

    private JiraObjectMappingView? _selectedObjectMapping;
    public JiraObjectMappingView? SelectedObjectMapping
    {
        get => _selectedObjectMapping;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedObjectMapping, value);
            this.RaisePropertyChanged(nameof(AttributeMappings));
            if (value != null) _ = OnObjectMappingSelectedAsync(value);
        }
    }

    /// <summary>The selected mapping's attribute rows, or an empty list when nothing is selected.</summary>
    public ObservableCollection<JiraObjectAttributeMappingView> AttributeMappings { get; } = new();

    private JiraObjectAttributeMappingView? _selectedAttributeMapping;
    public JiraObjectAttributeMappingView? SelectedAttributeMapping
    {
        get => _selectedAttributeMapping;
        set => this.RaiseAndSetIfChanged(ref _selectedAttributeMapping, value);
    }

    private string _importSummary = "";
    public string ImportSummary
    {
        get => _importSummary;
        private set => this.RaiseAndSetIfChanged(ref _importSummary, value);
    }

    // --- the mirror -------------------------------------------------------------------------

    public ObservableCollection<JiraServiceRequestView> MirroredRequests { get; } = new();

    private bool _breachedOnly;
    public bool BreachedOnly
    {
        get => _breachedOnly;
        set
        {
            this.RaiseAndSetIfChanged(ref _breachedOnly, value);
            _ = LoadMirrorAsync();
        }
    }

    private JiraServiceRequestView? _selectedRequest;
    public JiraServiceRequestView? SelectedRequest
    {
        get => _selectedRequest;
        set => this.RaiseAndSetIfChanged(ref _selectedRequest, value);
    }

    // --- commands ---------------------------------------------------------------------------

    public ReactiveCommand<RxVoid, RxVoid> BtSaveSettingsClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtLoadServiceDesksClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtLoadQueuesClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSyncJsmClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtLoadJiraFieldsClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddFieldMappingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveFieldMappingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveFieldMappingsClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtLoadSchemasClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtLoadObjectTypesClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddObjectMappingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveObjectMappingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddAttributeMappingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveAttributeMappingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveObjectMappingsClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtPreviewImportClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtImportClicked { get; }

    public JiraIntegrationViewModel()
    {
        BtSaveSettingsClicked = ReactiveCommand.CreateFromTask(SaveSettingsAsync);
        BtLoadServiceDesksClicked = ReactiveCommand.CreateFromTask(LoadServiceDesksAsync);
        BtLoadQueuesClicked = ReactiveCommand.CreateFromTask(LoadQueuesAsync);
        BtSyncJsmClicked = ReactiveCommand.CreateFromTask(SyncJsmAsync);
        BtLoadJiraFieldsClicked = ReactiveCommand.CreateFromTask(LoadJiraFieldsAsync);
        BtAddFieldMappingClicked = ReactiveCommand.Create(AddFieldMapping);
        BtRemoveFieldMappingClicked = ReactiveCommand.Create(RemoveFieldMapping);
        BtSaveFieldMappingsClicked = ReactiveCommand.CreateFromTask(SaveFieldMappingsAsync);
        BtLoadSchemasClicked = ReactiveCommand.CreateFromTask(LoadSchemasAsync);
        BtLoadObjectTypesClicked = ReactiveCommand.CreateFromTask(LoadObjectTypesAsync);
        BtAddObjectMappingClicked = ReactiveCommand.Create(AddObjectMapping);
        BtRemoveObjectMappingClicked = ReactiveCommand.Create(RemoveObjectMapping);
        BtAddAttributeMappingClicked = ReactiveCommand.Create(AddAttributeMapping);
        BtRemoveAttributeMappingClicked = ReactiveCommand.Create(RemoveAttributeMapping);
        BtSaveObjectMappingsClicked = ReactiveCommand.CreateFromTask(SaveObjectMappingsAsync);
        BtPreviewImportClicked = ReactiveCommand.CreateFromTask(PreviewImportAsync);
        BtImportClicked = ReactiveCommand.CreateFromTask(ImportAsync);
    }

    /// <summary>
    /// Points this view model at a connection, or clears it.
    ///
    /// Only the stored configuration is loaded here. The live reads — service desks, fields, schemas —
    /// are behind explicit buttons, because each of them spends the connection's credential against
    /// somebody else's API and firing six of them every time an operator clicks a row in the list
    /// would be a rate-limit problem the operator did not ask for.
    /// </summary>
    public async Task LoadAsync(int connectionId, IssueTrackerProviderKind provider)
    {
        _connectionId = connectionId;
        IsJira = connectionId > 0 && provider == IssueTrackerProviderKind.Jira;

        Clear();

        if (!IsJira) return;

        try
        {
            Busy = true;

            Settings = await Integrations.GetJiraSettingsAsync(connectionId);

            foreach (var mapping in await Integrations.GetJiraFieldMappingsAsync(connectionId))
                FieldMappings.Add(mapping);

            foreach (var mapping in await Integrations.GetAssetMappingsAsync(connectionId))
                ObjectMappings.Add(mapping);

            foreach (var field in await Integrations.GetMappableFieldsAsync())
                TargetFields.Add(field);

            foreach (var field in MappableSourceFields()) NetRiskFields.Add(field);

            await LoadMirrorAsync();
            await LoadImportedObjectsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the Jira configuration of connection {Connection}: {Message}",
                connectionId, ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    private void Clear()
    {
        Settings = new JiraConnectionSettingsView { ConnectionId = _connectionId };
        ServiceDesks.Clear();
        RequestTypes.Clear();
        Queues.Clear();
        FieldMappings.Clear();
        JiraFields.Clear();
        JiraPriorities.Clear();
        NetRiskFields.Clear();
        AssetSchemas.Clear();
        AssetObjectTypes.Clear();
        AssetAttributes.Clear();
        ObjectMappings.Clear();
        AttributeMappings.Clear();
        TargetFields.Clear();
        ImportedObjects.Clear();
        MirroredRequests.Clear();
        ImportSummary = "";
        SelectedFieldMapping = null;
        SelectedObjectMapping = null;
        SelectedAttributeMapping = null;
    }

    // --- settings ---------------------------------------------------------------------------

    private async Task SaveSettingsAsync()
    {
        if (!IsJira) return;

        try
        {
            // The queue grid is the source of truth for the selection, so it is projected back onto
            // the settings before the save rather than being kept in sync on every checkbox tick.
            Settings.QueueImports = Queues.Where(q => q.Import)
                .Select(q => new JiraQueueImportView
                {
                    ServiceDeskId = Settings.ServiceDeskId ?? 0,
                    QueueId = q.QueueId,
                    QueueName = q.Name,
                    Enabled = true,
                    MaxRequests = q.MaxRequests
                }).ToList();

            Settings = await Integrations.SaveJiraSettingsAsync(_connectionId, Settings);

            SyncQueueSelection();

            Toasts.Success(Localizer["IntegrationSavedMSG"]);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the Jira settings: {Message}", ex.Message);
            Toasts.Error(ExplainError(ex));
        }
    }

    private async Task LoadServiceDesksAsync()
    {
        if (!IsJira) return;

        await LiveReadAsync(async () =>
        {
            ServiceDesks.Clear();
            foreach (var desk in await Integrations.GetJiraServiceDesksAsync(_connectionId))
                ServiceDesks.Add(desk);
        }, "the service desks");
    }

    private async Task LoadQueuesAsync()
    {
        if (!IsJira || Settings.ServiceDeskId is not { } serviceDeskId) return;

        await LiveReadAsync(async () =>
        {
            RequestTypes.Clear();
            foreach (var type in await Integrations.GetJiraRequestTypesAsync(_connectionId, serviceDeskId))
                RequestTypes.Add(type);

            Queues.Clear();

            foreach (var queue in await Integrations.GetJiraQueuesAsync(_connectionId, serviceDeskId))
                Queues.Add(new QueueSelection
                {
                    QueueId = queue.Id,
                    Name = queue.Name,
                    IssueCount = queue.IssueCount
                });

            SyncQueueSelection();
        }, "the queues");
    }

    /// <summary>Ticks the queues the stored settings already import, after a live queue list arrives.</summary>
    private void SyncQueueSelection()
    {
        foreach (var queue in Queues)
        {
            var stored = Settings.QueueImports.FirstOrDefault(q => q.QueueId == queue.QueueId);

            queue.Import = stored != null;
            if (stored != null) queue.MaxRequests = stored.MaxRequests;
        }
    }

    private async Task SyncJsmAsync()
    {
        if (!IsJira) return;

        try
        {
            Busy = true;

            var result = await Integrations.SyncJiraServiceManagementAsync(_connectionId);

            Toasts.Success(
                $"{result.RequestsExamined} request(s), {result.RequestsCreated} new, "
                + $"{result.Breaches} new SLA breach(es), {result.Errors} error(s).");

            await LoadMirrorAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("The Jira Service Management sync failed: {Message}", ex.Message);
            Toasts.Error(ExplainError(ex));
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LoadMirrorAsync()
    {
        if (!IsJira) return;

        try
        {
            var requests = await Integrations.GetJiraRequestsAsync(_connectionId, BreachedOnly);

            MirroredRequests.Clear();
            foreach (var request in requests) MirroredRequests.Add(request);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the Jira request mirror: {Message}", ex.Message);
        }
    }

    // --- field mapping ----------------------------------------------------------------------

    private async Task LoadJiraFieldsAsync()
    {
        if (!IsJira) return;

        await LiveReadAsync(async () =>
        {
            JiraFields.Clear();
            foreach (var field in await Integrations.GetJiraFieldsAsync(_connectionId))
                JiraFields.Add(field);

            JiraPriorities.Clear();
            foreach (var priority in await Integrations.GetJiraPrioritiesAsync(_connectionId))
                JiraPriorities.Add(priority);
        }, "the Jira fields and priorities");
    }

    private void AddFieldMapping()
    {
        var mapping = new JiraFieldMappingView
        {
            Direction = JiraFieldMappingDirection.Outbound,
            Enabled = true
        };

        FieldMappings.Add(mapping);
        SelectedFieldMapping = mapping;
    }

    private void RemoveFieldMapping()
    {
        if (SelectedFieldMapping == null) return;

        FieldMappings.Remove(SelectedFieldMapping);
        SelectedFieldMapping = null;
    }

    private async Task SaveFieldMappingsAsync()
    {
        if (!IsJira) return;

        try
        {
            var saved = await Integrations.SetJiraFieldMappingsAsync(_connectionId,
                FieldMappings.ToList());

            FieldMappings.Clear();
            foreach (var mapping in saved) FieldMappings.Add(mapping);

            Toasts.Success(Localizer["IntegrationSavedMSG"]);
        }
        catch (Exception ex)
        {
            // The server refuses a mapping that would be stored and then silently do nothing — two
            // rows on one Jira field, or a row with neither a source nor a constant. Its message names
            // the problem, so it is shown rather than replaced with a generic failure.
            Logger.Error("Could not save the Jira field mappings: {Message}", ex.Message);
            Toasts.Error(ExplainError(ex));
        }
    }

    // --- Assets object mapping --------------------------------------------------------------

    private async Task LoadSchemasAsync()
    {
        if (!IsJira) return;

        await LiveReadAsync(async () =>
        {
            AssetSchemas.Clear();
            foreach (var schema in await Integrations.GetAssetSchemasAsync(_connectionId))
                AssetSchemas.Add(schema);
        }, "the Assets schemas");
    }

    private async Task LoadObjectTypesAsync()
    {
        if (!IsJira || Settings.AssetsSchemaId is not { } schemaId) return;

        await LiveReadAsync(async () =>
        {
            AssetObjectTypes.Clear();
            foreach (var type in await Integrations.GetAssetObjectTypesAsync(_connectionId, schemaId))
                AssetObjectTypes.Add(type);
        }, "the Assets object types");
    }

    /// <summary>
    /// Loads the selected type's attributes and its target-field list.
    ///
    /// The attributes are a live read because the picker has to offer the customer's own attribute
    /// names — the whole point of the mapping editor is that nobody types <c>Owner</c> from memory and
    /// hopes it matches.
    /// </summary>
    private async Task OnObjectMappingSelectedAsync(JiraObjectMappingView mapping)
    {
        AttributeMappings.Clear();
        foreach (var attribute in mapping.AttributeMappings) AttributeMappings.Add(attribute);

        TargetFields.Clear();
        foreach (var field in await SafeTargetFieldsAsync(mapping.TargetKind)) TargetFields.Add(field);

        if (mapping.ObjectTypeId <= 0) return;

        try
        {
            AssetAttributes.Clear();
            foreach (var attribute in await Integrations.GetAssetAttributesAsync(_connectionId,
                         mapping.ObjectTypeId))
                AssetAttributes.Add(attribute);
        }
        catch (Exception ex)
        {
            // Not surfaced as a toast: selecting a row in a grid is not an action the operator asked
            // to have fail, and the mapping rows they already have are still editable without the
            // picker. The Load button is there to retry it deliberately.
            Logger.Warning("Could not load the attributes of Assets object type {Type}: {Message}",
                mapping.ObjectTypeId, ex.Message);
        }
    }

    private async Task<List<MappableFieldView>> SafeTargetFieldsAsync(JiraAssetTargetKind kind)
    {
        try
        {
            return await Integrations.GetMappableFieldsAsync(kind);
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not load the mappable fields for {Kind}: {Message}", kind, ex.Message);
            return [];
        }
    }

    private void AddObjectMapping()
    {
        var mapping = new JiraObjectMappingView
        {
            TargetKind = JiraAssetTargetKind.Host,
            MatchStrategy = AssetMatchStrategy.ExternalIdThenIdentity,
            Enabled = true,
            CreateMissing = true,
            UpdateExisting = true
        };

        ObjectMappings.Add(mapping);
        SelectedObjectMapping = mapping;
    }

    private void RemoveObjectMapping()
    {
        if (SelectedObjectMapping == null) return;

        ObjectMappings.Remove(SelectedObjectMapping);
        SelectedObjectMapping = null;
        AttributeMappings.Clear();
    }

    private void AddAttributeMapping()
    {
        if (SelectedObjectMapping == null) return;

        var attribute = new JiraObjectAttributeMappingView
        {
            SortOrder = AttributeMappings.Count
        };

        AttributeMappings.Add(attribute);
        SelectedAttributeMapping = attribute;
    }

    private void RemoveAttributeMapping()
    {
        if (SelectedAttributeMapping == null) return;

        AttributeMappings.Remove(SelectedAttributeMapping);
        SelectedAttributeMapping = null;
    }

    private async Task SaveObjectMappingsAsync()
    {
        if (!IsJira) return;

        try
        {
            // The attribute grid edits the selected mapping's rows, so they are written back before
            // the save. Without this, editing a mapping's attributes and pressing Save would store
            // the mapping and discard exactly the change the operator just made.
            if (SelectedObjectMapping != null)
                SelectedObjectMapping.AttributeMappings = AttributeMappings.ToList();

            var saved = await Integrations.SetAssetMappingsAsync(_connectionId,
                ObjectMappings.ToList());

            ObjectMappings.Clear();
            foreach (var mapping in saved) ObjectMappings.Add(mapping);

            SelectedObjectMapping = null;
            AttributeMappings.Clear();

            Toasts.Success(Localizer["IntegrationSavedMSG"]);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the Assets object mappings: {Message}", ex.Message);
            Toasts.Error(ExplainError(ex));
        }
    }

    private Task PreviewImportAsync() => RunImportAsync(dryRun: true);

    private Task ImportAsync() => RunImportAsync(dryRun: false);

    private async Task RunImportAsync(bool dryRun)
    {
        if (!IsJira) return;

        try
        {
            Busy = true;

            var result = dryRun
                ? await Integrations.PreviewAssetImportAsync(_connectionId)
                : await Integrations.ImportAssetsAsync(_connectionId);

            ImportSummary =
                $"{(dryRun ? "Preview" : "Import")}: {result.Examined} object(s) examined, "
                + $"{result.Created} created, {result.Updated} updated, "
                + $"{result.Deactivated} retired, {result.Errors} error(s)."
                + (result.Messages.Count == 0
                    ? ""
                    : "\n" + string.Join("\n", result.Messages.Take(20)));

            ImportedObjects.Clear();

            // A dry run has written nothing, so the grid shows the sample it computed; a real import
            // has, so it shows what is stored. Showing the sample after a real import would hide the
            // objects beyond the first twenty.
            if (dryRun)
                foreach (var sample in result.Sample) ImportedObjects.Add(sample);
            else
                await LoadImportedObjectsAsync();

            Toasts.Success(ImportSummary.Split('\n')[0]);
        }
        catch (Exception ex)
        {
            Logger.Error("The Assets import failed: {Message}", ex.Message);
            Toasts.Error(ExplainError(ex));
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LoadImportedObjectsAsync()
    {
        try
        {
            var objects = await Integrations.GetAssetObjectsAsync(_connectionId);

            ImportedObjects.Clear();
            foreach (var item in objects) ImportedObjects.Add(item);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the imported Assets objects: {Message}", ex.Message);
        }
    }

    // --- helpers ----------------------------------------------------------------------------

    /// <summary>
    /// Runs a live read against Jira with the busy flag and one error path.
    ///
    /// Every one of these spends the connection's credential against a third party, so the failure an
    /// operator sees has to be the upstream one — "Assets needs Premium", "the project key is wrong" —
    /// and not "loading failed".
    /// </summary>
    private async Task LiveReadAsync(Func<Task> read, string what)
    {
        try
        {
            Busy = true;
            await read();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read {What} from Jira: {Message}", what, ex.Message);
            Toasts.Error(ExplainError(ex));
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// The NetRisk placeholders a Jira field mapping may read — the same vocabulary as the title and
    /// description templates, so an operator learns one list.
    /// </summary>
    private static IEnumerable<string> MappableSourceFields() =>
    [
        "", "FindingId", "Title", "Severity", "RawSeverity", "Status", "Description", "Evidence",
        "Asset", "Component", "Location", "Cves", "Cwes", "Cvss", "FirstDetection", "SlaDueDate",
        "FixedInVersion", "RuleId", "Link"
    ];
}

/// <summary>
/// One row of the queue picker: the queue, its advertised issue count, and whether it feeds the mirror.
///
/// A view-model type rather than the server's <see cref="JiraQueueView"/>, because the grid edits two
/// fields the server's read model has no place for — the checkbox and the per-queue ceiling.
/// </summary>
public class QueueSelection : ReactiveObject
{
    public int QueueId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>What Jira says is in the queue. The number that makes the ceiling below a decision.</summary>
    public int? IssueCount { get; set; }

    private bool _import;
    public bool Import
    {
        get => _import;
        set => this.RaiseAndSetIfChanged(ref _import, value);
    }

    private int _maxRequests = 500;
    public int MaxRequests
    {
        get => _maxRequests;
        set => this.RaiseAndSetIfChanged(ref _maxRequests, value);
    }
}

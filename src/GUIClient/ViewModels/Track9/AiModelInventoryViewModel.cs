using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.AiGovernance;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class AiMetricRow
{
    public AiModelMetricStateDto State { get; }
    public string MetricText { get; }
    public string StateText { get; }
    public string ValueText { get; }
    public string PreviousVersionText { get; }

    public AiMetricRow(AiModelMetricStateDto state)
    {
        State = state;
        MetricText = ViewModelBase.Localizer[$"Track9AiModelMetric{state.Metric}"];
        StateText = ViewModelBase.Localizer[Track9AiGovernanceForms.MetricStateKey(state)];
        ValueText = state.Value?.ToString("0.######", CultureInfo.CurrentCulture) ?? string.Empty;
        PreviousVersionText = Track9AiGovernanceForms.PreviousVersion(state) ?? string.Empty;
    }
}

public sealed class AiModelSummaryRow
{
    public AiModelSummaryDto Model { get; }
    public string Name => Model.Name;
    public string Version => Model.Version;
    public string StatusText { get; }
    public string EvaluationStateText { get; }
    public int FindingCount => Model.FindingCodes.Count;

    public AiModelSummaryRow(AiModelSummaryDto model)
    {
        Model = model;
        StatusText = ViewModelBase.Localizer[$"Track9AiModelStatus{model.Status}"];
        EvaluationStateText = ViewModelBase.Localizer[$"Track9AiModelEvaluationState{model.EvaluationState}"];
    }
}

public sealed class AiReadingRow
{
    public AiModelReadingDto Reading { get; }
    public string MetricText { get; }

    public AiReadingRow(AiModelReadingDto reading)
    {
        Reading = reading;
        MetricText = ViewModelBase.Localizer[$"Track9AiModelMetric{reading.Metric}"];
    }
}

public sealed class AiDataDraftRow
{
    public AiModelDataLinkRequest Request { get; }
    public string EntityName { get; }
    public string UsageText { get; }

    public AiDataDraftRow(AiModelDataLinkRequest request, string entityName)
    {
        Request = request;
        EntityName = entityName;
        UsageText = request.Usage is null ? string.Empty
            : ViewModelBase.Localizer[$"Track9AiModelDataUsage{request.Usage}"];
    }
}

/// <summary>
/// Stage 9.12 AI model inventory. Evaluation state is presented explicitly: a missing reading is
/// “not evaluated” with no numeric value, and a version change exposes the previous evaluated version.
/// </summary>
public class AiModelInventoryViewModel : ViewModelBase
{
    private readonly IAiGovernanceService _service;
    private readonly IEntitiesService _entitiesService;
    private readonly IUsersService _usersService;
    private readonly IThirdPartiesService _thirdPartiesService;
    private readonly Track9RegistersLoadGate _detailLoadGate = new();
    private readonly Track9RegistersLoadGate _listLoadGate = new();
    private bool _loaded;

    public string StrTitle { get; } = Localizer["Track9AiModelInventory"];
    public string StrRefresh { get; } = Localizer["Refresh"];
    public string StrNew { get; } = Localizer["New"];
    public string StrOpen { get; } = Localizer["Open"];
    public string StrRetire { get; } = Localizer["Track9Retire"];
    public string StrInventory { get; } = Localizer["Track9Inventory"];
    public string StrData { get; } = Localizer["Data"];
    public string StrEvaluation { get; } = Localizer["Track9Evaluation"];
    public string StrReadings { get; } = Localizer["Track9MetricReadings"];
    public string StrOverrides { get; } = Localizer["Track9HumanOverrides"];
    public string StrRisks { get; } = Localizer["Risks"];
    public string StrFindings { get; } = Localizer["Findings"];
    public string StrFindingsOnly { get; } = Localizer["Track9FindingsOnly"];
    public string StrIncludeRetired { get; } = Localizer["Track9IncludeRetired"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrRemove { get; } = Localizer["Remove"];
    public string StrVoid { get; } = Localizer["Track9Void"];
    public string StrSaved { get; } = Localizer["Track9Saved"];
    public string StrRetired { get; } = Localizer["Track9Retired"];
    public string StrVoided { get; } = Localizer["Track9Voided"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrNoData { get; } = Localizer["Track9NoData"];
    public string StrName { get; } = Localizer["Name"];
    public string StrPurpose { get; } = Localizer["Track9Purpose"];
    public string StrKind { get; } = Localizer["Track9Kind"];
    public string StrSource { get; } = Localizer["Track9Source"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrRiskTier { get; } = Localizer["Track9RiskTier"];
    public string StrOversight { get; } = Localizer["Track9HumanOversight"];
    public string StrOwner { get; } = Localizer["Track9Owner"];
    public string StrEntity { get; } = Localizer["Track9Entity"];
    public string StrVendor { get; } = Localizer["Track9Vendor"];
    public string StrVersion { get; } = Localizer["Track9Version"];
    public string StrVersionSince { get; } = Localizer["Track9VersionSince"];
    public string StrMaxEvaluationAge { get; } = Localizer["Track9MaxEvaluationAge"];
    public string StrNotes { get; } = Localizer["Track9Notes"];
    public string StrDataRecord { get; } = Localizer["Track9DataRecord"];
    public string StrDataUsage { get; } = Localizer["Track9DataUsage"];
    public string StrMetric { get; } = Localizer["Track9Metric"];
    public string StrValue { get; } = Localizer["Track9Value"];
    public string StrMeasuredAt { get; } = Localizer["Track9MeasuredAt"];
    public string StrPeriodStart { get; } = Localizer["Track9PeriodStart"];
    public string StrPeriodEnd { get; } = Localizer["Track9PeriodEnd"];
    public string StrSampleSize { get; } = Localizer["Track9SampleSize"];
    public string StrMethod { get; } = Localizer["Track9Method"];
    public string StrEvidence { get; } = Localizer["Track9Evidence"];
    public string StrOccurredAt { get; } = Localizer["Track9OccurredAt"];
    public string StrModelOutput { get; } = Localizer["Track9ModelOutput"];
    public string StrHumanDecision { get; } = Localizer["Track9HumanDecision"];
    public string StrReason { get; } = Localizer["Track9Reason"];
    public string StrEvaluationState { get; } = Localizer["Track9EvaluationState"];
    public string StrRequired { get; } = Localizer["Track9Required"];
    public string StrPreviousVersion { get; } = Localizer["Track9PreviousVersion"];
    public string StrRiskReference { get; } = Localizer["Track9RiskReference"];
    public string StrSubject { get; } = Localizer["Track9Subject"];

    public IReadOnlyList<Track9RegisterOption<AiModelKind>> KindOptions { get; } = EnumOptions<AiModelKind>();
    public IReadOnlyList<Track9RegisterOption<AiModelSource>> SourceOptions { get; } = EnumOptions<AiModelSource>();
    public IReadOnlyList<Track9RegisterOption<AiModelStatus>> StatusOptions { get; } = EnumOptions<AiModelStatus>();
    public IReadOnlyList<Track9RegisterOption<AiModelRiskTier>> RiskTierOptions { get; } = EnumOptions<AiModelRiskTier>();
    public IReadOnlyList<Track9RegisterOption<AiHumanOversight>> OversightOptions { get; } = EnumOptions<AiHumanOversight>();
    public IReadOnlyList<Track9RegisterOption<AiModelDataUsage>> DataUsageOptions { get; } = EnumOptions<AiModelDataUsage>();
    public IReadOnlyList<Track9RegisterOption<AiModelMetric>> MetricOptions { get; } = EnumOptions<AiModelMetric>();

    public ObservableCollection<AiModelSummaryRow> Items { get; } = [];
    public bool IsEmpty => _loaded && Items.Count == 0;
    public ObservableCollection<AiDataDraftRow> DataDrafts { get; } = [];
    public ObservableCollection<AiMetricRow> MetricRows { get; } = [];
    public ObservableCollection<AiReadingRow> Readings { get; } = [];
    public ObservableCollection<AiModelOverrideDto> Overrides { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> UserOptions { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> EntityOptions { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> ThirdPartyOptions { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> DataOptions { get; } = [];

    private AiModelStatus? _statusFilter;
    public AiModelStatus? StatusFilter
    {
        get => _statusFilter;
        set => this.RaiseAndSetIfChanged(ref _statusFilter, value);
    }
    public bool IncludeRetired { get; set; }
    public bool WithFindingsOnly { get; set; }

    private AiModelSummaryRow? _selectedItem;
    public AiModelSummaryRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!ReferenceEquals(_selectedItem, value)) _detailLoadGate.DiscardOutstanding();
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
        }
    }

    private AiModelDto? _selectedModel;
    public AiModelDto? SelectedModel
    {
        get => _selectedModel;
        private set
        {
            this.RaiseAndSetIfChanged(ref _selectedModel, value);
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(SelectedEvaluationStateText));
        }
    }
    public bool HasSelection => SelectedModel is not null;
    public string SelectedEvaluationStateText => SelectedModel is null
        ? string.Empty
        : Localizer[$"Track9AiModelEvaluationState{SelectedModel.Evaluation.State}"];
    public bool CanManage => Track9RegistersAccess.CanManageAiGovernance(AuthenticationService.AuthenticatedUserInfo);
    public bool IsThirdLine => Track9RegistersAccess.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public string WriteDisabledReason => Localizer[IsThirdLine ? "Track9ThirdLineReadOnly" : "Track9ReadOnly"];

    private AiModelDto _editor = NewEditor();
    public AiModelDto Editor
    {
        get => _editor;
        private set
        {
            this.RaiseAndSetIfChanged(ref _editor, value);
            this.RaisePropertyChanged(nameof(SelectedEditorKind));
            this.RaisePropertyChanged(nameof(SelectedEditorSource));
            this.RaisePropertyChanged(nameof(SelectedEditorStatus));
            this.RaisePropertyChanged(nameof(SelectedEditorRiskTier));
            this.RaisePropertyChanged(nameof(SelectedEditorOversight));
            this.RaisePropertyChanged(nameof(SelectedEditorOwnerId));
            this.RaisePropertyChanged(nameof(SelectedEditorEntityId));
            this.RaisePropertyChanged(nameof(SelectedEditorThirdPartyId));
        }
    }
    public AiModelKind? SelectedEditorKind { get => Editor.Kind; set { if (value is null) return; Editor.Kind = value.Value; this.RaisePropertyChanged(); } }
    public AiModelSource? SelectedEditorSource { get => Editor.Source; set { if (value is null) return; Editor.Source = value.Value; this.RaisePropertyChanged(); } }
    public AiModelStatus? SelectedEditorStatus { get => Editor.Status; set { if (value is null) return; Editor.Status = value.Value; this.RaisePropertyChanged(); } }
    public AiModelRiskTier? SelectedEditorRiskTier { get => Editor.RiskTier; set { if (value is null) return; Editor.RiskTier = value; this.RaisePropertyChanged(); } }
    public AiHumanOversight? SelectedEditorOversight { get => Editor.HumanOversight; set { if (value is null) return; Editor.HumanOversight = value; this.RaisePropertyChanged(); } }
    public int? SelectedEditorOwnerId { get => Editor.OwnerId; set { if (value is null) return; Editor.OwnerId = value; this.RaisePropertyChanged(); } }
    public int? SelectedEditorEntityId { get => Editor.EntityId; set { if (value is null) return; Editor.EntityId = value; this.RaisePropertyChanged(); } }
    public int? SelectedEditorThirdPartyId { get => Editor.ThirdPartyId; set { if (value is null) return; Editor.ThirdPartyId = value; this.RaisePropertyChanged(); } }

    public AiModelDataLinkRequest NewDataLink { get; private set; } = new();
    public int? SelectedDataEntityId
    {
        get => NewDataLink.EntityId;
        set
        {
            if (value is null || value == NewDataLink.EntityId) return;
            NewDataLink.EntityId = value;
            this.RaisePropertyChanged();
        }
    }
    public AiModelDataUsage? SelectedDataUsage
    {
        get => NewDataLink.Usage;
        set
        {
            if (value is null || value == NewDataLink.Usage) return;
            NewDataLink.Usage = value;
            this.RaisePropertyChanged();
        }
    }
    public AiDataDraftRow? SelectedDataLink { get; set; }

    public AiModelMetric ReadingMetric { get; set; } = AiModelMetric.Drift;
    public decimal? ReadingValue { get; set; }
    public DateTime? ReadingMeasuredAt { get; set; }
    public TimeSpan? ReadingMeasuredTime
    {
        get => ReadingMeasuredAt?.TimeOfDay;
        set
        {
            if (value is null) return;
            ReadingMeasuredAt = (ReadingMeasuredAt ?? DateTime.Today).Date + value.Value;
            this.RaisePropertyChanged(nameof(ReadingMeasuredAt));
            this.RaisePropertyChanged();
        }
    }
    public DateTime? ReadingPeriodStart { get; set; }
    public DateTime? ReadingPeriodEnd { get; set; }
    public int? ReadingSampleSize { get; set; }
    public string? ReadingMethod { get; set; }
    public string? ReadingEvidence { get; set; }
    public AiReadingRow? SelectedReading { get; set; }

    public AiModelOverrideRequest OverrideDraft { get; private set; } = new();
    public DateTime? OverrideOccurredDate
    {
        get => OverrideDraft.OccurredAt;
        set
        {
            if (value is null) return;
            var time = OverrideDraft.OccurredAt?.TimeOfDay ?? TimeSpan.Zero;
            OverrideDraft.OccurredAt = value.Value.Date + time;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(OverrideOccurredTime));
        }
    }
    public TimeSpan? OverrideOccurredTime
    {
        get => OverrideDraft.OccurredAt?.TimeOfDay;
        set
        {
            if (value is null) return;
            OverrideDraft.OccurredAt = (OverrideDraft.OccurredAt ?? DateTime.Today).Date + value.Value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(OverrideOccurredDate));
        }
    }
    public AiModelOverrideDto? SelectedOverride { get; set; }
    public string? VoidReason { get; set; }
    public string? RetireReason { get; set; }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RetireCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddDataCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveDataCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveDataCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RecordReadingCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> VoidReadingCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RecordOverrideCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> VoidOverrideCommand { get; }

    public AiModelInventoryViewModel() : this(GetService<IAiGovernanceService>(), GetService<IEntitiesService>(),
        GetService<IUsersService>(), GetService<IThirdPartiesService>()) { }
    internal AiModelInventoryViewModel(IAiGovernanceService service) : this(service, GetService<IEntitiesService>(),
        GetService<IUsersService>(), GetService<IThirdPartiesService>()) { }
    internal AiModelInventoryViewModel(IAiGovernanceService service, IEntitiesService entitiesService,
        IUsersService usersService, IThirdPartiesService thirdPartiesService)
    {
        _service = service;
        _entitiesService = entitiesService;
        _usersService = usersService;
        _thirdPartiesService = thirdPartiesService;
        ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
        NewCommand = ReactiveCommand.Create(NewRecord);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        RetireCommand = ReactiveCommand.CreateFromTask(RetireAsync);
        AddDataCommand = ReactiveCommand.Create(AddData);
        RemoveDataCommand = ReactiveCommand.Create(RemoveData);
        SaveDataCommand = ReactiveCommand.CreateFromTask(SaveDataAsync);
        RecordReadingCommand = ReactiveCommand.CreateFromTask(RecordReadingAsync);
        VoidReadingCommand = ReactiveCommand.CreateFromTask(VoidReadingAsync);
        RecordOverrideCommand = ReactiveCommand.CreateFromTask(RecordOverrideAsync);
        VoidOverrideCommand = ReactiveCommand.CreateFromTask(VoidOverrideAsync);
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded) await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        var generation = _listLoadGate.Begin();
        await WithBusyAsync(async () =>
        {
            try
            {
                var items = await _service.GetModelsAsync(StatusFilter, IncludeRetired, WithFindingsOnly);
                if (!_listLoadGate.IsCurrent(generation)) return;
                Replace(Items, items.Select(item => new AiModelSummaryRow(item)));
                ErrorMessage = null;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
                await LoadLookupOptionsAsync(generation);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load the AI model inventory");
                if (!_listLoadGate.IsCurrent(generation)) return;
                ErrorMessage = StrLoadError;
            }
        });
    }

    private void NewRecord()
    {
        if (!CanManage) return;
        _detailLoadGate.DiscardOutstanding();
        SelectedItem = null;
        SelectedModel = null;
        Editor = NewEditor();
        DataDrafts.Clear();
        MetricRows.Clear();
        Readings.Clear();
        Overrides.Clear();
    }

    private async Task OpenAsync()
    {
        if (SelectedItem is null) return;
        await LoadModelAsync(SelectedItem.Model.Id);
    }

    private async Task LoadModelAsync(int id)
    {
        var generation = _detailLoadGate.Begin();
        await WithBusyAsync(async () =>
        {
            try
            {
                var model = await _service.GetModelAsync(id);
                if (!_detailLoadGate.IsCurrent(generation)) return;
                var readings = await _service.GetReadingsAsync(id, includeVoided: true);
                if (!_detailLoadGate.IsCurrent(generation)) return;
                var overrides = await _service.GetOverridesAsync(id, includeVoided: true);
                if (!_detailLoadGate.IsCurrent(generation)) return;
                SelectedModel = model;
                EnsureThirdPartyOption(model.ThirdPartyId, model.ThirdPartyName);
                Editor = model;
                Replace(DataDrafts, model.Data.Select(data => DataRow(new AiModelDataLinkRequest
                {
                    EntityId = data.EntityId,
                    Usage = data.Usage
                })));
                Replace(MetricRows, model.Evaluation.Metrics.Select(metric => new AiMetricRow(metric)));
                Replace(Readings, readings.Select(reading => new AiReadingRow(reading)));
                Replace(Overrides, overrides);
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load AI model {ModelId}", id);
                if (!_detailLoadGate.IsCurrent(generation)) return;
                ClearDetail();
                ErrorMessage = ExplainError(ex);
            }
        });
    }

    private async Task SaveAsync()
    {
        if (!CanManage) return;
        var id = SelectedModel?.Id;
        var request = Track9AiGovernanceForms.BuildRequest(Editor);
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            var saved = id is null
                ? await _service.CreateModelAsync(request)
                : await _service.UpdateModelAsync(id.Value, request);
            await ReloadAsync();
            if (_detailLoadGate.IsCurrent(generation)) await LoadModelAsync(saved.Id);
        });
    }

    private async Task RetireAsync()
    {
        if (!CanManage || SelectedModel is null) return;
        var id = SelectedModel.Id;
        var request = new AiGovernanceReasonRequest { Reason = RetireReason };
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrRetired, async () =>
        {
            await _service.RetireModelAsync(id, request);
            await ReloadAsync();
            if (_detailLoadGate.IsCurrent(generation)) await LoadModelAsync(id);
        });
    }

    private void AddData()
    {
        if (!CanManage) return;
        if (NewDataLink.EntityId is null || NewDataLink.Usage is null) return;
        DataDrafts.Add(DataRow(NewDataLink));
        NewDataLink = new AiModelDataLinkRequest();
        this.RaisePropertyChanged(nameof(NewDataLink));
        this.RaisePropertyChanged(nameof(SelectedDataEntityId));
        this.RaisePropertyChanged(nameof(SelectedDataUsage));
    }

    private void RemoveData()
    {
        if (!CanManage) return;
        if (SelectedDataLink is not null) DataDrafts.Remove(SelectedDataLink);
    }

    private async Task SaveDataAsync()
    {
        if (!CanManage || SelectedModel is null) return;
        var id = SelectedModel.Id;
        var request = new AiModelDataRequest { Data = DataDrafts.Select(row => row.Request).ToList() };
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            await _service.SetDataAsync(id, request);
            if (_detailLoadGate.IsCurrent(generation)) await LoadModelAsync(id);
        });
    }

    private async Task RecordReadingAsync()
    {
        if (!CanManage || SelectedModel is null) return;
        var id = SelectedModel.Id;
        var request = Track9AiGovernanceForms.BuildReading(ReadingMetric, ReadingValue, ReadingMeasuredAt,
            ReadingPeriodStart, ReadingPeriodEnd, ReadingSampleSize, ReadingMethod, ReadingEvidence);
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            await _service.RecordReadingAsync(id, request);
            if (_detailLoadGate.IsCurrent(generation)) await LoadModelAsync(id);
        });
    }

    private async Task VoidReadingAsync()
    {
        if (!CanManage || SelectedModel is null || SelectedReading is null) return;
        var id = SelectedModel.Id;
        var readingId = SelectedReading.Reading.Id;
        var request = new AiGovernanceReasonRequest { Reason = VoidReason };
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrVoided, async () =>
        {
            await _service.VoidReadingAsync(id, readingId, request);
            if (_detailLoadGate.IsCurrent(generation)) await LoadModelAsync(id);
        });
    }

    private async Task RecordOverrideAsync()
    {
        if (!CanManage || SelectedModel is null) return;
        var id = SelectedModel.Id;
        var request = OverrideDraft;
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            await _service.RecordOverrideAsync(id, request);
            if (_detailLoadGate.IsCurrent(generation))
            {
                OverrideDraft = new AiModelOverrideRequest();
                this.RaisePropertyChanged(nameof(OverrideDraft));
                this.RaisePropertyChanged(nameof(OverrideOccurredDate));
                this.RaisePropertyChanged(nameof(OverrideOccurredTime));
                await LoadModelAsync(id);
            }
        });
    }

    private async Task VoidOverrideAsync()
    {
        if (!CanManage || SelectedModel is null || SelectedOverride is null) return;
        var id = SelectedModel.Id;
        var overrideId = SelectedOverride.Id;
        var request = new AiGovernanceReasonRequest { Reason = VoidReason };
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrVoided, async () =>
        {
            await _service.VoidOverrideAsync(id, overrideId, request);
            if (_detailLoadGate.IsCurrent(generation)) await LoadModelAsync(id);
        });
    }

    private static AiModelDto NewEditor() => new()
    {
        Kind = AiModelKind.Other,
        Source = AiModelSource.InHouse,
        Status = AiModelStatus.Proposed,
        MaxEvaluationAgeDays = 90
    };

    private void ClearDetail()
    {
        SelectedModel = null;
        Editor = NewEditor();
        DataDrafts.Clear();
        MetricRows.Clear();
        Readings.Clear();
        Overrides.Clear();
    }

    private static string EntityName(DAL.Entities.Entity entity) =>
        entity.EntitiesProperties.FirstOrDefault(property => property.Type == "name")?.Value ?? $"#{entity.Id}";

    private static IReadOnlyList<Track9RegisterOption<T>> EnumOptions<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new Track9RegisterOption<T>(value,
            Localizer[$"Track9{typeof(T).Name}{value}"])).ToList();

    private AiDataDraftRow DataRow(AiModelDataLinkRequest request) => new(request,
        DataOptions.FirstOrDefault(option => option.Value == request.EntityId)?.Name ?? $"#{request.EntityId}");

    private async Task LoadLookupOptionsAsync(long generation)
    {
        try
        {
            var entities = await _entitiesService.GetAllAsync(loadProperties: true);
            if (!_listLoadGate.IsCurrent(generation)) return;
            Replace(EntityOptions, entities.OrderBy(EntityName)
                .Select(entity => new Track9RegisterOption<int>(entity.Id, EntityName(entity))));
            Replace(DataOptions, entities.Where(entity => entity.DefinitionName == "organizationData")
                .OrderBy(EntityName).Select(entity => new Track9RegisterOption<int>(entity.Id, EntityName(entity))));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unable to load entity names for the AI model inventory");
        }

        try
        {
            var users = await _usersService.GetAllAsync();
            if (!_listLoadGate.IsCurrent(generation)) return;
            Replace(UserOptions, users.OrderBy(user => user.Name)
                .Select(user => new Track9RegisterOption<int>(user.Id, user.Name)));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unable to load owner names for the AI model inventory");
        }

        if (!_listLoadGate.IsCurrent(generation)) return;
        ThirdPartyOptions.Clear();
        if (!Track9WorkspaceAccess.CanReadRegister(AuthenticationService.AuthenticatedUserInfo, "third_party_manage"))
            return;
        try
        {
            var thirdParties = await _thirdPartiesService.GetThirdPartiesAsync(includeTerminated: true);
            if (!_listLoadGate.IsCurrent(generation)) return;
            Replace(ThirdPartyOptions, thirdParties.OrderBy(party => party.Name)
                .Select(party => new Track9RegisterOption<int>(party.Id, party.Name)));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unable to load vendor names for the AI model inventory");
        }
    }

    private void EnsureThirdPartyOption(int? id, string? name)
    {
        if (id is not int value || ThirdPartyOptions.Any(option => option.Value == value)) return;
        ThirdPartyOptions.Add(new Track9RegisterOption<int>(value, name ?? $"#{value}"));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
}

/// <summary>AI components linked to one risk, including the hidden-model count that derives flag 11.</summary>
public class RiskAiModelsBlockViewModel : ViewModelBase
{
    private readonly IAiGovernanceService _service;
    private readonly Track9RegistersLoadGate _loadGate = new();
    private int? _riskId;

    public string StrTitle { get; } = Localizer["Track9RiskAiModels"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrRemove { get; } = Localizer["Remove"];
    public string StrHidden { get; } = Localizer["Track9HiddenRecords"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrLinked { get; } = Localizer["Track9Linked"];
    public string StrUnlinked { get; } = Localizer["Track9Unlinked"];
    public string StrName { get; } = Localizer["Name"];
    public string StrVersion { get; } = Localizer["Track9Version"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrRiskTier { get; } = Localizer["Track9RiskTier"];
    public string StrEvaluationState { get; } = Localizer["Track9EvaluationState"];
    public string StrNotes { get; } = Localizer["Track9Notes"];

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private RiskAiModelsDto? _models;
    public RiskAiModelsDto? Models
    {
        get => _models;
        private set => this.RaiseAndSetIfChanged(ref _models, value);
    }

    public ObservableCollection<AiModelSummaryDto> AvailableModels { get; } = [];
    public ObservableCollection<RiskAiModelRow> Rows { get; } = [];
    public AiModelSummaryDto? SelectedAvailableModel { get; set; }
    public RiskAiModelRow? SelectedLinkedModel { get; set; }
    public string? LinkNote { get; set; }
    public bool CanEdit => Track9RegistersAccess.CanEditRiskLinks(AuthenticationService.AuthenticatedUserInfo);

    public ReactiveCommand<RxVoid, RxVoid> LinkCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> UnlinkCommand { get; }

    public RiskAiModelsBlockViewModel() : this(GetService<IAiGovernanceService>()) { }
    internal RiskAiModelsBlockViewModel(IAiGovernanceService service)
    {
        _service = service;
        LinkCommand = ReactiveCommand.CreateFromTask(LinkAsync);
        UnlinkCommand = ReactiveCommand.CreateFromTask(UnlinkAsync);
    }

    public async Task LoadRiskAsync(int? riskId)
    {
        var generation = _loadGate.Begin();
        _riskId = riskId;
        Models = null;
        Rows.Clear();
        AvailableModels.Clear();
        ErrorMessage = null;
        if (riskId is null) return;
        try
        {
            var models = await _service.GetRiskModelsAsync(riskId.Value);
            var available = await _service.GetModelsAsync(includeRetired: false);
            if (!_loadGate.IsCurrent(generation)) return;
            Models = models;
            foreach (var model in models.Models) Rows.Add(new RiskAiModelRow(model));
            foreach (var model in available) AvailableModels.Add(model);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unable to load AI models for risk {RiskId}", riskId.Value);
            if (_loadGate.IsCurrent(generation)) ErrorMessage = StrLoadError;
        }
    }

    private async Task LinkAsync()
    {
        if (!CanEdit || _riskId is null || SelectedAvailableModel is null) return;
        var riskId = _riskId.Value;
        var modelId = SelectedAvailableModel.Id;
        var request = new AiModelRiskLinkRequest { Note = LinkNote };
        await RunAsync(StrLinked, async () =>
        {
            await _service.LinkRiskAsync(modelId, riskId, request);
            if (_riskId == riskId) await LoadRiskAsync(riskId);
        });
    }

    private async Task UnlinkAsync()
    {
        if (!CanEdit || _riskId is null || SelectedLinkedModel is null) return;
        var riskId = _riskId.Value;
        var modelId = SelectedLinkedModel.Model.ModelId;
        await RunAsync(StrUnlinked, async () =>
        {
            await _service.UnlinkRiskAsync(modelId, riskId);
            if (_riskId == riskId) await LoadRiskAsync(riskId);
        });
    }
}

public sealed class RiskAiModelRow
{
    public RiskAiModelDto Model { get; }
    public string Name => Model.Name;
    public string Version => Model.Version;
    public string? Note => Model.Note;
    public string StatusText { get; }
    public string RiskTierText { get; }
    public string EvaluationStateText { get; }

    public RiskAiModelRow(RiskAiModelDto model)
    {
        Model = model;
        StatusText = ViewModelBase.Localizer[$"Track9AiModelStatus{model.Status}"];
        RiskTierText = model.RiskTier is { } tier
            ? ViewModelBase.Localizer[$"Track9AiModelRiskTier{tier}"] : string.Empty;
        EvaluationStateText = ViewModelBase.Localizer[$"Track9AiModelEvaluationState{model.EvaluationState}"];
    }
}

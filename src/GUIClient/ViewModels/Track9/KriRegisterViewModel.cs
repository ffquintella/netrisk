using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.DTO;
using Model.Monitoring;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

/// <summary>A localized typed choice; ToString lets a ComboBox render it without exposing an enum token.</summary>
public sealed class Track9Choice<T>(T value, string label) where T : struct, Enum
{
    public T Value { get; } = value;
    public string Label { get; } = label;
    public override string ToString() => Label;
}

/// <summary>A name-based selector item for legacy services whose DTOs otherwise render as numeric IDs.</summary>
public sealed class Track9LookupItem(int? id, string label)
{
    public int? Id { get; } = id;
    public string Label { get; } = label;
    public override string ToString() => Label;
}

public sealed class KriRegisterRow(KriDto item, string stateLabel)
{
    public KriDto Item { get; } = item;
    public int Id => Item.Id;
    public string Name => Item.Name;
    public string StateLabel { get; } = stateLabel;
    public decimal? LatestValue => Item.Status.LatestValue;
}

/// <summary>KRI definitions, immutable reading history and the risks each indicator governs (T306).</summary>
public sealed class KriRegisterViewModel : ViewModelBase
{
    private readonly IMonitoringService _monitoring = GetService<IMonitoringService>();
    private readonly IUsersService _users = GetService<IUsersService>();
    private readonly IEntitiesService _entities = GetService<IEntitiesService>();
    private readonly IRisksService _risks = GetService<IRisksService>();
    private readonly Track9MonitoringLoadGate _listGate = new();
    private readonly Track9MonitoringLoadGate _detailGate = new();
    private bool _loaded;
    private string _loadError = string.Empty;
    private ObservableCollection<KriRegisterRow> _kris = [];
    private KriRegisterRow? _selectedKri;
    private KriDetailDto? _detail;

    public string StrTitle { get; } = Localizer["Track9KriRegisterTitle"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrShowRetired { get; } = Localizer["Track9ShowRetired"];
    public string StrNoItems { get; } = Localizer["Track9NoItems"];
    public string StrThirdLine { get; } = Localizer["Track9ReadOnlyThirdLine"];
    public string StrDefinition { get; } = Localizer["Track9KriDefinition"];
    public string StrName { get; } = Localizer["Name"];
    public string StrDescription { get; } = Localizer["Description"];
    public string StrCategory { get; } = Localizer["Track9Category"];
    public string StrSource { get; } = Localizer["Source"];
    public string StrUnit { get; } = Localizer["Track9Unit"];
    public string StrDirection { get; } = Localizer["Track9Direction"];
    public string StrTolerance { get; } = Localizer["Track9Tolerance"];
    public string StrWarning { get; } = Localizer["Track9WarningThreshold"];
    public string StrRationale { get; } = Localizer["Track9ToleranceRationale"];
    public string StrMaxAge { get; } = Localizer["Track9MaximumReadingAge"];
    public string StrOwnerId { get; } = Localizer["Track9OwnerId"];
    public string StrEntityId { get; } = Localizer["Track9EntityId"];
    public string StrCreate { get; } = Localizer["Create"];
    public string StrUpdate { get; } = Localizer["Update"];
    public string StrRetire { get; } = Localizer["Track9Retire"];
    public string StrReadings { get; } = Localizer["Track9Readings"];
    public string StrRecordReading { get; } = Localizer["Track9RecordReading"];
    public string StrValue { get; } = Localizer["Value"];
    public string StrObservedAt { get; } = Localizer["Track9ObservedAt"];
    public string StrNotes { get; } = Localizer["Notes"];
    public string StrVoid { get; } = Localizer["Track9VoidReading"];
    public string StrVoidReason { get; } = Localizer["Track9VoidReason"];
    public string StrLinkedRisks { get; } = Localizer["Track9LinkedRisks"];
    public string StrRiskId { get; } = Localizer["Track9RiskId"];
    public string StrLink { get; } = Localizer["Track9LinkRisk"];
    public string StrUnlink { get; } = Localizer["Track9UnlinkRisk"];
    public string StrStatus { get; } = Localizer["Status"];

    public ObservableCollection<Track9Choice<KriCategory>> Categories { get; } =
    [
        new(KriCategory.Unavailability, Localizer["Track9KriCategoryUnavailability"]),
        new(KriCategory.DataLoss, Localizer["Track9KriCategoryDataLoss"]),
        new(KriCategory.DataSubjects, Localizer["Track9KriCategoryDataSubjects"]),
        new(KriCategory.Other, Localizer["Track9KriCategoryOther"])
    ];

    public ObservableCollection<Track9Choice<KriDirection>> Directions { get; } =
    [
        new(KriDirection.HigherIsWorse, Localizer["Track9KriDirectionHigher"]),
        new(KriDirection.LowerIsWorse, Localizer["Track9KriDirectionLower"])
    ];

    public ObservableCollection<Track9LookupItem> Owners { get; } = [];
    public ObservableCollection<Track9LookupItem> EntityChoices { get; } = [];
    public ObservableCollection<Track9LookupItem> RiskChoices { get; } = [];

    public ObservableCollection<KriRegisterRow> Kris
    {
        get => _kris;
        private set
        {
            this.RaiseAndSetIfChanged(ref _kris, value);
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public KriRegisterRow? SelectedKri
    {
        get => _selectedKri;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedKri, value);
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(CanRetire));
            this.RaisePropertyChanged(nameof(CanWriteDetail));
            if (value is null)
            {
                _detailGate.DiscardOutstanding();
                Detail = null;
                ResetEditor();
            }
            else
            {
                Detail = null;
                SelectedReading = null;
                SelectedLinkedRisk = null;
                ResetEditor();
                this.RaisePropertyChanged(nameof(SelectedReading));
                this.RaisePropertyChanged(nameof(SelectedLinkedRisk));
                _ = LoadSelectedAsync(value.Id);
            }
        }
    }

    public KriDetailDto? Detail
    {
        get => _detail;
        private set
        {
            this.RaiseAndSetIfChanged(ref _detail, value);
            this.RaisePropertyChanged(nameof(HasDetail));
            this.RaisePropertyChanged(nameof(CanWriteDetail));
        }
    }

    public string LoadError
    {
        get => _loadError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _loadError, value);
            this.RaisePropertyChanged(nameof(HasLoadError));
        }
    }

    public bool IncludeRetired { get; set; }
    public bool IsEmpty => _loaded && Kris.Count == 0 && !HasLoadError;
    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public bool HasSelection => SelectedKri is not null;
    public bool HasDetail => Detail is not null;
    public bool IsThirdLine => Track9MonitoringPermissions.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public bool CanDefine => Track9MonitoringPermissions.CanDefineKriOrCommittee(AuthenticationService.AuthenticatedUserInfo);
    public bool CanRetire => CanDefine && SelectedKri?.Item.RetiredAt is null && SelectedKri is not null;
    public bool CanWriteDetail => Track9MonitoringPermissions.CanSubmitRiskData(AuthenticationService.AuthenticatedUserInfo)
                                  && Detail is { RetiredAt: null };

    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Track9Choice<KriCategory>? SelectedCategory { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public Track9Choice<KriDirection>? SelectedDirection { get; set; }
    public decimal? ToleranceThreshold { get; set; }
    public decimal? WarningThreshold { get; set; }
    public string ToleranceRationale { get; set; } = string.Empty;
    public decimal? MaxReadingAgeDays { get; set; } = MonitoringLimits.DefaultMaxReadingAgeDays;
    public Track9LookupItem? SelectedOwner { get; set; }
    public Track9LookupItem? SelectedEntity { get; set; }
    public decimal? ReadingValue { get; set; }
    public DateTimeOffset? ReadingObservedAt { get; set; } = DateTimeOffset.Now;
    public string ReadingNote { get; set; } = string.Empty;
    public KriReadingDto? SelectedReading { get; set; }
    public string VoidReason { get; set; } = string.Empty;
    public Track9LookupItem? SelectedRiskToLink { get; set; }
    public KriLinkedRiskDto? SelectedLinkedRisk { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRetireClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRecordReadingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtVoidReadingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtLinkRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtUnlinkRiskClicked { get; }

    public KriRegisterViewModel()
    {
        SelectedCategory = Categories[0];
        SelectedDirection = Directions[0];
        BtReloadClicked = ReactiveCommand.CreateFromTask(ReloadAsync);
        BtNewClicked = ReactiveCommand.Create(ClearEditor);
        BtSaveClicked = ReactiveCommand.CreateFromTask(SaveAsync);
        BtRetireClicked = ReactiveCommand.CreateFromTask(RetireAsync);
        BtRecordReadingClicked = ReactiveCommand.CreateFromTask(RecordReadingAsync);
        BtVoidReadingClicked = ReactiveCommand.CreateFromTask(VoidReadingAsync);
        BtLinkRiskClicked = ReactiveCommand.CreateFromTask(LinkRiskAsync);
        BtUnlinkRiskClicked = ReactiveCommand.CreateFromTask(UnlinkRiskAsync);
    }

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : ReloadAsync();
    public Task ReloadAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var token = _listGate.Begin();
        try
        {
            await WithBusyAsync(async () =>
            {
                var selectedId = SelectedKri?.Id;
                var krisTask = _monitoring.GetKrisAsync(IncludeRetired);
                var usersTask = _users.GetAllAsync();
                var entitiesTask = _entities.GetAllAsync();
                var risksTask = _risks.GetAllRisksAsync();
                await Task.WhenAll(krisTask, usersTask, entitiesTask, risksTask);
                if (!_listGate.IsCurrent(token)) return;
                LoadError = string.Empty;
                Kris = new ObservableCollection<KriRegisterRow>((await krisTask)
                    .Select(x => new KriRegisterRow(x, Localizer[KriStateKey(x.Status.State)])));
                Replace(Owners, [new Track9LookupItem(null, Localizer["Track9Unassigned"]),
                    .. (await usersTask).Select(x => new Track9LookupItem(x.Id, x.Name))]);
                Replace(EntityChoices, [new Track9LookupItem(null, Localizer["Track9OrganizationWide"]),
                    .. (await entitiesTask).Select(x => new Track9LookupItem(x.Id, x.DisplayName))]);
                Replace(RiskChoices, (await risksTask)
                    .Select(x => new Track9LookupItem(x.Id, x.Subject)).ToList());
                _loaded = true;
                SelectedKri = selectedId is null ? null : Kris.FirstOrDefault(x => x.Id == selectedId);
                this.RaisePropertyChanged(nameof(IsEmpty));
            });
        }
        catch (Exception ex)
        {
            if (!_listGate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading the Track 9 KRI register");
            LoadError = ExplainError(ex);
        }
    }

    private async Task LoadSelectedAsync(int? id)
    {
        var token = _detailGate.Begin();
        if (id is null)
        {
            Detail = null;
            return;
        }

        try
        {
            var detail = await _monitoring.GetKriAsync(id.Value);
            if (!_detailGate.IsCurrent(token)) return;
            Detail = detail;
            PopulateEditor(detail);
        }
        catch (Exception ex)
        {
            if (!_detailGate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading KRI {KriId}", id);
            LoadError = ExplainError(ex);
        }
    }

    private void PopulateEditor(KriDto kri)
    {
        Name = kri.Name;
        Description = kri.Description ?? string.Empty;
        SelectedCategory = Categories.First(x => x.Value == kri.Category);
        Source = kri.Source;
        Unit = kri.Unit;
        SelectedDirection = Directions.First(x => x.Value == kri.Direction);
        ToleranceThreshold = kri.ToleranceThreshold;
        WarningThreshold = kri.WarningThreshold;
        ToleranceRationale = kri.ToleranceRationale;
        MaxReadingAgeDays = kri.MaxReadingAgeDays;
        SelectedOwner = Owners.FirstOrDefault(x => x.Id == kri.OwnerId);
        SelectedEntity = EntityChoices.FirstOrDefault(x => x.Id == kri.EntityId);
        foreach (var property in new[] { nameof(Name), nameof(Description), nameof(SelectedCategory), nameof(Source),
                     nameof(Unit), nameof(SelectedDirection), nameof(ToleranceThreshold), nameof(WarningThreshold),
                     nameof(ToleranceRationale), nameof(MaxReadingAgeDays), nameof(SelectedOwner), nameof(SelectedEntity) })
            this.RaisePropertyChanged(property);
    }

    private void ClearEditor()
    {
        if (!CanDefine) return;
        _detailGate.DiscardOutstanding();
        SelectedKri = null;
    }

    private void ResetEditor()
    {
        Name = Description = Source = Unit = ToleranceRationale = string.Empty;
        SelectedCategory = Categories[0];
        SelectedDirection = Directions[0];
        ToleranceThreshold = WarningThreshold = null;
        SelectedOwner = Owners.FirstOrDefault(x => x.Id is null);
        SelectedEntity = EntityChoices.FirstOrDefault(x => x.Id is null);
        MaxReadingAgeDays = MonitoringLimits.DefaultMaxReadingAgeDays;
        foreach (var property in new[] { nameof(Name), nameof(Description), nameof(SelectedCategory), nameof(Source),
                     nameof(Unit), nameof(SelectedDirection), nameof(ToleranceThreshold), nameof(WarningThreshold),
                     nameof(ToleranceRationale), nameof(MaxReadingAgeDays), nameof(SelectedOwner), nameof(SelectedEntity) })
            this.RaisePropertyChanged(property);
    }

    private KriRequest Request() => new()
    {
        Name = Name,
        Description = Description,
        Category = SelectedCategory?.Value,
        Source = Source,
        Unit = Unit,
        Direction = SelectedDirection?.Value,
        ToleranceThreshold = ToleranceThreshold,
        WarningThreshold = WarningThreshold,
        ToleranceRationale = ToleranceRationale,
        MaxReadingAgeDays = MaxReadingAgeDays is { } age ? decimal.ToInt32(age) : null,
        OwnerId = SelectedOwner?.Id,
        EntityId = SelectedEntity?.Id
    };

    private Task SaveAsync() => !CanDefine
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9KriSaved"], async () =>
    {
        var saved = SelectedKri is null
            ? await _monitoring.CreateKriAsync(Request())
            : await _monitoring.UpdateKriAsync(SelectedKri.Id, Request());
        await ReloadAsync();
        SelectedKri = Kris.FirstOrDefault(x => x.Id == saved.Id);
    });

    private Task RetireAsync() => !CanRetire || SelectedKri is null ? Task.CompletedTask : RunAsync(Localizer["Track9KriRetired"], async () =>
    {
        await _monitoring.RetireKriAsync(SelectedKri.Id);
        await ReloadAsync();
    });

    private Task RecordReadingAsync() => !CanWriteDetail || Detail is null ? Task.CompletedTask : RunAsync(Localizer["Track9ReadingRecorded"], async () =>
    {
        Detail = await _monitoring.RecordReadingAsync(Detail.Id, new KriReadingRequest
        {
            Value = ReadingValue,
            ObservedAt = ReadingObservedAt?.UtcDateTime,
            Note = ReadingNote
        });
        await ReloadAsync();
    });

    private Task VoidReadingAsync() => !CanWriteDetail || Detail is null || SelectedReading is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9ReadingVoided"], async () =>
        {
            Detail = await _monitoring.VoidReadingAsync(Detail.Id, SelectedReading.Id,
                new KriReadingVoidRequest { Reason = VoidReason });
            await ReloadAsync();
        });

    private Task LinkRiskAsync() => !CanWriteDetail || Detail is null || SelectedRiskToLink?.Id is not { } riskId
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9RiskLinked"], async () =>
        {
            Detail = await _monitoring.LinkRiskAsync(Detail.Id, riskId);
            await ReloadAsync();
        });

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static string KriStateKey(KriState state) => state switch
    {
        KriState.NoReading => "Track9NoReading",
        KriState.Stale => "Track9Stale",
        KriState.WithinTolerance => "Track9WithinTolerance",
        KriState.Warning => "Warning",
        KriState.Breached => "Track9Breached",
        KriState.Retired => "Retired",
        _ => "Track9NoReading"
    };

    private Task UnlinkRiskAsync() => !CanWriteDetail || Detail is null || SelectedLinkedRisk is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9RiskUnlinked"], async () =>
        {
            await _monitoring.UnlinkRiskAsync(Detail.Id, SelectedLinkedRisk.RiskId);
            Detail = await _monitoring.GetKriAsync(Detail.Id);
            await ReloadAsync();
        });
}

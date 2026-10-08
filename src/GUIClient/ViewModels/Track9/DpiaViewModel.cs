using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using Model.DataCatalogue;
using GUIClient.Tools.Track9;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

/// <summary>Stage 9.11 RIPD/DPIA workflow: draft, links, approval and reasoned retirement.</summary>
public class DpiaViewModel : ViewModelBase
{
    private readonly IDataCatalogueService _service;
    private readonly IEntitiesService _entitiesService;
    private readonly Track9RegistersLoadGate _detailLoadGate = new();
    private readonly Track9RegistersLoadGate _listLoadGate = new();
    private bool _loaded;

    public string StrTitle { get; } = Localizer["Track9DpiaRegister"];
    public string StrRefresh { get; } = Localizer["Refresh"];
    public string StrNew { get; } = Localizer["New"];
    public string StrOpen { get; } = Localizer["Open"];
    public string StrApprove { get; } = Localizer["Approve"];
    public string StrRetire { get; } = Localizer["Track9Retire"];
    public string StrLinks { get; } = Localizer["Track9Links"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrRemove { get; } = Localizer["Remove"];
    public string StrSaved { get; } = Localizer["Track9Saved"];
    public string StrApproved { get; } = Localizer["Track9Approved"];
    public string StrRetired { get; } = Localizer["Track9Retired"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrNoData { get; } = Localizer["Track9NoData"];
    public string StrTitleColumn { get; } = Localizer["Track9Title"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrResidualRisk { get; } = Localizer["Track9ResidualRisk"];
    public string StrReviewDue { get; } = Localizer["Track9ReviewDue"];
    public string StrReviewOverdue { get; } = Localizer["Track9ReviewOverdue"];
    public string StrPerformedAt { get; } = Localizer["Track9PerformedAt"];
    public string StrDocumentReference { get; } = Localizer["Track9DocumentReference"];
    public string StrSummary { get; } = Localizer["Track9Summary"];
    public string StrReason { get; } = Localizer["Track9Reason"];
    public string StrEntity { get; } = Localizer["Track9Entity"];
    public string StrKind { get; } = Localizer["Track9Kind"];

    public IReadOnlyList<Track9RegisterOption<DpiaStatus>> StatusOptions { get; } = EnumOptions<DpiaStatus>();
    public IReadOnlyList<Track9RegisterOption<DpiaResidualRisk>> ResidualRiskOptions { get; } = EnumOptions<DpiaResidualRisk>();

    public ObservableCollection<DpiaSummaryRow> Items { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> EntityOptions { get; } = [];
    public ObservableCollection<DpiaLinkRow> LinkRows { get; } = [];
    public bool IsEmpty => _loaded && Items.Count == 0;

    private DpiaStatus? _statusFilter;
    public DpiaStatus? StatusFilter
    {
        get => _statusFilter;
        set => this.RaiseAndSetIfChanged(ref _statusFilter, value);
    }

    private DpiaSummaryRow? _selectedItem;
    public DpiaSummaryRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!ReferenceEquals(_selectedItem, value)) _detailLoadGate.DiscardOutstanding();
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
        }
    }

    private DpiaDto? _selectedDpia;
    public DpiaDto? SelectedDpia
    {
        get => _selectedDpia;
        private set
        {
            this.RaiseAndSetIfChanged(ref _selectedDpia, value);
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(CanEdit));
            this.RaisePropertyChanged(nameof(CanApprove));
            this.RaisePropertyChanged(nameof(CanRetire));
            this.RaisePropertyChanged(nameof(CanManageEdit));
            this.RaisePropertyChanged(nameof(CanManageApprove));
            this.RaisePropertyChanged(nameof(CanManageRetire));
        }
    }
    public bool HasSelection => SelectedDpia is not null;
    public bool CanEdit => SelectedDpia is null || SelectedDpia.Status == DpiaStatus.Draft;
    public bool CanApprove => SelectedDpia?.Status == DpiaStatus.Draft;
    public bool CanRetire => SelectedDpia?.Status == DpiaStatus.Approved;
    public bool CanManage => Track9RegistersAccess.CanManageDataCatalogue(AuthenticationService.AuthenticatedUserInfo);
    public bool CanManageEdit => CanManage && CanEdit;
    public bool CanManageApprove => CanManage && CanApprove;
    public bool CanManageRetire => CanManage && CanRetire;
    public bool IsThirdLine => Track9RegistersAccess.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public string WriteDisabledReason => Localizer[IsThirdLine ? "Track9ThirdLineReadOnly" : "Track9ReadOnly"];

    private DpiaRequest _editor = new();
    public DpiaRequest Editor
    {
        get => _editor;
        private set
        {
            this.RaiseAndSetIfChanged(ref _editor, value);
            this.RaisePropertyChanged(nameof(SelectedResidualRisk));
        }
    }
    public DpiaResidualRisk? SelectedResidualRisk
    {
        get => Editor.ResidualRisk;
        set { if (value is null) return; Editor.ResidualRisk = value; this.RaisePropertyChanged(); }
    }

    private DpiaLinkRow? _selectedLink;
    public DpiaLinkRow? SelectedLink
    {
        get => _selectedLink;
        set => this.RaiseAndSetIfChanged(ref _selectedLink, value);
    }

    public int? LinkEntityId { get; set; }
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
    public ReactiveCommand<RxVoid, RxVoid> LinkCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> UnlinkCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ApproveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RetireCommand { get; }

    public DpiaViewModel() : this(GetService<IDataCatalogueService>(), GetService<IEntitiesService>()) { }
    internal DpiaViewModel(IDataCatalogueService service) : this(service, GetService<IEntitiesService>()) { }
    internal DpiaViewModel(IDataCatalogueService service, IEntitiesService entitiesService)
    {
        _service = service;
        _entitiesService = entitiesService;
        ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
        NewCommand = ReactiveCommand.Create(NewRecord);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        LinkCommand = ReactiveCommand.CreateFromTask(LinkAsync);
        UnlinkCommand = ReactiveCommand.CreateFromTask(UnlinkAsync);
        ApproveCommand = ReactiveCommand.CreateFromTask(ApproveAsync);
        RetireCommand = ReactiveCommand.CreateFromTask(RetireAsync);
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
                var items = await _service.GetDpiasAsync(StatusFilter);
                var entities = await _entitiesService.GetAllAsync(loadProperties: true);
                if (!_listLoadGate.IsCurrent(generation)) return;
                Items.Clear();
                foreach (var item in items) Items.Add(new DpiaSummaryRow(item));
                EntityOptions.Clear();
                foreach (var entity in entities.OrderBy(EntityName))
                    EntityOptions.Add(new Track9RegisterOption<int>(entity.Id, EntityName(entity)));
                ErrorMessage = null;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load DPIAs");
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
        SelectedDpia = null;
        Editor = new DpiaRequest();
        LinkRows.Clear();
    }

    private async Task OpenAsync()
    {
        if (SelectedItem is null) return;
        await LoadDpiaAsync(SelectedItem.Summary.Id);
    }

    private async Task LoadDpiaAsync(int id)
    {
        var generation = _detailLoadGate.Begin();
        try
        {
            var dpia = await _service.GetDpiaAsync(id);
            if (!_detailLoadGate.IsCurrent(generation)) return;
            SelectedDpia = dpia;
            Editor = new DpiaRequest
            {
                Title = dpia.Title,
                Summary = dpia.Summary,
                DocumentReference = dpia.DocumentReference,
                ResidualRisk = dpia.ResidualRisk,
                PerformedAt = dpia.PerformedAt,
                NextReviewDueAt = dpia.NextReviewDueAt
            };
            LinkRows.Clear();
            foreach (var link in dpia.Links) LinkRows.Add(new DpiaLinkRow(link));
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unable to load DPIA {DpiaId}", id);
            if (!_detailLoadGate.IsCurrent(generation)) return;
            SelectedDpia = null;
            Editor = new DpiaRequest();
            LinkRows.Clear();
            ErrorMessage = ExplainError(ex);
        }
    }

    private async Task SaveAsync()
    {
        if (!CanManageEdit) return;
        var id = SelectedDpia?.Id;
        var request = new DpiaRequest
        {
            Title = Editor.Title,
            Summary = Editor.Summary,
            DocumentReference = Editor.DocumentReference,
            ResidualRisk = Editor.ResidualRisk,
            PerformedAt = Editor.PerformedAt,
            NextReviewDueAt = Editor.NextReviewDueAt
        };
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            var saved = id is null
                ? await _service.CreateDpiaAsync(request)
                : await _service.UpdateDpiaAsync(id.Value, request);
            await ReloadAsync();
            if (_detailLoadGate.IsCurrent(generation)) await LoadDpiaAsync(saved.Id);
        });
    }

    private async Task LinkAsync()
    {
        if (!CanManageEdit || SelectedDpia is null || LinkEntityId is not int entityId) return;
        var id = SelectedDpia.Id;
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            await _service.LinkDpiaAsync(id, entityId);
            if (_detailLoadGate.IsCurrent(generation)) await LoadDpiaAsync(id);
        });
    }

    private async Task UnlinkAsync()
    {
        if (!CanManageEdit || SelectedDpia is null || SelectedLink is null) return;
        var id = SelectedDpia.Id;
        var entityId = SelectedLink.Link.EntityId;
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            await _service.UnlinkDpiaAsync(id, entityId);
            if (_detailLoadGate.IsCurrent(generation)) await LoadDpiaAsync(id);
        });
    }

    private async Task ApproveAsync()
    {
        if (!CanManageApprove || SelectedDpia is null) return;
        var id = SelectedDpia.Id;
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrApproved, async () =>
        {
            await _service.ApproveDpiaAsync(id);
            await ReloadAsync();
            if (_detailLoadGate.IsCurrent(generation)) await LoadDpiaAsync(id);
        });
    }

    private async Task RetireAsync()
    {
        if (!CanManageRetire || SelectedDpia is null) return;
        var id = SelectedDpia.Id;
        var request = new DpiaRetireRequest { Reason = RetireReason };
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrRetired, async () =>
        {
            await _service.RetireDpiaAsync(id, request);
            await ReloadAsync();
            if (_detailLoadGate.IsCurrent(generation)) await LoadDpiaAsync(id);
        });
    }

    private static string EntityName(DAL.Entities.Entity entity) =>
        entity.EntitiesProperties.FirstOrDefault(property => property.Type == "name")?.Value ?? $"#{entity.Id}";

    private static IReadOnlyList<Track9RegisterOption<T>> EnumOptions<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new Track9RegisterOption<T>(value,
            Localizer[$"Track9{typeof(T).Name}{value}"])).ToList();
}

public sealed class DpiaSummaryRow
{
    public DpiaSummaryDto Summary { get; }
    public string Title => Summary.Title;
    public bool ReviewOverdue => Summary.ReviewOverdue;
    public string StatusText { get; }
    public string ResidualRiskText { get; }

    public DpiaSummaryRow(DpiaSummaryDto summary)
    {
        Summary = summary;
        StatusText = ViewModelBase.Localizer[$"Track9DpiaStatus{summary.Status}"];
        ResidualRiskText = summary.ResidualRisk is { } risk
            ? ViewModelBase.Localizer[$"Track9DpiaResidualRisk{risk}"] : string.Empty;
    }
}

public sealed class DpiaLinkRow
{
    public DpiaLinkDto Link { get; }
    public string? EntityName => Link.EntityName;
    public string KindText { get; }

    public DpiaLinkRow(DpiaLinkDto link)
    {
        Link = link;
        KindText = ViewModelBase.Localizer[$"Track9DpiaLinkKind{link.Kind}"];
    }
}

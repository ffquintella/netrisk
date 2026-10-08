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

/// <summary>Stage 9.11 law, regulation, contract and internal-norm register.</summary>
public class LegalRequirementsViewModel : ViewModelBase
{
    private readonly IDataCatalogueService _service;
    private readonly IThirdPartiesService _thirdPartiesService;
    private readonly Track9RegistersLoadGate _selectionGate = new();
    private readonly Track9RegistersLoadGate _listLoadGate = new();
    private bool _loaded;

    public string StrTitle { get; } = Localizer["Track9LegalRequirements"];
    public string StrRefresh { get; } = Localizer["Refresh"];
    public string StrNew { get; } = Localizer["New"];
    public string StrDelete { get; } = Localizer["Delete"];
    public string StrSaved { get; } = Localizer["Track9Saved"];
    public string StrDeleted { get; } = Localizer["Track9Deleted"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrNoData { get; } = Localizer["Track9NoData"];
    public string StrCode { get; } = Localizer["Track9Code"];
    public string StrTitleColumn { get; } = Localizer["Track9Title"];
    public string StrKind { get; } = Localizer["Track9Kind"];
    public string StrDescription { get; } = Localizer["Track9Description"];
    public string StrReference { get; } = Localizer["Track9Reference"];
    public string StrVendor { get; } = Localizer["Track9Vendor"];
    public string StrRiskLinks { get; } = Localizer["Track9RiskLinks"];
    public string StrPurposes { get; } = Localizer["Track9Purposes"];
    public string StrHiddenSupplierReadOnly { get; } = Localizer["Track9HiddenSupplierReadOnly"];

    public IReadOnlyList<Track9RegisterOption<LegalRequirementKind>> KindOptions { get; } = EnumOptions<LegalRequirementKind>();
    public ObservableCollection<LegalRequirementRow> Items { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> ThirdPartyOptions { get; } = [];
    public bool CanManage => Track9RegistersAccess.CanManageDataCatalogue(AuthenticationService.AuthenticatedUserInfo);
    public bool IsThirdLine => Track9RegistersAccess.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public string WriteDisabledReason => Localizer[IsThirdLine ? "Track9ThirdLineReadOnly" : "Track9ReadOnly"];

    private LegalRequirementRow? _selectedItem;
    public LegalRequirementRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!ReferenceEquals(_selectedItem, value)) _selectionGate.DiscardOutstanding();
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
            if (value is not null) CopyToEditor(value.Requirement);
            this.RaisePropertyChanged(nameof(SelectedSupplierHidden));
            this.RaisePropertyChanged(nameof(CanEditSelected));
        }
    }
    public bool SelectedSupplierHidden => SelectedItem?.Requirement.ThirdPartyHidden == true;
    public bool CanEditSelected => Track9RegistersAccess.CanEditLegalRequirement(
        AuthenticationService.AuthenticatedUserInfo, SelectedSupplierHidden);

    private LegalRequirementRequest _editor = new();
    public LegalRequirementRequest Editor
    {
        get => _editor;
        private set
        {
            this.RaiseAndSetIfChanged(ref _editor, value);
            this.RaisePropertyChanged(nameof(SelectedKind));
            this.RaisePropertyChanged(nameof(SelectedThirdPartyId));
        }
    }
    public LegalRequirementKind? SelectedKind
    {
        get => Editor.Kind;
        set { if (value is null) return; Editor.Kind = value; this.RaisePropertyChanged(); }
    }
    public int? SelectedThirdPartyId
    {
        get => Editor.ThirdPartyId;
        set { if (value is null) return; Editor.ThirdPartyId = value; this.RaisePropertyChanged(); }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }
    public bool IsEmpty => _loaded && Items.Count == 0;

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public LegalRequirementsViewModel() : this(GetService<IDataCatalogueService>(), GetService<IThirdPartiesService>()) { }
    internal LegalRequirementsViewModel(IDataCatalogueService service) : this(service, GetService<IThirdPartiesService>()) { }
    internal LegalRequirementsViewModel(IDataCatalogueService service, IThirdPartiesService thirdPartiesService)
    {
        _service = service;
        _thirdPartiesService = thirdPartiesService;
        ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
        NewCommand = ReactiveCommand.Create(() =>
        {
            if (!CanManage) return;
            _selectionGate.DiscardOutstanding();
            SelectedItem = null;
            Editor = new LegalRequirementRequest();
        });
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
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
                var items = await _service.GetRequirementsAsync();
                if (!_listLoadGate.IsCurrent(generation)) return;
                Items.Clear();
                foreach (var item in items) Items.Add(new LegalRequirementRow(item));
                ThirdPartyOptions.Clear();
                ErrorMessage = null;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
                await LoadThirdPartyOptionsAsync(generation);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load legal requirements");
                if (!_listLoadGate.IsCurrent(generation)) return;
                ErrorMessage = StrLoadError;
            }
        });
    }

    private void CopyToEditor(LegalRequirementDto item)
    {
        if (item.ThirdPartyId is int id && ThirdPartyOptions.All(option => option.Value != id))
            ThirdPartyOptions.Add(new Track9RegisterOption<int>(id, item.ThirdPartyName ?? $"#{id}"));
        Editor = new LegalRequirementRequest
        {
            Code = item.Code,
            Title = item.Title,
            Kind = item.Kind,
            Description = item.Description,
            Reference = item.Reference,
            ThirdPartyId = item.ThirdPartyId
        };
    }

    private async Task LoadThirdPartyOptionsAsync(long generation)
    {
        if (!_listLoadGate.IsCurrent(generation)) return;
        if (!Track9WorkspaceAccess.CanReadRegister(AuthenticationService.AuthenticatedUserInfo, "third_party_manage"))
            return;
        try
        {
            var thirdParties = await _thirdPartiesService.GetThirdPartiesAsync(includeTerminated: true);
            if (!_listLoadGate.IsCurrent(generation)) return;
            ThirdPartyOptions.Clear();
            foreach (var party in thirdParties.OrderBy(party => party.Name))
                ThirdPartyOptions.Add(new Track9RegisterOption<int>(party.Id, party.Name));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unable to load vendors for legal requirements");
        }
    }

    private async Task SaveAsync()
    {
        if (!CanEditSelected) return;
        var id = SelectedItem?.Requirement.Id;
        var request = new LegalRequirementRequest
        {
            Code = Editor.Code,
            Title = Editor.Title,
            Kind = Editor.Kind,
            Description = Editor.Description,
            Reference = Editor.Reference,
            ThirdPartyId = Editor.ThirdPartyId
        };
        var generation = _selectionGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            var saved = id is null
                ? await _service.CreateRequirementAsync(request)
                : await _service.UpdateRequirementAsync(id.Value, request);
            await ReloadAsync();
            if (_selectionGate.IsCurrent(generation)) SelectedItem = Find(saved.Id);
        });
    }

    private async Task DeleteAsync()
    {
        if (!CanManage || SelectedItem is null) return;
        var id = SelectedItem.Requirement.Id;
        var generation = _selectionGate.Begin();
        await RunAsync(StrDeleted, async () =>
        {
            await _service.DeleteRequirementAsync(id);
            if (_selectionGate.IsCurrent(generation))
            {
                SelectedItem = null;
                Editor = new LegalRequirementRequest();
            }
            await ReloadAsync();
        });
    }

    private LegalRequirementRow? Find(int id)
    {
        foreach (var item in Items)
            if (item.Requirement.Id == id) return item;
        return null;
    }

    private static IReadOnlyList<Track9RegisterOption<T>> EnumOptions<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new Track9RegisterOption<T>(value,
            Localizer[$"Track9{typeof(T).Name}{value}"])).ToList();
}

public sealed class LegalRequirementRow
{
    public LegalRequirementDto Requirement { get; }
    public string Code => Requirement.Code;
    public string Title => Requirement.Title;
    public int RiskLinkCount => Requirement.RiskLinkCount;
    public int PurposeCount => Requirement.PurposeCount;
    public string KindText { get; }

    public LegalRequirementRow(LegalRequirementDto requirement)
    {
        Requirement = requirement;
        KindText = ViewModelBase.Localizer[$"Track9LegalRequirementKind{requirement.Kind}"];
    }
}

/// <summary>Legal/compliance evidence block mounted on risk detail.</summary>
public class RiskRequirementsBlockViewModel : ViewModelBase
{
    private readonly IDataCatalogueService _service;
    private readonly Track9RegistersLoadGate _loadGate = new();
    private int? _riskId;

    public string StrTitle { get; } = Localizer["Track9RiskRequirements"];
    public string StrRequirements { get; } = Localizer["Track9LegalRequirements"];
    public string StrCatalogueEvidence { get; } = Localizer["Track9CatalogueEvidence"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrRemove { get; } = Localizer["Remove"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrLinked { get; } = Localizer["Track9Linked"];
    public string StrUnlinked { get; } = Localizer["Track9Unlinked"];
    public string StrCode { get; } = Localizer["Track9Code"];
    public string StrTitleColumn { get; } = Localizer["Track9Title"];
    public string StrKind { get; } = Localizer["Track9Kind"];
    public string StrNotes { get; } = Localizer["Track9Notes"];
    public string StrName { get; } = Localizer["Name"];
    public string StrCatalogued { get; } = Localizer["Track9Catalogued"];
    public string StrPersonalData { get; } = Localizer["Track9PersonalData"];
    public string StrFindings { get; } = Localizer["Findings"];

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private RiskComplianceDto? _compliance;
    public RiskComplianceDto? Compliance
    {
        get => _compliance;
        private set => this.RaiseAndSetIfChanged(ref _compliance, value);
    }

    public ObservableCollection<LegalRequirementDto> AvailableRequirements { get; } = [];
    public ObservableCollection<RiskRequirementRow> RequirementRows { get; } = [];
    public ObservableCollection<RiskDataRecordRow> DataRows { get; } = [];
    public LegalRequirementDto? SelectedAvailableRequirement { get; set; }
    public RiskRequirementRow? SelectedLinkedRequirement { get; set; }
    public string? LinkNote { get; set; }
    public bool CanEdit => Track9RegistersAccess.CanEditRiskLinks(AuthenticationService.AuthenticatedUserInfo);

    public ReactiveCommand<RxVoid, RxVoid> LinkCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> UnlinkCommand { get; }

    public RiskRequirementsBlockViewModel() : this(GetService<IDataCatalogueService>()) { }
    internal RiskRequirementsBlockViewModel(IDataCatalogueService service)
    {
        _service = service;
        LinkCommand = ReactiveCommand.CreateFromTask(LinkAsync);
        UnlinkCommand = ReactiveCommand.CreateFromTask(UnlinkAsync);
    }

    public async Task LoadRiskAsync(int? riskId)
    {
        var generation = _loadGate.Begin();
        _riskId = riskId;
        Compliance = null;
        AvailableRequirements.Clear();
        RequirementRows.Clear();
        DataRows.Clear();
        ErrorMessage = null;
        if (riskId is null) return;
        try
        {
            var compliance = await _service.GetRiskComplianceAsync(riskId.Value);
            var requirements = await _service.GetRequirementsAsync();
            if (!_loadGate.IsCurrent(generation)) return;
            Compliance = compliance;
            foreach (var requirement in compliance.Requirements) RequirementRows.Add(new RiskRequirementRow(requirement));
            foreach (var record in compliance.DataRecords) DataRows.Add(new RiskDataRecordRow(record));
            foreach (var requirement in requirements) AvailableRequirements.Add(requirement);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unable to load compliance for risk {RiskId}", riskId.Value);
            if (_loadGate.IsCurrent(generation)) ErrorMessage = StrLoadError;
        }
    }

    private async Task LinkAsync()
    {
        if (!CanEdit || _riskId is null || SelectedAvailableRequirement is null) return;
        var riskId = _riskId.Value;
        var requirementId = SelectedAvailableRequirement.Id;
        var request = new RiskLegalRequirementRequest { Note = LinkNote };
        await RunAsync(StrLinked, async () =>
        {
            await _service.LinkRiskRequirementAsync(riskId, requirementId, request);
            if (_riskId == riskId) await LoadRiskAsync(riskId);
        });
    }

    private async Task UnlinkAsync()
    {
        if (!CanEdit || _riskId is null || SelectedLinkedRequirement is null) return;
        var riskId = _riskId.Value;
        var requirementId = SelectedLinkedRequirement.Requirement.RequirementId;
        await RunAsync(StrUnlinked, async () =>
        {
            await _service.UnlinkRiskRequirementAsync(riskId, requirementId);
            if (_riskId == riskId) await LoadRiskAsync(riskId);
        });
    }
}

public sealed class RiskRequirementRow
{
    public RiskRequirementDto Requirement { get; }
    public string Code => Requirement.Code;
    public string Title => Requirement.Title;
    public string? Note => Requirement.Note;
    public string KindText { get; }
    public RiskRequirementRow(RiskRequirementDto requirement)
    {
        Requirement = requirement;
        KindText = ViewModelBase.Localizer[$"Track9LegalRequirementKind{requirement.Kind}"];
    }
}

public sealed class RiskDataRecordRow
{
    public RiskDataRecordDto Record { get; }
    public string? Name => Record.Name;
    public bool Catalogued => Record.Catalogued;
    public int FindingCount => Record.FindingCodes.Count;
    public string PersonalDataText { get; }
    public RiskDataRecordRow(RiskDataRecordDto record)
    {
        Record = record;
        PersonalDataText = record.PersonalData is { } category
            ? ViewModelBase.Localizer[$"Track9PersonalDataCategory{category}"] : string.Empty;
    }
}

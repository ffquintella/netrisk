using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.DataCatalogue;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

/// <summary>
/// Stage 9.11 LGPD catalogue. A save always sends typed purpose and location lists, including an
/// intentional empty declaration, so opening an older client cannot silently erase those lists.
/// </summary>
public class DataCatalogueViewModel : ViewModelBase
{
    private readonly IDataCatalogueService _service;
    private readonly Track9RegistersLoadGate _detailLoadGate = new();
    private readonly Track9RegistersLoadGate _listLoadGate = new();
    private bool _loaded;

    public string StrTitle { get; } = Localizer["Track9DataCatalogue"];
    public string StrRefresh { get; } = Localizer["Refresh"];
    public string StrOpen { get; } = Localizer["Open"];
    public string StrName { get; } = Localizer["Name"];
    public string StrFindingsOnly { get; } = Localizer["Track9FindingsOnly"];
    public string StrPersonalData { get; } = Localizer["Track9PersonalData"];
    public string StrDataSubjects { get; } = Localizer["Track9DataSubjects"];
    public string StrDataCategories { get; } = Localizer["Track9DataCategories"];
    public string StrRetention { get; } = Localizer["Track9Retention"];
    public string StrInvolvesMinors { get; } = Localizer["Track9InvolvesMinors"];
    public string StrLargeVolume { get; } = Localizer["Track9LargeVolume"];
    public string StrStrategicResearch { get; } = Localizer["Track9StrategicResearch"];
    public string StrTransferDeclared { get; } = Localizer["Track9TransferDeclared"];
    public string StrTransfer { get; } = Localizer["Track9InternationalTransfer"];
    public string StrPurposes { get; } = Localizer["Track9Purposes"];
    public string StrLocations { get; } = Localizer["Track9DataLocations"];
    public string StrProcessors { get; } = Localizer["Track9Processors"];
    public string StrDpias { get; } = Localizer["Track9Dpias"];
    public string StrFindings { get; } = Localizer["Findings"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrRemove { get; } = Localizer["Remove"];
    public string StrSaved { get; } = Localizer["Track9Saved"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrNoData { get; } = Localizer["Track9NoData"];
    public string StrCatalogued { get; } = Localizer["Track9Catalogued"];
    public string StrPurpose { get; } = Localizer["Track9Purpose"];
    public string StrLegalBasis { get; } = Localizer["Track9LegalBasis"];
    public string StrLegalRequirement { get; } = Localizer["Track9LegalRequirement"];
    public string StrBasisReference { get; } = Localizer["Track9BasisReference"];
    public string StrCountry { get; } = Localizer["Track9Country"];
    public string StrRegion { get; } = Localizer["Track9Region"];
    public string StrNotes { get; } = Localizer["Track9Notes"];
    public string StrRetentionPeriod { get; } = Localizer["Track9RetentionPeriod"];
    public string StrRetentionTrigger { get; } = Localizer["Track9RetentionTrigger"];
    public string StrRetentionBasis { get; } = Localizer["Track9RetentionBasis"];
    public string StrReviewDue { get; } = Localizer["Track9ReviewDue"];
    public string StrReviewedAt { get; } = Localizer["Track9ReviewedAt"];
    public string StrTransferMechanism { get; } = Localizer["Track9TransferMechanism"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrThroughGroup { get; } = Localizer["Track9ThroughGroup"];
    public string StrTitleColumn { get; } = Localizer["Track9Title"];
    public string StrResidualRisk { get; } = Localizer["Track9ResidualRisk"];

    public IReadOnlyList<Track9RegisterOption<PersonalDataCategory>> PersonalDataOptions { get; } = EnumOptions<PersonalDataCategory>();
    public IReadOnlyList<Track9RegisterOption<LgpdLegalBasis>> LegalBasisOptions { get; } = EnumOptions<LgpdLegalBasis>();
    public IReadOnlyList<Track9RegisterOption<InternationalTransferMechanism>> TransferMechanismOptions { get; } =
        EnumOptions<InternationalTransferMechanism>();
    public IReadOnlyList<Track9RegisterOption<DataLocationPurpose>> LocationPurposeOptions { get; } = EnumOptions<DataLocationPurpose>();

    public ObservableCollection<DataRecordSummaryRow> Items { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> RequirementOptions { get; } = [];
    public bool IsEmpty => _loaded && Items.Count == 0;

    private bool _withFindingsOnly;
    public bool WithFindingsOnly
    {
        get => _withFindingsOnly;
        set => this.RaiseAndSetIfChanged(ref _withFindingsOnly, value);
    }

    private DataRecordSummaryRow? _selectedItem;
    public DataRecordSummaryRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!ReferenceEquals(_selectedItem, value)) _detailLoadGate.DiscardOutstanding();
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
        }
    }

    private DataRecordDto? _selectedRecord;
    public DataRecordDto? SelectedRecord
    {
        get => _selectedRecord;
        private set
        {
            this.RaiseAndSetIfChanged(ref _selectedRecord, value);
            this.RaisePropertyChanged(nameof(HasSelection));
        }
    }
    public bool HasSelection => SelectedRecord is not null;
    public bool CanManage => Track9RegistersAccess.CanManageDataCatalogue(AuthenticationService.AuthenticatedUserInfo);
    public bool IsThirdLine => Track9RegistersAccess.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public string WriteDisabledReason => Localizer[IsThirdLine ? "Track9ThirdLineReadOnly" : "Track9ReadOnly"];

    private DataRecordDto _editor = new();
    public DataRecordDto Editor
    {
        get => _editor;
        private set
        {
            this.RaiseAndSetIfChanged(ref _editor, value);
            this.RaisePropertyChanged(nameof(SelectedPersonalData));
            this.RaisePropertyChanged(nameof(SelectedTransferMechanism));
        }
    }
    public PersonalDataCategory? SelectedPersonalData
    {
        get => Editor.PersonalData;
        set { if (value is null) return; Editor.PersonalData = value; this.RaisePropertyChanged(); }
    }
    public InternationalTransferMechanism? SelectedTransferMechanism
    {
        get => Editor.TransferMechanism;
        set { if (value is null) return; Editor.TransferMechanism = value; this.RaisePropertyChanged(); }
    }

    public ObservableCollection<DataPurposeDraftRow> PurposeDrafts { get; } = [];
    public ObservableCollection<DataLocationDraftRow> LocationDrafts { get; } = [];
    public ObservableCollection<DataProcessorRow> ProcessorRows { get; } = [];
    public ObservableCollection<DataDpiaRow> DpiaRows { get; } = [];
    public DataCataloguePurposeRequest NewPurpose { get; private set; } = new();
    public DataCatalogueLocationRequest NewLocation { get; private set; } = new();
    public LgpdLegalBasis? SelectedPurposeLegalBasis
    {
        get => NewPurpose.LegalBasis;
        set { if (value is null) return; NewPurpose.LegalBasis = value; this.RaisePropertyChanged(); }
    }
    public int? SelectedPurposeRequirementId
    {
        get => NewPurpose.LegalRequirementId;
        set { if (value is null) return; NewPurpose.LegalRequirementId = value; this.RaisePropertyChanged(); }
    }
    public DataLocationPurpose? SelectedNewLocationPurpose
    {
        get => NewLocation.Purpose;
        set { if (value is null) return; NewLocation.Purpose = value; this.RaisePropertyChanged(); }
    }
    public int? RetentionRequirementId { get; set; }

    private DataPurposeDraftRow? _selectedPurpose;
    public DataPurposeDraftRow? SelectedPurpose
    {
        get => _selectedPurpose;
        set => this.RaiseAndSetIfChanged(ref _selectedPurpose, value);
    }

    private DataLocationDraftRow? _selectedLocation;
    public DataLocationDraftRow? SelectedLocation
    {
        get => _selectedLocation;
        set => this.RaiseAndSetIfChanged(ref _selectedLocation, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddPurposeCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemovePurposeCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddLocationCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLocationCommand { get; }

    public DataCatalogueViewModel() : this(GetService<IDataCatalogueService>()) { }
    internal DataCatalogueViewModel(IDataCatalogueService service)
    {
        _service = service;
        ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        AddPurposeCommand = ReactiveCommand.Create(AddPurpose);
        RemovePurposeCommand = ReactiveCommand.Create(RemovePurpose);
        AddLocationCommand = ReactiveCommand.Create(AddLocation);
        RemoveLocationCommand = ReactiveCommand.Create(RemoveLocation);
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
                var items = await _service.GetRecordsAsync(WithFindingsOnly);
                var requirements = await _service.GetRequirementsAsync();
                if (!_listLoadGate.IsCurrent(generation)) return;
                Replace(Items, items.Select(item => new DataRecordSummaryRow(item)));
                Replace(RequirementOptions, requirements.OrderBy(requirement => requirement.Title)
                    .Select(requirement => new Track9RegisterOption<int>(requirement.Id,
                        $"{requirement.Code} — {requirement.Title}")));
                ErrorMessage = null;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load the data catalogue");
                if (!_listLoadGate.IsCurrent(generation)) return;
                ErrorMessage = StrLoadError;
            }
        });
    }

    private async Task OpenAsync()
    {
        if (SelectedItem is null) return;
        await LoadRecordAsync(SelectedItem.Summary.EntityId);
    }

    private async Task LoadRecordAsync(int entityId)
    {
        var generation = _detailLoadGate.Begin();
        await WithBusyAsync(async () =>
        {
            try
            {
                var record = await _service.GetRecordAsync(entityId);
                if (!_detailLoadGate.IsCurrent(generation)) return;
                SelectedRecord = record;
                Editor = record;
                Replace(PurposeDrafts, record.Purposes.Select(purpose => PurposeRow(new DataCataloguePurposeRequest
                {
                    Purpose = purpose.Purpose,
                    LegalBasis = purpose.LegalBasis,
                    LegalRequirementId = purpose.LegalRequirement?.Id,
                    BasisReference = purpose.BasisReference
                })));
                Replace(LocationDrafts, record.Locations.Select(location => new DataLocationDraftRow(new DataCatalogueLocationRequest
                {
                    Country = location.Country,
                    Region = location.Region,
                    Purpose = location.Purpose
                })));
                Replace(ProcessorRows, record.Processors.Select(processor => new DataProcessorRow(processor)));
                Replace(DpiaRows, record.Dpias.Select(dpia => new DataDpiaRow(dpia)));
                RetentionRequirementId = record.RetentionRequirement?.Id;
                this.RaisePropertyChanged(nameof(RetentionRequirementId));
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load data record {EntityId}", entityId);
                if (!_detailLoadGate.IsCurrent(generation)) return;
                ClearDetail();
                ErrorMessage = ExplainError(ex);
            }
        });
    }

    private async Task SaveAsync()
    {
        if (!CanManage || SelectedRecord is null) return;
        var entityId = SelectedRecord.EntityId;
        var request = Track9DataCatalogueForms.BuildRequest(Editor);
        request.Purposes = PurposeDrafts.Select(row => row.Request).ToList();
        request.Locations = LocationDrafts.Select(row => row.Request).ToList();
        request.RetentionRequirementId = RetentionRequirementId;
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            await _service.SaveRecordAsync(entityId, request);
            await ReloadAsync();
            if (_detailLoadGate.IsCurrent(generation)) await LoadRecordAsync(entityId);
        });
    }

    private void AddPurpose()
    {
        if (!CanManage) return;
        PurposeDrafts.Add(PurposeRow(NewPurpose));
        NewPurpose = new DataCataloguePurposeRequest();
        this.RaisePropertyChanged(nameof(NewPurpose));
        this.RaisePropertyChanged(nameof(SelectedPurposeLegalBasis));
        this.RaisePropertyChanged(nameof(SelectedPurposeRequirementId));
    }

    private void RemovePurpose()
    {
        if (!CanManage) return;
        if (SelectedPurpose is not null) PurposeDrafts.Remove(SelectedPurpose);
    }

    private void AddLocation()
    {
        if (!CanManage) return;
        LocationDrafts.Add(new DataLocationDraftRow(NewLocation));
        NewLocation = new DataCatalogueLocationRequest();
        this.RaisePropertyChanged(nameof(NewLocation));
        this.RaisePropertyChanged(nameof(SelectedNewLocationPurpose));
    }

    private void RemoveLocation()
    {
        if (!CanManage) return;
        if (SelectedLocation is not null) LocationDrafts.Remove(SelectedLocation);
    }

    private void ClearDetail()
    {
        SelectedRecord = null;
        Editor = new DataRecordDto();
        PurposeDrafts.Clear();
        LocationDrafts.Clear();
        ProcessorRows.Clear();
        DpiaRows.Clear();
        RetentionRequirementId = null;
        this.RaisePropertyChanged(nameof(RetentionRequirementId));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static IReadOnlyList<Track9RegisterOption<T>> EnumOptions<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new Track9RegisterOption<T>(value,
            Localizer[$"Track9{typeof(T).Name}{value}"])).ToList();

    private DataPurposeDraftRow PurposeRow(DataCataloguePurposeRequest request) => new(request,
        RequirementOptions.FirstOrDefault(option => option.Value == request.LegalRequirementId)?.Name ?? string.Empty);
}

public sealed class DataRecordSummaryRow
{
    public DataRecordSummaryDto Summary { get; }
    public string? Name => Summary.Name;
    public bool Catalogued => Summary.Catalogued;
    public int FindingCount => Summary.FindingCodes.Count;
    public string PersonalDataText { get; }

    public DataRecordSummaryRow(DataRecordSummaryDto summary)
    {
        Summary = summary;
        PersonalDataText = summary.PersonalData is { } category
            ? ViewModelBase.Localizer[$"Track9PersonalDataCategory{category}"] : string.Empty;
    }
}

public sealed class DataPurposeDraftRow
{
    public DataCataloguePurposeRequest Request { get; }
    public string? Purpose => Request.Purpose;
    public string? BasisReference => Request.BasisReference;
    public string RequirementName { get; }
    public string LegalBasisText { get; }

    public DataPurposeDraftRow(DataCataloguePurposeRequest request, string requirementName)
    {
        Request = request;
        RequirementName = requirementName;
        LegalBasisText = request.LegalBasis is { } basis
            ? ViewModelBase.Localizer[$"Track9LgpdLegalBasis{basis}"] : string.Empty;
    }
}

public sealed class DataLocationDraftRow
{
    public DataCatalogueLocationRequest Request { get; }
    public string? Country => Request.Country;
    public string? Region => Request.Region;
    public string PurposeText { get; }

    public DataLocationDraftRow(DataCatalogueLocationRequest request)
    {
        Request = request;
        PurposeText = request.Purpose is { } purpose
            ? ViewModelBase.Localizer[$"Track9DataLocationPurpose{purpose}"] : string.Empty;
    }
}

public sealed class DataProcessorRow
{
    public DataRecordProcessorDto Processor { get; }
    public string Name => Processor.Name;
    public bool ThroughGroup => Processor.ThroughGroup;
    public string StatusText { get; }

    public DataProcessorRow(DataRecordProcessorDto processor)
    {
        Processor = processor;
        StatusText = ViewModelBase.Localizer[$"Track9ThirdPartyStatus{processor.Status}"];
    }
}

public sealed class DataDpiaRow
{
    public DataRecordDpiaDto Dpia { get; }
    public string Title => Dpia.Title;
    public DateTime? NextReviewDueAt => Dpia.NextReviewDueAt;
    public string StatusText { get; }
    public string ResidualRiskText { get; }

    public DataDpiaRow(DataRecordDpiaDto dpia)
    {
        Dpia = dpia;
        StatusText = ViewModelBase.Localizer[$"Track9DpiaStatus{dpia.Status}"];
        ResidualRiskText = dpia.ResidualRisk is { } risk
            ? ViewModelBase.Localizer[$"Track9DpiaResidualRisk{risk}"] : string.Empty;
    }
}

/// <summary>Full read-only catalogue block mounted on an organization-data entity detail.</summary>
public class DataCatalogueRecordBlockViewModel : ViewModelBase
{
    private readonly IDataCatalogueService _service;
    private readonly Track9RegistersLoadGate _loadGate = new();
    public string StrTitle { get; } = Localizer["Track9DataCatalogue"];
    public string StrNotCatalogued { get; } = Localizer["Track9NotCatalogued"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrPersonalData { get; } = Localizer["Track9PersonalData"];
    public string StrPurposes { get; } = Localizer["Track9Purposes"];
    public string StrPurpose { get; } = Localizer["Track9Purpose"];
    public string StrLegalBasis { get; } = Localizer["Track9LegalBasis"];
    public string StrRetention { get; } = Localizer["Track9Retention"];
    public string StrRetentionPeriod { get; } = Localizer["Track9RetentionPeriod"];
    public string StrRetentionTrigger { get; } = Localizer["Track9RetentionTrigger"];
    public string StrRetentionBasis { get; } = Localizer["Track9RetentionBasis"];
    public string StrLegalRequirement { get; } = Localizer["Track9LegalRequirement"];
    public string StrLocations { get; } = Localizer["Track9DataLocations"];
    public string StrCountry { get; } = Localizer["Track9Country"];
    public string StrRegion { get; } = Localizer["Track9Region"];
    public string StrProcessors { get; } = Localizer["Track9Processors"];
    public string StrName { get; } = Localizer["Name"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrThroughGroup { get; } = Localizer["Track9ThroughGroup"];
    public string StrTransfer { get; } = Localizer["Track9InternationalTransfer"];
    public string StrTransferMechanism { get; } = Localizer["Track9TransferMechanism"];
    public string StrFindings { get; } = Localizer["Findings"];
    public string StrNone { get; } = Localizer["Track9NoneLinked"];

    public ObservableCollection<Track9RegisterOption<string>> LocationRows { get; } = [];
    public ObservableCollection<Track9RegisterOption<string>> ProcessorRows { get; } = [];

    private bool _hasTarget;
    public bool HasTarget
    {
        get => _hasTarget;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasTarget, value);
            this.RaisePropertyChanged(nameof(IsNotCatalogued));
        }
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private DataRecordDto? _record;
    public DataRecordDto? Record
    {
        get => _record;
        private set
        {
            this.RaiseAndSetIfChanged(ref _record, value);
            this.RaisePropertyChanged(nameof(HasRecord));
            this.RaisePropertyChanged(nameof(IsNotCatalogued));
            this.RaisePropertyChanged(nameof(PersonalDataText));
            this.RaisePropertyChanged(nameof(TransferMechanismText));
        }
    }
    public bool HasRecord => Record is not null;
    public bool IsNotCatalogued => HasTarget && Record is { Catalogued: false };
    public string PersonalDataText => Record?.PersonalData is { } category
        ? Localizer[$"Track9PersonalDataCategory{category}"] : string.Empty;
    public string TransferMechanismText => Record?.TransferMechanism is { } mechanism
        ? Localizer[$"Track9InternationalTransferMechanism{mechanism}"] : string.Empty;

    public DataCatalogueRecordBlockViewModel() : this(GetService<IDataCatalogueService>()) { }
    internal DataCatalogueRecordBlockViewModel(IDataCatalogueService service) => _service = service;

    public async Task LoadEntityAsync(int? entityId)
    {
        var generation = _loadGate.Begin();
        HasTarget = entityId is not null;
        Record = null;
        LocationRows.Clear();
        ProcessorRows.Clear();
        ErrorMessage = null;
        if (entityId is null) return;
        try
        {
            var record = await _service.GetRecordAsync(entityId.Value);
            if (!_loadGate.IsCurrent(generation)) return;
            Record = record;
            foreach (var location in record.Locations)
                LocationRows.Add(new Track9RegisterOption<string>(location.Country,
                    $"{location.Country} · {location.Region} · {Localizer[$"Track9DataLocationPurpose{location.Purpose}"]}"));
            foreach (var processor in record.Processors)
                ProcessorRows.Add(new Track9RegisterOption<string>(processor.Name,
                    $"{processor.Name} · {Localizer[$"Track9ThirdPartyStatus{processor.Status}"]}"));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unable to load data catalogue block for entity {EntityId}", entityId.Value);
            if (_loadGate.IsCurrent(generation)) ErrorMessage = StrLoadError;
        }
    }
}

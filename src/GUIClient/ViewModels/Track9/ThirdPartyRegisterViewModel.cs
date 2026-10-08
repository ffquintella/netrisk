using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.ThirdParties;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

/// <summary>
/// Stage 9.10 desktop register. The screen edits the typed REST contracts directly: supplier terms,
/// links, sub-processors, locations, HECVAT answers (including explicit blanks) and SBOM JSON text.
/// The server remains the authorization and validation authority for every write.
/// </summary>
public class ThirdPartyRegisterViewModel : ViewModelBase
{
    private readonly IThirdPartiesService _service;
    private readonly IEntitiesService _entitiesService;
    private readonly IUsersService _usersService;
    private readonly Track9RegistersLoadGate _detailLoadGate = new();
    private readonly Track9RegistersLoadGate _childLoadGate = new();
    private readonly Track9RegistersLoadGate _listLoadGate = new();
    private bool _loaded;

    public string StrTitle { get; } = Localizer["Track9ThirdPartyRegister"];
    public string StrRefresh { get; } = Localizer["Refresh"];
    public string StrNew { get; } = Localizer["New"];
    public string StrOpen { get; } = Localizer["Open"];
    public string StrDelete { get; } = Localizer["Delete"];
    public string StrName { get; } = Localizer["Name"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrContract { get; } = Localizer["Track9ContractTerms"];
    public string StrCloud { get; } = Localizer["Track9Cloud"];
    public string StrIdentity { get; } = Localizer["Track9Identity"];
    public string StrPersonalData { get; } = Localizer["Track9PersonalData"];
    public string StrLinks { get; } = Localizer["Track9Links"];
    public string StrSubprocessors { get; } = Localizer["Track9Subprocessors"];
    public string StrLocations { get; } = Localizer["Track9DataLocations"];
    public string StrHecvat { get; } = Localizer["Track9Hecvat"];
    public string StrSbom { get; } = Localizer["Track9Sbom"];
    public string StrFindings { get; } = Localizer["Findings"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrRemove { get; } = Localizer["Remove"];
    public string StrSaveList { get; } = Localizer["Track9SaveDeclaration"];
    public string StrNoSelection { get; } = Localizer["Track9SelectRecord"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrSaved { get; } = Localizer["Track9Saved"];
    public string StrDeleted { get; } = Localizer["Track9Deleted"];
    public string StrLinked { get; } = Localizer["Track9Linked"];
    public string StrUnlinked { get; } = Localizer["Track9Unlinked"];
    public string StrImported { get; } = Localizer["Track9Imported"];
    public string StrVoided { get; } = Localizer["Track9Voided"];
    public string StrVoid { get; } = Localizer["Track9Void"];
    public string StrLegalName { get; } = Localizer["Track9LegalName"];
    public string StrTaxId { get; } = Localizer["Track9TaxId"];
    public string StrCountry { get; } = Localizer["Track9Country"];
    public string StrRegion { get; } = Localizer["Track9Region"];
    public string StrDescription { get; } = Localizer["Track9Description"];
    public string StrWebsite { get; } = Localizer["Track9Website"];
    public string StrEntity { get; } = Localizer["Track9Entity"];
    public string StrOwner { get; } = Localizer["Track9Owner"];
    public string StrKind { get; } = Localizer["Track9Kind"];
    public string StrService { get; } = Localizer["Track9Service"];
    public string StrSupplier { get; } = Localizer["Track9Supplier"];
    public string StrPurpose { get; } = Localizer["Track9Purpose"];
    public string StrContractReference { get; } = Localizer["Track9ContractReference"];
    public string StrContractStart { get; } = Localizer["Track9ContractStart"];
    public string StrContractEnd { get; } = Localizer["Track9ContractEnd"];
    public string StrSla { get; } = Localizer["Track9SlaAvailability"];
    public string StrRto { get; } = Localizer["Track9RtoMinutes"];
    public string StrRpo { get; } = Localizer["Track9RpoMinutes"];
    public string StrVulnerabilityFix { get; } = Localizer["Track9VulnerabilityFixDays"];
    public string StrRightToAudit { get; } = Localizer["Track9RightToAudit"];
    public string StrAuditClause { get; } = Localizer["Track9AuditClause"];
    public string StrExitPlan { get; } = Localizer["Track9ExitPlan"];
    public string StrReviewedAt { get; } = Localizer["Track9ReviewedAt"];
    public string StrTestedAt { get; } = Localizer["Track9TestedAt"];
    public string StrDataPortability { get; } = Localizer["Track9DataPortability"];
    public string StrFrameworkVersion { get; } = Localizer["Track9FrameworkVersion"];
    public string StrExpectedQuestions { get; } = Localizer["Track9ExpectedQuestions"];
    public string StrRespondedAt { get; } = Localizer["Track9RespondedAt"];
    public string StrValidUntil { get; } = Localizer["Track9ValidUntil"];
    public string StrEvidence { get; } = Localizer["Track9Evidence"];
    public string StrNotes { get; } = Localizer["Track9Notes"];
    public string StrQuestion { get; } = Localizer["Track9Question"];
    public string StrAnswer { get; } = Localizer["Track9Answer"];
    public string StrPreferredAnswer { get; } = Localizer["Track9PreferredAnswer"];
    public string StrWeight { get; } = Localizer["Track9Weight"];
    public string StrCritical { get; } = Localizer["Track9Critical"];
    public string StrState { get; } = Localizer["Track9State"];
    public string StrAnswered { get; } = Localizer["Track9Answered"];
    public string StrUnanswered { get; } = Localizer["Track9Unanswered"];
    public string StrComponentName { get; } = Localizer["Track9ComponentName"];
    public string StrComponentVersion { get; } = Localizer["Track9ComponentVersion"];
    public string StrFileName { get; } = Localizer["Track9FileName"];
    public string StrFormat { get; } = Localizer["Track9Format"];
    public string StrComponentCount { get; } = Localizer["Track9ComponentCount"];
    public string StrSha256 { get; } = Localizer["Track9Sha256"];
    public string StrReason { get; } = Localizer["Track9Reason"];
    public string StrSbomDocument { get; } = Localizer["Track9SbomDocument"];
    public string StrPurl { get; } = Localizer["Track9Purl"];
    public string StrLicense { get; } = Localizer["Track9License"];
    public string StrNoData { get; } = Localizer["Track9NoData"];

    public IReadOnlyList<Track9RegisterOption<ThirdPartyStatus>> StatusOptions { get; } = EnumOptions<ThirdPartyStatus>();
    public IReadOnlyList<Track9RegisterOption<ThirdPartyDataLocationPurpose>> LocationPurposeOptions { get; } =
        EnumOptions<ThirdPartyDataLocationPurpose>();
    public IReadOnlyList<Track9RegisterOption<HecvatVariant>> HecvatVariantOptions { get; } = EnumOptions<HecvatVariant>();
    public IReadOnlyList<Track9RegisterOption<HecvatAnswer>> HecvatAnswerOptions { get; } = EnumOptions<HecvatAnswer>();

    public ObservableCollection<ThirdPartySummaryRow> Items { get; } = [];
    public bool IsEmpty => _loaded && Items.Count == 0;
    public ObservableCollection<Track9RegisterOption<int>> EntityOptions { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> UserOptions { get; } = [];
    public ObservableCollection<Track9RegisterOption<int>> ThirdPartyOptions { get; } = [];

    private ThirdPartySummaryRow? _selectedItem;
    public ThirdPartySummaryRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!ReferenceEquals(_selectedItem, value)) _detailLoadGate.DiscardOutstanding();
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
        }
    }

    private ThirdPartyDto? _selectedParty;
    public ThirdPartyDto? SelectedParty
    {
        get => _selectedParty;
        private set
        {
            this.RaiseAndSetIfChanged(ref _selectedParty, value);
            this.RaisePropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => SelectedParty is not null;

    private ThirdPartyDto _editor = NewEditor();
    public ThirdPartyDto Editor
    {
        get => _editor;
        private set
        {
            this.RaiseAndSetIfChanged(ref _editor, value);
            this.RaisePropertyChanged(nameof(SelectedEditorStatus));
            this.RaisePropertyChanged(nameof(SelectedEditorEntityId));
            this.RaisePropertyChanged(nameof(SelectedEditorOwnerId));
        }
    }
    public ThirdPartyStatus? SelectedEditorStatus
    {
        get => Editor.Status;
        set { if (value is null) return; Editor.Status = value.Value; this.RaisePropertyChanged(); }
    }
    public int? SelectedEditorEntityId
    {
        get => Editor.EntityId;
        set { if (value is null) return; Editor.EntityId = value; this.RaisePropertyChanged(); }
    }
    public int? SelectedEditorOwnerId
    {
        get => Editor.OwnerId;
        set { if (value is null) return; Editor.OwnerId = value; this.RaisePropertyChanged(); }
    }

    public ObservableCollection<ThirdPartySubprocessorRequest> SubprocessorDrafts { get; } = [];
    public ObservableCollection<ThirdPartyLocationDraftRow> LocationDrafts { get; } = [];
    public ObservableCollection<HecvatAnswerDraftRow> AnswerDrafts { get; } = [];
    public ObservableCollection<ThirdPartyLinkRow> LinkRows { get; } = [];
    public ObservableCollection<ThirdPartyAssessmentRow> AssessmentRows { get; } = [];
    public ObservableCollection<ThirdPartySbomRow> SbomRows { get; } = [];
    public ThirdPartySubprocessorRequest? SelectedSubprocessorDraft { get; set; }
    public ThirdPartyLocationDraftRow? SelectedLocationDraft { get; set; }
    public HecvatAnswerDraftRow? SelectedAnswerDraft { get; set; }

    private ThirdPartyLinkRow? _selectedLinkRow;
    public ThirdPartyLinkRow? SelectedLinkRow
    {
        get => _selectedLinkRow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLinkRow, value);
            SelectedLink = value?.Link;
        }
    }

    private ThirdPartyAssessmentRow? _selectedAssessmentRow;
    public ThirdPartyAssessmentRow? SelectedAssessmentRow
    {
        get => _selectedAssessmentRow;
        set
        {
            if (!ReferenceEquals(_selectedAssessmentRow, value)) _childLoadGate.DiscardOutstanding();
            this.RaiseAndSetIfChanged(ref _selectedAssessmentRow, value);
            SelectedAssessment = value?.Assessment;
        }
    }

    private ThirdPartySbomRow? _selectedSbomRow;
    public ThirdPartySbomRow? SelectedSbomRow
    {
        get => _selectedSbomRow;
        set
        {
            if (!ReferenceEquals(_selectedSbomRow, value)) _childLoadGate.DiscardOutstanding();
            this.RaiseAndSetIfChanged(ref _selectedSbomRow, value);
            SelectedSbom = value?.Sbom;
        }
    }

    private ThirdPartyLinkDto? _selectedLink;
    public ThirdPartyLinkDto? SelectedLink
    {
        get => _selectedLink;
        set => this.RaiseAndSetIfChanged(ref _selectedLink, value);
    }

    private ThirdPartyAssessmentDto? _selectedAssessment;
    public ThirdPartyAssessmentDto? SelectedAssessment
    {
        get => _selectedAssessment;
        set => this.RaiseAndSetIfChanged(ref _selectedAssessment, value);
    }

    private ThirdPartySbomDto? _selectedSbom;
    public ThirdPartySbomDto? SelectedSbom
    {
        get => _selectedSbom;
        set => this.RaiseAndSetIfChanged(ref _selectedSbom, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanManage => Track9RegistersAccess.CanManageThirdParties(AuthenticationService.AuthenticatedUserInfo);
    public bool IsThirdLine => Track9RegistersAccess.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public string WriteDisabledReason => Localizer[IsThirdLine ? "Track9ThirdLineReadOnly" : "Track9ReadOnly"];

    public int? LinkEntityId { get; set; }
    public string? LinkDescription { get; set; }
    public ThirdPartySubprocessorRequest NewSubprocessor { get; private set; } = new();
    public ThirdPartyDataLocationRequest NewLocation { get; private set; } = new();
    public int? SelectedSubprocessorThirdPartyId
    {
        get => NewSubprocessor.SubprocessorThirdPartyId;
        set { if (value is null) return; NewSubprocessor.SubprocessorThirdPartyId = value; this.RaisePropertyChanged(); }
    }
    public ThirdPartyDataLocationPurpose? SelectedLocationPurpose
    {
        get => NewLocation.Purpose;
        set { if (value is null) return; NewLocation.Purpose = value; this.RaisePropertyChanged(); }
    }
    public ThirdPartyAssessmentRequest AssessmentDraft { get; private set; } = new()
    {
        Variant = HecvatVariant.Unified,
        ExpectedQuestionCount = 1
    };
    public HecvatVariant? SelectedAssessmentVariant
    {
        get => AssessmentDraft.Variant;
        set
        {
            if (value is null || value.Value == AssessmentDraft.Variant) return;
            AssessmentDraft.Variant = value.Value;
            this.RaisePropertyChanged();
        }
    }
    public HecvatAnswerDto NewAnswer { get; private set; } = new()
    {
        Answer = HecvatAnswer.Unanswered,
        PreferredAnswer = HecvatAnswer.Yes,
        Weight = 1
    };
    public HecvatAnswer? SelectedAnswerValue
    {
        get => NewAnswer.Answer;
        set { if (value is null) return; NewAnswer.Answer = value.Value; this.RaisePropertyChanged(); }
    }
    public HecvatAnswer? SelectedPreferredAnswerValue
    {
        get => NewAnswer.PreferredAnswer;
        set { if (value is null) return; NewAnswer.PreferredAnswer = value; this.RaisePropertyChanged(); }
    }
    public string? AssessmentVoidReason { get; set; }
    public ThirdPartySbomRequest SbomDraft { get; private set; } = new();

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> LinkCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> UnlinkCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddSubprocessorCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveSubprocessorCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveSubprocessorsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddLocationCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLocationCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveLocationsCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RecordAssessmentCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenAssessmentCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddAnswerCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveAnswerCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveAnswersCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> VoidAssessmentCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ImportSbomCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> OpenSbomCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteSbomCommand { get; }

    public ThirdPartyRegisterViewModel() : this(GetService<IThirdPartiesService>(), GetService<IEntitiesService>(),
        GetService<IUsersService>()) { }

    internal ThirdPartyRegisterViewModel(IThirdPartiesService service) : this(service, GetService<IEntitiesService>(),
        GetService<IUsersService>()) { }

    internal ThirdPartyRegisterViewModel(IThirdPartiesService service, IEntitiesService entitiesService,
        IUsersService usersService)
    {
        _service = service;
        _entitiesService = entitiesService;
        _usersService = usersService;
        ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);
        NewCommand = ReactiveCommand.Create(NewRecord);
        OpenCommand = ReactiveCommand.CreateFromTask(OpenSelectedAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
        LinkCommand = ReactiveCommand.CreateFromTask(LinkAsync);
        UnlinkCommand = ReactiveCommand.CreateFromTask(UnlinkAsync);
        AddSubprocessorCommand = ReactiveCommand.Create(AddSubprocessor);
        RemoveSubprocessorCommand = ReactiveCommand.Create(() =>
        {
            if (!CanManage) return;
            if (SelectedSubprocessorDraft is not null) SubprocessorDrafts.Remove(SelectedSubprocessorDraft);
        });
        SaveSubprocessorsCommand = ReactiveCommand.CreateFromTask(SaveSubprocessorsAsync);
        AddLocationCommand = ReactiveCommand.Create(AddLocation);
        RemoveLocationCommand = ReactiveCommand.Create(() =>
        {
            if (!CanManage) return;
            if (SelectedLocationDraft is not null) LocationDrafts.Remove(SelectedLocationDraft);
        });
        SaveLocationsCommand = ReactiveCommand.CreateFromTask(SaveLocationsAsync);
        RecordAssessmentCommand = ReactiveCommand.CreateFromTask(RecordAssessmentAsync);
        OpenAssessmentCommand = ReactiveCommand.CreateFromTask(OpenAssessmentAsync);
        AddAnswerCommand = ReactiveCommand.Create(AddAnswer);
        RemoveAnswerCommand = ReactiveCommand.Create(() =>
        {
            if (!CanManage) return;
            if (SelectedAnswerDraft is not null) AnswerDrafts.Remove(SelectedAnswerDraft);
        });
        SaveAnswersCommand = ReactiveCommand.CreateFromTask(SaveAnswersAsync);
        VoidAssessmentCommand = ReactiveCommand.CreateFromTask(VoidAssessmentAsync);
        ImportSbomCommand = ReactiveCommand.CreateFromTask(ImportSbomAsync);
        OpenSbomCommand = ReactiveCommand.CreateFromTask(OpenSbomAsync);
        DeleteSbomCommand = ReactiveCommand.CreateFromTask(DeleteSbomAsync);
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
                var items = await _service.GetThirdPartiesAsync(includeTerminated: true);
                var entities = await _entitiesService.GetAllAsync(loadProperties: true);
                var users = await _usersService.GetAllAsync();
                if (!_listLoadGate.IsCurrent(generation)) return;
                Replace(Items, items.Select(item => new ThirdPartySummaryRow(item)));
                Replace(ThirdPartyOptions, items.OrderBy(item => item.Name)
                    .Select(item => new Track9RegisterOption<int>(item.Id, item.Name)));
                Replace(EntityOptions, entities.OrderBy(EntityName)
                    .Select(entity => new Track9RegisterOption<int>(entity.Id, EntityName(entity))));
                Replace(UserOptions, users.OrderBy(user => user.Name)
                    .Select(user => new Track9RegisterOption<int>(user.Id, user.Name)));
                ErrorMessage = null;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load the third-party register");
                if (!_listLoadGate.IsCurrent(generation)) return;
                ErrorMessage = StrLoadError;
            }
        });
    }

    private void NewRecord()
    {
        if (!CanManage) return;
        _detailLoadGate.DiscardOutstanding();
        _childLoadGate.DiscardOutstanding();
        SelectedItem = null;
        SelectedParty = null;
        Editor = NewEditor();
        FillDrafts(Editor);
    }

    private async Task OpenSelectedAsync()
    {
        if (SelectedItem is null) return;
        await LoadPartyAsync(SelectedItem.Summary.Id);
    }

    private async Task LoadPartyAsync(int id)
    {
        _childLoadGate.DiscardOutstanding();
        var generation = _detailLoadGate.Begin();
        await WithBusyAsync(async () =>
        {
            try
            {
                var party = await _service.GetThirdPartyAsync(id);
                if (!_detailLoadGate.IsCurrent(generation)) return;
                SelectedParty = party;
                Editor = party;
                FillDrafts(party);
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Unable to load third party {ThirdPartyId}", id);
                if (!_detailLoadGate.IsCurrent(generation)) return;
                ClearDetail();
                ErrorMessage = ExplainError(ex);
            }
        });
    }

    private async Task SaveAsync()
    {
        if (!CanManage) return;
        var savedId = SelectedParty?.Id;
        var request = Track9ThirdPartyForms.BuildRequest(Editor);
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrSaved, async () =>
        {
            var saved = savedId is null
                ? await _service.CreateAsync(request)
                : await _service.UpdateAsync(savedId.Value, request);
            await ReloadAsync();
            if (_detailLoadGate.IsCurrent(generation)) await LoadPartyAsync(saved.Id);
        });
    }

    private async Task DeleteAsync()
    {
        if (!CanManage || SelectedParty is null) return;
        var id = SelectedParty.Id;
        var generation = _detailLoadGate.Begin();
        await RunAsync(StrDeleted, async () =>
        {
            await _service.DeleteAsync(id);
            if (_detailLoadGate.IsCurrent(generation)) NewRecord();
            await ReloadAsync();
        });
    }

    private async Task LinkAsync()
    {
        if (!CanManage || SelectedParty is null || LinkEntityId is not int entityId) return;
        var partyId = SelectedParty.Id;
        await RunAsync(StrLinked, async () =>
        {
            await _service.LinkAsync(partyId, entityId,
                new ThirdPartyLinkRequest { Description = LinkDescription });
            if (SelectedParty?.Id == partyId) await LoadPartyAsync(partyId);
        });
    }

    private async Task UnlinkAsync()
    {
        if (!CanManage || SelectedParty is null || SelectedLink is null) return;
        var id = SelectedParty.Id;
        var entityId = SelectedLink.EntityId;
        await RunAsync(StrUnlinked, async () =>
        {
            await _service.UnlinkAsync(id, entityId);
            if (SelectedParty?.Id == id) await LoadPartyAsync(id);
        });
    }

    private void AddSubprocessor()
    {
        if (!CanManage) return;
        SubprocessorDrafts.Add(NewSubprocessor);
        NewSubprocessor = new ThirdPartySubprocessorRequest();
        this.RaisePropertyChanged(nameof(NewSubprocessor));
        this.RaisePropertyChanged(nameof(SelectedSubprocessorThirdPartyId));
    }

    private async Task SaveSubprocessorsAsync()
    {
        if (!CanManage || SelectedParty is null) return;
        var id = SelectedParty.Id;
        var request = new ThirdPartySubprocessorsRequest { Subprocessors = SubprocessorDrafts.ToList() };
        await RunAsync(StrSaved, async () =>
        {
            await _service.SetSubprocessorsAsync(id, request);
            if (SelectedParty?.Id == id) await LoadPartyAsync(id);
        });
    }

    private void AddLocation()
    {
        if (!CanManage) return;
        LocationDrafts.Add(new ThirdPartyLocationDraftRow(NewLocation));
        NewLocation = new ThirdPartyDataLocationRequest();
        this.RaisePropertyChanged(nameof(NewLocation));
        this.RaisePropertyChanged(nameof(SelectedLocationPurpose));
    }

    private async Task SaveLocationsAsync()
    {
        if (!CanManage || SelectedParty is null) return;
        var id = SelectedParty.Id;
        var request = new ThirdPartyDataLocationsRequest
        {
            Locations = LocationDrafts.Select(row => row.Location).ToList()
        };
        await RunAsync(StrSaved, async () =>
        {
            await _service.SetDataLocationsAsync(id, request);
            if (SelectedParty?.Id == id) await LoadPartyAsync(id);
        });
    }

    private async Task RecordAssessmentAsync()
    {
        if (!CanManage || SelectedParty is null) return;
        var id = SelectedParty.Id;
        var request = AssessmentDraft;
        await RunAsync(StrSaved, async () =>
        {
            await _service.RecordAssessmentAsync(id, request);
            if (SelectedParty?.Id == id)
            {
                AnswerDrafts.Clear();
                await LoadPartyAsync(id);
            }
        });
    }

    private async Task OpenAssessmentAsync()
    {
        if (SelectedParty is null || SelectedAssessment is null) return;
        var partyId = SelectedParty.Id;
        var assessmentId = SelectedAssessment.Id;
        var generation = _childLoadGate.Begin();
        try
        {
            var assessment = await _service.GetAssessmentAsync(partyId, assessmentId);
            if (!_childLoadGate.IsCurrent(generation) || SelectedParty?.Id != partyId ||
                SelectedAssessment?.Id != assessmentId) return;
            SelectedAssessment = assessment;
            Replace(AnswerDrafts, assessment.Answers.Select(answer => new HecvatAnswerDraftRow(answer)));
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unable to load assessment {AssessmentId} for third party {ThirdPartyId}", assessmentId, partyId);
            if (_childLoadGate.IsCurrent(generation)) ErrorMessage = ExplainError(ex);
        }
    }

    private void AddAnswer()
    {
        if (!CanManage) return;
        AnswerDrafts.Add(new HecvatAnswerDraftRow(NewAnswer));
        NewAnswer = new HecvatAnswerDto
        {
            Answer = HecvatAnswer.Unanswered,
            PreferredAnswer = HecvatAnswer.Yes,
            Weight = 1
        };
        this.RaisePropertyChanged(nameof(NewAnswer));
        this.RaisePropertyChanged(nameof(SelectedAnswerValue));
        this.RaisePropertyChanged(nameof(SelectedPreferredAnswerValue));
    }

    private async Task SaveAnswersAsync()
    {
        if (!CanManage || SelectedParty is null || SelectedAssessment is null) return;
        var partyId = SelectedParty.Id;
        var assessmentId = SelectedAssessment.Id;
        var request = Track9ThirdPartyForms.BuildAnswers(AnswerDrafts.Select(row => row.Answer));
        await RunAsync(StrSaved, async () =>
        {
            await _service.ReplaceAnswersAsync(partyId, assessmentId, request);
            if (SelectedParty?.Id == partyId && SelectedAssessment?.Id == assessmentId)
                await LoadPartyAsync(partyId);
        });
    }

    private async Task VoidAssessmentAsync()
    {
        if (!CanManage || SelectedParty is null || SelectedAssessment is null) return;
        var partyId = SelectedParty.Id;
        var assessmentId = SelectedAssessment.Id;
        await RunAsync(StrVoided, async () =>
        {
            await _service.VoidAssessmentAsync(partyId, assessmentId,
                new ThirdPartyAssessmentVoidRequest { Reason = AssessmentVoidReason });
            if (SelectedParty?.Id == partyId && SelectedAssessment?.Id == assessmentId)
                await LoadPartyAsync(partyId);
        });
    }

    private async Task ImportSbomAsync()
    {
        if (!CanManage || SelectedParty is null) return;
        var id = SelectedParty.Id;
        var request = SbomDraft;
        await RunAsync(StrImported, async () =>
        {
            await _service.ImportSbomAsync(id, request);
            if (SelectedParty?.Id == id)
            {
                SbomDraft = new ThirdPartySbomRequest();
                this.RaisePropertyChanged(nameof(SbomDraft));
                await LoadPartyAsync(id);
            }
        });
    }

    private async Task OpenSbomAsync()
    {
        if (SelectedParty is null || SelectedSbom is null) return;
        var partyId = SelectedParty.Id;
        var sbomId = SelectedSbom.Id;
        var generation = _childLoadGate.Begin();
        try
        {
            var sbom = await _service.GetSbomAsync(partyId, sbomId);
            if (!_childLoadGate.IsCurrent(generation) || SelectedParty?.Id != partyId || SelectedSbom?.Id != sbomId)
                return;
            SelectedSbom = sbom;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unable to load SBOM {SbomId} for third party {ThirdPartyId}", sbomId, partyId);
            if (_childLoadGate.IsCurrent(generation)) ErrorMessage = ExplainError(ex);
        }
    }

    private async Task DeleteSbomAsync()
    {
        if (!CanManage || SelectedParty is null || SelectedSbom is null) return;
        var id = SelectedParty.Id;
        var sbomId = SelectedSbom.Id;
        await RunAsync(StrDeleted, async () =>
        {
            await _service.DeleteSbomAsync(id, sbomId);
            if (SelectedParty?.Id == id && SelectedSbom?.Id == sbomId)
            {
                SelectedSbom = null;
                await LoadPartyAsync(id);
            }
        });
    }

    private void FillDrafts(ThirdPartyDto party)
    {
        Replace(SubprocessorDrafts, party.Subprocessors.Select(item => new ThirdPartySubprocessorRequest
        {
            Name = item.Name,
            SubprocessorThirdPartyId = item.SubprocessorThirdPartyId,
            Service = item.Service,
            Country = item.Country,
            ProcessesPersonalData = item.ProcessesPersonalData
        }));
        Replace(LocationDrafts, party.DataLocations.Select(item => new ThirdPartyLocationDraftRow(new ThirdPartyDataLocationRequest
        {
            Country = item.Country,
            Region = item.Region,
            Purpose = item.Purpose
        })));
        Replace(LinkRows, party.Links.Select(item => new ThirdPartyLinkRow(item)));
        Replace(AssessmentRows, party.Assessments.Select(item => new ThirdPartyAssessmentRow(item)));
        Replace(SbomRows, party.Sboms.Select(item => new ThirdPartySbomRow(item)));
        SelectedAssessmentRow = null;
        SelectedSbomRow = null;
        SelectedLinkRow = null;
        AnswerDrafts.Clear();
    }

    private void ClearDetail()
    {
        SelectedParty = null;
        Editor = NewEditor();
        SubprocessorDrafts.Clear();
        LocationDrafts.Clear();
        AnswerDrafts.Clear();
        LinkRows.Clear();
        AssessmentRows.Clear();
        SbomRows.Clear();
        SelectedAssessment = null;
        SelectedSbom = null;
    }

    private static ThirdPartyDto NewEditor() => new() { Status = ThirdPartyStatus.Prospective };

    private static string EntityName(DAL.Entities.Entity entity) =>
        entity.EntitiesProperties.FirstOrDefault(property => property.Type == "name")?.Value ?? $"#{entity.Id}";

    private static IReadOnlyList<Track9RegisterOption<T>> EnumOptions<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new Track9RegisterOption<T>(value,
            Localizer[$"Track9{typeof(T).Name}{value}"])).ToList();

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }
}

public sealed class ThirdPartySummaryRow
{
    public ThirdPartySummaryDto Summary { get; }
    public string Name => Summary.Name;
    public int FindingCount => Summary.FindingCount;
    public string StatusText { get; }

    public ThirdPartySummaryRow(ThirdPartySummaryDto summary)
    {
        Summary = summary;
        StatusText = ViewModelBase.Localizer[$"Track9ThirdPartyStatus{summary.Status}"];
    }
}

public sealed class ThirdPartyLinkRow
{
    public ThirdPartyLinkDto Link { get; }
    public string? EntityName => Link.EntityName;
    public string? Description => Link.Description;
    public string KindText { get; }

    public ThirdPartyLinkRow(ThirdPartyLinkDto link)
    {
        Link = link;
        KindText = ViewModelBase.Localizer[$"Track9ThirdPartyLinkKind{link.Kind}"];
    }
}

public sealed class ThirdPartyLocationDraftRow
{
    public ThirdPartyDataLocationRequest Location { get; }
    public string? Country => Location.Country;
    public string? Region => Location.Region;
    public string PurposeText { get; }

    public ThirdPartyLocationDraftRow(ThirdPartyDataLocationRequest location)
    {
        Location = location;
        PurposeText = location.Purpose is { } purpose
            ? ViewModelBase.Localizer[$"Track9ThirdPartyDataLocationPurpose{purpose}"] : string.Empty;
    }
}

public sealed class HecvatAnswerDraftRow
{
    public HecvatAnswerDto Answer { get; }
    public string QuestionId => Answer.QuestionId;
    public int Weight => Answer.Weight;
    public bool Critical => Answer.Critical;
    public string? Notes => Answer.Notes;
    public string AnswerText { get; }
    public string PreferredAnswerText { get; }

    public HecvatAnswerDraftRow(HecvatAnswerDto answer)
    {
        Answer = answer;
        AnswerText = ViewModelBase.Localizer[$"Track9HecvatAnswer{answer.Answer}"];
        PreferredAnswerText = answer.PreferredAnswer is { } preferred
            ? ViewModelBase.Localizer[$"Track9HecvatAnswer{preferred}"] : string.Empty;
    }
}

public sealed class ThirdPartyAssessmentRow
{
    public ThirdPartyAssessmentDto Assessment { get; }
    public int Id => Assessment.Id;
    public int AnsweredCount => Assessment.Result.AnsweredCount;
    public int UnansweredCount => Assessment.Result.UnansweredCount;
    public string VariantText { get; }
    public string StateText { get; }

    public ThirdPartyAssessmentRow(ThirdPartyAssessmentDto assessment)
    {
        Assessment = assessment;
        VariantText = ViewModelBase.Localizer[$"Track9HecvatVariant{assessment.Variant}"];
        StateText = ViewModelBase.Localizer[$"Track9HecvatState{assessment.Result.State}"];
    }
}

public sealed class ThirdPartySbomRow
{
    public ThirdPartySbomDto Sbom { get; }
    public string ComponentName => Sbom.ComponentName;
    public string? ComponentVersion => Sbom.ComponentVersion;
    public int ComponentCount => Sbom.ComponentCount;
    public string DocumentSha256 => Sbom.DocumentSha256;
    public string FormatText { get; }

    public ThirdPartySbomRow(ThirdPartySbomDto sbom)
    {
        Sbom = sbom;
        FormatText = ViewModelBase.Localizer[$"Track9SbomFormat{sbom.Format}"];
    }
}

/// <summary>Read-only third-party block mounted on service, process and data entity details.</summary>
public class EntityThirdPartiesBlockViewModel : ViewModelBase
{
    private readonly IThirdPartiesService _service;
    private readonly Track9RegistersLoadGate _loadGate = new();
    public string StrTitle { get; } = Localizer["Track9ThirdPartiesBlock"];
    public string StrNone { get; } = Localizer["Track9NoneLinked"];
    public string StrLoadError { get; } = Localizer["Track9LoadError"];
    public string StrName { get; } = Localizer["Name"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrKind { get; } = Localizer["Track9Kind"];
    public string StrDescription { get; } = Localizer["Track9Description"];
    public ObservableCollection<EntityThirdPartyRow> Items { get; } = [];
    public bool IsEmpty => Items.Count == 0;
    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public EntityThirdPartiesBlockViewModel() : this(GetService<IThirdPartiesService>()) { }
    internal EntityThirdPartiesBlockViewModel(IThirdPartiesService service) => _service = service;

    public async Task LoadEntityAsync(int? entityId)
    {
        var generation = _loadGate.Begin();
        Items.Clear();
        ErrorMessage = null;
        if (entityId is null)
        {
            this.RaisePropertyChanged(nameof(IsEmpty));
            return;
        }

        try
        {
            var items = await _service.GetByEntityAsync(entityId.Value);
            if (!_loadGate.IsCurrent(generation)) return;
            foreach (var item in items) Items.Add(new EntityThirdPartyRow(item));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unable to load third parties for entity {EntityId}", entityId.Value);
            if (_loadGate.IsCurrent(generation)) ErrorMessage = StrLoadError;
        }
        this.RaisePropertyChanged(nameof(IsEmpty));
    }
}

public sealed class EntityThirdPartyRow
{
    public EntityThirdPartyDto Party { get; }
    public string Name => Party.Name;
    public string? Description => Party.Description;
    public string StatusText { get; }
    public string KindText { get; }

    public EntityThirdPartyRow(EntityThirdPartyDto party)
    {
        Party = party;
        StatusText = ViewModelBase.Localizer[$"Track9ThirdPartyStatus{party.Status}"];
        KindText = ViewModelBase.Localizer[$"Track9ThirdPartyLinkKind{party.Kind}"];
    }
}

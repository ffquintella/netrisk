using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Tools;
using Model.DTO;
using Model.Governance;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Admin;

/// <summary>
/// The Track 8 governance administration screen: risk appetite (8.3.3), the risks above it, the
/// assessment intake queue (8.5.2), reviewer appointments (8.6.2) and the legacy workflow-violation
/// report (8.3.1).
///
/// One view model behind five tabs, for the same reason <see cref="FindingsAdminViewModel"/> is:
/// somebody standing the governance layer up does all five in one sitting, and five near-identical
/// load/save view models would be five places to fix the same bug.
/// </summary>
public class GovernanceAdminViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrRiskAppetite { get; } = Localizer["RiskAppetite"];
    public string StrAboveAppetite { get; } = Localizer["RisksAboveAppetite"];
    public string StrPendingRisks { get; } = Localizer["PendingRisks"];
    public string StrReviewers { get; } = Localizer["EntityRiskReviewers"];
    public string StrWorkflowViolations { get; } = Localizer["WorkflowViolations"];
    public string StrMaxAcceptableResidual { get; } = Localizer["MaxAcceptableResidual"];
    public string StrDualApprovalThreshold { get; } = Localizer["DualApprovalThreshold"];
    public string StrGlobal { get; } = Localizer["Global"];
    public string StrEntity { get; } = Localizer["Entity"];
    public string StrNotes { get; } = Localizer["Notes"];
    public string StrDelete { get; } = Localizer["Delete"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrCount { get; } = Localizer["Count"];
    public string StrSubject { get; } = Localizer["Subject"];
    public string StrScore { get; } = Localizer["Score"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrPromote { get; } = Localizer["Promote"];
    public string StrDismiss { get; } = Localizer["Dismiss"];
    public string StrReason { get; } = Localizer["Reason"];
    public string StrUser { get; } = Localizer["User"];
    public string StrPrimary { get; } = Localizer["Primary"];
    public string StrAppoint { get; } = Localizer["Appoint"];
    public string StrRemove { get; } = Localizer["Remove"];
    public string StrRisk { get; } = Localizer["Risk"];
    public string StrNoAppetiteConfiguredMsg { get; } = Localizer["NoAppetiteConfiguredMSG"];
    public string StrWorkflowViolationsMsg { get; } = Localizer["WorkflowViolationsMSG"];
    public string StrPendingRisksMsg { get; } = Localizer["PendingRisksMSG"];
    public string StrReviewersMsg { get; } = Localizer["EntityRiskReviewersMSG"];

    // Stage 9.2 (S42 §7): the origin column and the standalone hypothesis form.
    public string StrOrigin { get; } = Localizer["PendingOrigin"];
    public string StrNewHypothesis { get; } = Localizer["NewHypothesis"];
    public string StrHypothesisDescription { get; } = Localizer["HypothesisDescription"];
    public string StrRegisterHypothesis { get; } = Localizer["RegisterHypothesis"];

    #endregion

    #region SERVICES

    private IRiskGovernanceService GovernanceService { get; } = GetService<IRiskGovernanceService>();

    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();

    private IUsersService UsersService { get; } = GetService<IUsersService>();

    #endregion

    #region PROPERTIES

    public ObservableCollection<AppetiteRow> Appetites { get; } = [];

    private AppetiteRow? _selectedAppetite;

    public AppetiteRow? SelectedAppetite
    {
        get => _selectedAppetite;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAppetite, value);

            // The editor is a copy, never the row in the grid: binding the grid's instance means a
            // half-typed threshold is already "saved" as far as every other panel reading the list is
            // concerned.
            MaxAcceptableResidual = value?.MaxAcceptableResidual ?? 0;
            DualApprovalThreshold = value?.DualApprovalThreshold ?? 0;
            AppetiteNotes = value?.Notes;
            SelectedAppetiteEntityId = value?.EntityId;
        }
    }

    private double _maxAcceptableResidual;

    public double MaxAcceptableResidual
    {
        get => _maxAcceptableResidual;
        set => this.RaiseAndSetIfChanged(ref _maxAcceptableResidual, value);
    }

    private double _dualApprovalThreshold;

    public double DualApprovalThreshold
    {
        get => _dualApprovalThreshold;
        set => this.RaiseAndSetIfChanged(ref _dualApprovalThreshold, value);
    }

    private string? _appetiteNotes;

    public string? AppetiteNotes
    {
        get => _appetiteNotes;
        set => this.RaiseAndSetIfChanged(ref _appetiteNotes, value);
    }

    private int? _selectedAppetiteEntityId;

    /// <summary>Null means the organization-wide appetite.</summary>
    public int? SelectedAppetiteEntityId
    {
        get => _selectedAppetiteEntityId;
        set => this.RaiseAndSetIfChanged(ref _selectedAppetiteEntityId, value);
    }

    private bool _appetiteConfigured;

    /// <summary>
    /// False when no appetite row exists at all — the seeded state, in which nothing is gated. The
    /// view says so explicitly rather than showing zeros, because "no ceiling" and "a ceiling of
    /// zero" are opposite things.
    /// </summary>
    public bool AppetiteConfigured
    {
        get => _appetiteConfigured;
        set => this.RaiseAndSetIfChanged(ref _appetiteConfigured, value);
    }

    public ObservableCollection<AppetiteBreachRow> AboveAppetite { get; } = [];

    public ObservableCollection<PendingRiskRow> PendingRisks { get; } = [];

    private PendingRiskRow? _selectedPendingRisk;

    public PendingRiskRow? SelectedPendingRisk
    {
        get => _selectedPendingRisk;
        set => this.RaiseAndSetIfChanged(ref _selectedPendingRisk, value);
    }

    private string? _hypothesisSubject;

    /// <summary>The subject of a standalone hypothesis about to be registered (T152).</summary>
    public string? HypothesisSubject
    {
        get => _hypothesisSubject;
        set => this.RaiseAndSetIfChanged(ref _hypothesisSubject, value);
    }

    private int? _hypothesisEntityId;

    /// <summary>
    /// The entity the hypothesis is filed under (S42 §5.4). Left empty, the server files it under the
    /// caller's only entity, asks a caller with several to choose, or files it organization-wide for an
    /// unrestricted caller.
    /// </summary>
    public int? HypothesisEntityId
    {
        get => _hypothesisEntityId;
        set => this.RaiseAndSetIfChanged(ref _hypothesisEntityId, value);
    }

    private string? _hypothesisDescription;

    /// <summary>Why it is suspected — stored as the pending risk's comment.</summary>
    public string? HypothesisDescription
    {
        get => _hypothesisDescription;
        set => this.RaiseAndSetIfChanged(ref _hypothesisDescription, value);
    }

    private string? _triageReason;

    public string? TriageReason
    {
        get => _triageReason;
        set => this.RaiseAndSetIfChanged(ref _triageReason, value);
    }

    public ObservableCollection<Entity> Entities { get; } = [];

    private Entity? _selectedEntity;

    public Entity? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedEntity, value);
            _ = LoadReviewersAsync();
        }
    }

    public ObservableCollection<EntityRiskReviewer> Reviewers { get; } = [];

    private EntityRiskReviewer? _selectedReviewer;

    public EntityRiskReviewer? SelectedReviewer
    {
        get => _selectedReviewer;
        set => this.RaiseAndSetIfChanged(ref _selectedReviewer, value);
    }

    public ObservableCollection<UserListing> Users { get; } = [];

    private UserListing? _selectedUser;

    public UserListing? SelectedUser
    {
        get => _selectedUser;
        set => this.RaiseAndSetIfChanged(ref _selectedUser, value);
    }

    private bool _appointAsPrimary;

    public bool AppointAsPrimary
    {
        get => _appointAsPrimary;
        set => this.RaiseAndSetIfChanged(ref _appointAsPrimary, value);
    }

    public ObservableCollection<WorkflowViolation> Violations { get; } = [];

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveAppetiteClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteAppetiteClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtPromotePendingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDismissPendingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRegisterHypothesisClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAppointReviewerClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveReviewerClicked { get; }

    #endregion

    public GovernanceAdminViewModel()
    {
        BtReloadClicked = ReactiveCommand.CreateFromTask(InitializeAsync);
        BtSaveAppetiteClicked = ReactiveCommand.CreateFromTask(SaveAppetiteAsync);
        BtDeleteAppetiteClicked = ReactiveCommand.CreateFromTask(DeleteAppetiteAsync);
        BtPromotePendingClicked = ReactiveCommand.CreateFromTask(PromotePendingAsync);
        BtDismissPendingClicked = ReactiveCommand.CreateFromTask(DismissPendingAsync);
        BtRegisterHypothesisClicked = ReactiveCommand.CreateFromTask(RegisterHypothesisAsync,
            this.WhenAnyValue(x => x.HypothesisSubject, subject => !string.IsNullOrWhiteSpace(subject)));
        BtAppointReviewerClicked = ReactiveCommand.CreateFromTask(AppointReviewerAsync);
        BtRemoveReviewerClicked = ReactiveCommand.CreateFromTask(RemoveReviewerAsync);
    }

    public async Task InitializeAsync()
    {
        await WithBusyAsync(async () =>
        {
            await LoadAppetitesAsync();
            // Entities first: the pending-risk rows name the entity each hypothesis is filed under.
            await LoadEntitiesAndUsersAsync();
            await LoadPendingRisksAsync();
            await LoadViolationsAsync();
        });
    }

    // --- 8.3.3 appetite -----------------------------------------------------------------------

    private async Task LoadAppetitesAsync()
    {
        var appetites = await GovernanceService.GetAppetitesAsync();

        // Wrapped as they arrive, so the scope cell is a string the grid can render rather than an
        // entity id the grid would have to resolve.
        Appetites.Clear();
        foreach (var appetite in appetites) Appetites.Add(new AppetiteRow(appetite, StrGlobal));

        AppetiteConfigured = Appetites.Count > 0;
        SelectedAppetite = Appetites.FirstOrDefault();

        var counts = await GovernanceService.GetRisksAboveAppetiteAsync();

        AboveAppetite.Clear();
        foreach (var count in counts) AboveAppetite.Add(new AppetiteBreachRow(count, StrGlobal));
    }

    private async Task SaveAppetiteAsync()
    {
        await RunAsync(Localizer["AppetiteSavedMSG"], async () =>
        {
            await GovernanceService.SaveAppetiteAsync(new RiskAppetite
            {
                Id = SelectedAppetite?.EntityId == SelectedAppetiteEntityId ? SelectedAppetite?.Id ?? 0 : 0,
                EntityId = SelectedAppetiteEntityId,
                MaxAcceptableResidual = MaxAcceptableResidual,
                DualApprovalThreshold = DualApprovalThreshold,
                Notes = AppetiteNotes
            });

            await LoadAppetitesAsync();
        });
    }

    private async Task DeleteAppetiteAsync()
    {
        if (SelectedAppetite is null) return;

        await RunAsync(Localizer["AppetiteDeletedMSG"], async () =>
        {
            await GovernanceService.DeleteAppetiteAsync(SelectedAppetite.Id);
            await LoadAppetitesAsync();
        });
    }

    // --- 8.5.2 intake triage ------------------------------------------------------------------

    private async Task LoadPendingRisksAsync()
    {
        var pending = await GovernanceService.GetPendingRisksAsync();

        PendingRisks.Clear();
        foreach (var row in pending)
            PendingRisks.Add(new PendingRiskRow(row, Localizer[RiskScenarioSummary.OriginKey(row.Origin)],
                Entities.FirstOrDefault(e => e.Id == row.EntityId)?.DisplayName, StrGlobal));
    }

    /// <summary>
    /// Registers a standalone hypothesis (Stage 9.2, T152) — one no assessment answer raised — into
    /// this same queue, where it is promoted or dismissed like any other row.
    /// </summary>
    private async Task RegisterHypothesisAsync()
    {
        if (string.IsNullOrWhiteSpace(HypothesisSubject)) return;

        await RunAsync(Localizer["HypothesisRegisteredMSG"], async () =>
        {
            await GovernanceService.CreateHypothesisAsync(new HypothesisRequest
            {
                Subject = HypothesisSubject,
                Description = HypothesisDescription,
                EntityId = HypothesisEntityId
            });

            HypothesisSubject = null;
            HypothesisDescription = null;
            await LoadPendingRisksAsync();
        });
    }

    private async Task PromotePendingAsync()
    {
        if (SelectedPendingRisk is null) return;

        await RunAsync(Localizer["PendingRiskPromotedMSG"], async () =>
        {
            await GovernanceService.PromotePendingRiskAsync(SelectedPendingRisk.Id,
                new PendingRiskPromotion
                {
                    Subject = SelectedPendingRisk.Listing.Subject,
                    Notes = SelectedPendingRisk.Listing.Comment,
                    OwnerId = SelectedPendingRisk.Listing.OwnerId
                });

            await LoadPendingRisksAsync();
        });
    }

    private async Task DismissPendingAsync()
    {
        if (SelectedPendingRisk is null) return;

        // The reason is mandatory server-side; refusing here avoids a round trip to be told so, and
        // the message is the same one the server would have sent.
        if (string.IsNullOrWhiteSpace(TriageReason))
        {
            Toasts.Error(Localizer["DismissalNeedsAReasonMSG"]);
            return;
        }

        await RunAsync(Localizer["PendingRiskDismissedMSG"], async () =>
        {
            await GovernanceService.DismissPendingRiskAsync(SelectedPendingRisk.Id, TriageReason!);
            TriageReason = null;
            await LoadPendingRisksAsync();
        });
    }

    // --- 8.6.2 reviewer appointments ----------------------------------------------------------

    private async Task LoadEntitiesAndUsersAsync()
    {
        var entities = await EntitiesService.GetAllAsync("organization");

        Entities.Clear();
        foreach (var entity in entities) Entities.Add(entity);

        // UserListing carries no enabled flag; the server refuses to appoint a disabled account and
        // its message says why, which is a better place for that rule than a client-side filter over
        // data the client does not have.
        var users = await UsersService.GetAllAsync();

        Users.Clear();
        foreach (var user in users) Users.Add(user);

        SelectedEntity = Entities.FirstOrDefault();
    }

    private async Task LoadReviewersAsync()
    {
        Reviewers.Clear();

        if (SelectedEntity is null) return;

        var reviewers = await GovernanceService.GetEntityReviewersAsync(SelectedEntity.Id);
        foreach (var reviewer in reviewers) Reviewers.Add(reviewer);
    }

    private async Task AppointReviewerAsync()
    {
        if (SelectedEntity is null || SelectedUser is null) return;

        await RunAsync(Localizer["ReviewerAppointedMSG"], async () =>
        {
            await GovernanceService.AppointReviewerAsync(SelectedEntity.Id, SelectedUser.Id,
                AppointAsPrimary);

            await LoadReviewersAsync();
        });
    }

    private async Task RemoveReviewerAsync()
    {
        if (SelectedReviewer is null) return;

        await RunAsync(Localizer["ReviewerRemovedMSG"], async () =>
        {
            await GovernanceService.RemoveReviewerAsync(SelectedReviewer.Id);
            await LoadReviewersAsync();
        });
    }

    // --- 8.3.1 legacy violations --------------------------------------------------------------

    private async Task LoadViolationsAsync()
    {
        Violations.Clear();

        // Reported, never repaired from here. Auto-mutating a legacy status would destroy the record
        // of how the risk got there, which is the only thing that makes the report useful.
        var violations = await GovernanceService.GetWorkflowViolationsAsync();

        foreach (var violation in violations) Violations.Add(violation);
    }
}

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Continuity;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// The "Continuity (BIA)" block under the entity form (Stage 9.3, S43 §7), shown for business processes
/// and IT services to whoever may read continuity data: MTPD/MAO, RTO and RPO, the effective
/// criticality and where it comes from, the verification of each objective, the cascade (conflicts,
/// requirements without an objective, cycles, the tightest dependent MTPD), the weighted threat, and
/// the direct dependencies.
///
/// Every state is text, never colour. The buttons follow the caller flags the server computes from the
/// permission and the scope — hiding a button is not the control; the server decides every write.
/// </summary>
public class ContinuityPanelViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrContinuity { get; } = Localizer["Continuity"];
    public string StrMtpd { get; } = Localizer["Mtpd"];
    public string StrRto { get; } = Localizer["Rto"];
    public string StrRpo { get; } = Localizer["Rpo"];
    public string StrAssessedAt { get; } = Localizer["BiaAssessedAt"];
    public string StrEffectiveCriticality { get; } = Localizer["EffectiveCriticality"];
    public string StrRtoVerification { get; } = Localizer["RtoVerification"];
    public string StrRpoVerification { get; } = Localizer["RpoVerification"];
    public string StrCascadeConflicts { get; } = Localizer["CascadeConflicts"];
    public string StrContinuityThreat { get; } = Localizer["ContinuityThreat"];
    public string StrDependsOn { get; } = Localizer["DependsOn"];
    public string StrDependencyDirection { get; } = Localizer["DependencyDirection"];
    public string StrName { get; } = Localizer["Name"];
    public string StrDescription { get; } = Localizer["Description"];
    public string StrEditBia { get; } = Localizer["EditBia"];
    public string StrDeleteBia { get; } = Localizer["DeleteBia"];
    public string StrAddDependency { get; } = Localizer["AddDependency"];
    public string StrRemoveDependency { get; } = Localizer["RemoveDependency"];
    public string StrRestorationTests { get; } = Localizer["RestorationTests"];

    private string StrAbsent { get; } = Localizer["BiaAbsent"];
    private string StrDaysFormat { get; } = Localizer["DurationDaysFormat"];
    private string StrHoursFormat { get; } = Localizer["DurationHoursFormat"];
    private string StrMinutesFormat { get; } = Localizer["DurationMinutesFormat"];
    private string StrDependedOnBy { get; } = Localizer["DependedOnBy"];
    private string StrDeclaredIgnoredFormat { get; } = Localizer["DeclaredCriticalityIgnored"];
    private string StrLastTestOnFormat { get; } = Localizer["LastTestOn"];
    private string StrConflictLineFormat { get; } = Localizer["CascadeConflictLine"];
    private string StrRequirementWithoutObjectiveFormat { get; } = Localizer["RequirementWithoutObjective"];
    private string StrCircularDependencyFormat { get; } = Localizer["CircularDependency"];
    private string StrTightestMtpdFormat { get; } = Localizer["TightestDependentMtpd"];
    private string StrThreatNone { get; } = Localizer["ThreatNone"];
    private string StrThreatConfirmedFormat { get; } = Localizer["ThreatConfirmedWeight"];
    private string StrThreatUnverifiedFormat { get; } = Localizer["ThreatUnverifiedWeight"];
    private string StrThreatItemFormat { get; } = Localizer["ThreatItemLine"];
    private string StrDeleteBiaConfirm { get; } = Localizer["DeleteBiaConfirm"];

    #endregion

    #region SERVICES

    private IContinuityService ContinuityService { get; } = GetService<IContinuityService>();
    private IDialogService DialogService { get; } = GetService<IDialogService>();

    #endregion

    #region PROPERTIES

    private int _entityId;
    private string _entityName = string.Empty;

    private bool _isVisible;
    public bool IsVisible { get => _isVisible; private set => this.RaiseAndSetIfChanged(ref _isVisible, value); }

    private ContinuityProfileDto? _profile;
    public ContinuityProfileDto? Profile
    {
        get => _profile;
        private set
        {
            this.RaiseAndSetIfChanged(ref _profile, value);
            RaiseAll();
        }
    }

    public ObservableCollection<ContinuityDependencyRow> Dependencies { get; } = [];

    private ContinuityDependencyRow? _selectedDependency;
    public ContinuityDependencyRow? SelectedDependency
    {
        get => _selectedDependency;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDependency, value);
            this.RaisePropertyChanged(nameof(IsRemoveDependencyEnabled));
        }
    }

    private string Duration(int? minutes) =>
        ContinuitySummary.Duration(minutes, StrAbsent, StrDaysFormat, StrHoursFormat, StrMinutesFormat);

    public string MtpdText => Duration(Profile?.Bia?.MtpdMinutes);
    public string RtoText => Duration(Profile?.Bia?.RtoMinutes);
    public string RpoText => Duration(Profile?.Bia?.RpoMinutes);

    public string AssessedAtText => Profile?.Bia is { } bia
        ? DateTime.SpecifyKind(bia.AssessedAt, DateTimeKind.Utc).ToLocalTime().ToString("d", CultureInfo.CurrentCulture)
        : StrAbsent;

    public string CriticalityText
    {
        get
        {
            if (Profile is null) return string.Empty;

            string source = Localizer[ContinuitySummary.SourceKey(Profile.Subject.CriticalitySource)];
            var text = Profile.Subject.EffectiveCriticality is { } c ? $"{c} — {source}" : source;

            return Profile.DeclaredCriticalityIgnored && Profile.DeclaredCriticality is { } declared
                ? text + " · " + string.Format(CultureInfo.CurrentCulture, StrDeclaredIgnoredFormat, declared)
                : text;
        }
    }

    public string RtoVerificationText => VerificationText(Profile?.RtoVerification);
    public string RpoVerificationText => VerificationText(Profile?.RpoVerification);

    private string VerificationText(ObjectiveVerificationDto? verification)
    {
        if (verification is null) return string.Empty;

        string text = Localizer[ContinuitySummary.StatusKey(verification.Status)];
        if (verification.Reason is { } reason) text += " — " + Localizer[ContinuitySummary.ReasonKey(reason)];
        if (verification.TestedAt is { } at)
            text += " · " + string.Format(CultureInfo.CurrentCulture, StrLastTestOnFormat,
                DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("d", CultureInfo.CurrentCulture));
        return text;
    }

    /// <summary>One line per conflict, per objective without a value under a requirement, the cycle and
    /// the tightest dependent MTPD — empty when the cascade has nothing to say.</summary>
    public string CascadeText
    {
        get
        {
            if (Profile is null) return string.Empty;
            var cascade = Profile.Cascade;

            var lines = cascade.Conflicts.Select(c => string.Format(CultureInfo.CurrentCulture, StrConflictLineFormat,
                    Localizer[c.Objective == ContinuityObjective.Rto ? "Rto" : "Rpo"], Duration(c.DeclaredMinutes),
                    Duration(c.RequiredMinutes), c.BindingName ?? "#" + c.BindingEntityId))
                .Concat(cascade.ObjectivesWithoutValueUnderRequirement.Select(o =>
                    string.Format(CultureInfo.CurrentCulture, StrRequirementWithoutObjectiveFormat,
                        Localizer[o == ContinuityObjective.Rto ? "Rto" : "Rpo"])))
                .ToList();

            if (cascade.CycleMembers.Count > 0)
            {
                var names = cascade.CycleMembers.Select(id =>
                    cascade.Providers.FirstOrDefault(p => p.EntityId == id)?.Name ?? "#" + id);
                lines.Add(string.Format(CultureInfo.CurrentCulture, StrCircularDependencyFormat, string.Join(", ", names)));
            }

            if (cascade.TightestDependentMtpd is { } tightest)
                lines.Add(string.Format(CultureInfo.CurrentCulture, StrTightestMtpdFormat, Duration(tightest.Minutes),
                    tightest.BindingName ?? "#" + tightest.BindingEntityId));

            return string.Join(Environment.NewLine, lines);
        }
    }

    public bool HasCascadeText => !string.IsNullOrEmpty(CascadeText);

    public string ThreatText => ContinuitySummary.ThreatHeadline(Profile?.Threat, StrThreatNone,
        StrThreatConfirmedFormat, StrThreatUnverifiedFormat);

    public string ThreatItemsText => Profile is null
        ? string.Empty
        : string.Join(Environment.NewLine, Profile.Threat.Items.Select(i => string.Format(CultureInfo.CurrentCulture,
            StrThreatItemFormat, Localizer[ContinuitySummary.ThreatReasonKey(i.Reason)],
            Localizer[i.Objective == ContinuityObjective.Rto ? "Rto" : "Rpo"],
            i.ViaName ?? (i.ViaEntityId is { } via ? "#" + via : "—"))));

    public bool HasThreatItems => Profile?.Threat.Items.Count > 0;

    public bool CanManageBia => Profile?.CallerCanManageBia == true;

    public bool IsDeleteBiaEnabled => CanManageBia && Profile?.Bia is not null;

    public bool IsRemoveDependencyEnabled => CanManageBia && SelectedDependency is { IsOutgoing: true };

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtEditBiaClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteBiaClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddDependencyClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveDependencyClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRestorationTestsClicked { get; }

    #endregion

    public ContinuityPanelViewModel()
    {
        BtEditBiaClicked = ReactiveCommand.CreateFromTask(EditBiaAsync);
        BtDeleteBiaClicked = ReactiveCommand.CreateFromTask(DeleteBiaAsync);
        BtAddDependencyClicked = ReactiveCommand.CreateFromTask(AddDependencyAsync);
        BtRemoveDependencyClicked = ReactiveCommand.CreateFromTask(RemoveDependencyAsync);
        BtRestorationTestsClicked = ReactiveCommand.CreateFromTask(ShowTestsAsync);
    }

    /// <summary>
    /// Shows the block for <paramref name="entityId"/> when it is a process or a service and the user may
    /// read continuity data; hides it otherwise. A read that fails (403, network) hides it too, rather than
    /// showing a block of "Absent" that would read as a fact.
    /// </summary>
    public async Task LoadAsync(int entityId, string? definitionName, string entityName)
    {
        _entityId = entityId;
        _entityName = entityName;

        if (!ContinuitySummary.IsSubject(definitionName)
            || !ContinuityAccess.CanRead(AuthenticationService.AuthenticatedUserInfo))
        {
            IsVisible = false;
            Profile = null;
            return;
        }

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var requested = _entityId;

        try
        {
            var profile = await ContinuityService.GetProfileAsync(requested);

            // A later selection wins over a slow earlier answer.
            if (requested != _entityId) return;

            Dependencies.Clear();
            foreach (var d in profile.DependsOn) Dependencies.Add(new ContinuityDependencyRow(d, true, StrDependsOn));
            foreach (var d in profile.DependedOnBy) Dependencies.Add(new ContinuityDependencyRow(d, false, StrDependedOnBy));
            SelectedDependency = null;

            Profile = profile;
            IsVisible = true;
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not load the continuity profile of entity {Id}: {Message}", requested, ex.Message);
            IsVisible = false;
            Profile = null;
        }
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(MtpdText), nameof(RtoText), nameof(RpoText), nameof(AssessedAtText), nameof(CriticalityText),
                     nameof(RtoVerificationText), nameof(RpoVerificationText), nameof(CascadeText), nameof(HasCascadeText),
                     nameof(ThreatText), nameof(ThreatItemsText), nameof(HasThreatItems), nameof(CanManageBia),
                     nameof(IsDeleteBiaEnabled), nameof(IsRemoveDependencyEnabled)
                 })
            this.RaisePropertyChanged(name);
    }

    private ContinuityDialogParameter Parameter() => new()
    {
        EntityId = _entityId,
        EntityName = _entityName,
        Bia = Profile?.Bia,
        ExistingProviderIds = Profile?.DependsOn.Select(d => d.ProviderEntityId).ToList() ?? [],
        CanRecordTests = Profile?.CallerCanRecordTests == true
    };

    private async Task OpenAsync(string viewModel)
    {
        var result = await DialogService.ShowDialogAsync<ContinuityDialogResult, ContinuityDialogParameter>(
            viewModel, Parameter());

        if (result is { Changed: true }) await ReloadAsync();
    }

    private Task EditBiaAsync() => CanManageBia ? OpenAsync(nameof(EditBiaDialogViewModel)) : Task.CompletedTask;

    private Task AddDependencyAsync() =>
        CanManageBia ? OpenAsync(nameof(AddBiaDependencyDialogViewModel)) : Task.CompletedTask;

    private Task ShowTestsAsync() => OpenAsync(nameof(RestorationTestsDialogViewModel));

    private async Task DeleteBiaAsync()
    {
        if (!IsDeleteBiaEnabled) return;
        if (!await ConfirmationDialog.ConfirmDeleteAsync(_entityName, StrDeleteBiaConfirm)) return;

        await RunAsync(string.Empty, async () =>
        {
            await ContinuityService.DeleteBiaAsync(_entityId);
            await ReloadAsync();
        });
    }

    private async Task RemoveDependencyAsync()
    {
        if (!IsRemoveDependencyEnabled || SelectedDependency is null) return;

        var dependencyId = SelectedDependency.Dependency.Id;

        await RunAsync(string.Empty, async () =>
        {
            await ContinuityService.DeleteDependencyAsync(_entityId, dependencyId);
            await ReloadAsync();
        });
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Model.RiskIndex;
using ReactiveUI;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>
/// The open draft as one object in the GUI (S40 §6.1): its saved state and revision, the edit both the Draft and the Sources
/// and freshness tabs change, the live validation through the server's own rules and the last server validation. One Save,
/// one Discard and one <c>If-Match</c>: two tabs never race each other into a <c>409 stale_draft</c>.
/// </summary>
public class RiskProfileDraftSession : RiskAdminViewModelBase
{
    private readonly IRiskIndexService _readService;
    private readonly IRiskIndexConfigService _configService;

    public RiskProfileDraftSession(RiskIndexAdminRights rights, IRiskIndexService readService, IRiskIndexConfigService configService)
    {
        Rights = rights;
        _readService = readService;
        _configService = configService;
    }

    public RiskIndexAdminRights Rights { get; }

    /// <summary>
    /// Raised after anything a tab shows changed. The argument is true when the values were replaced (a load, a save, a
    /// discard): the tabs then re-read their controls; false for an edit, after which they only refresh what derives from it.
    /// </summary>
    public event Action<bool>? SessionChanged;

    /// <summary>Raised when the list of versions changed on the server (create, delete, submit, approve, reject, retire).</summary>
    public event Action? ProfilesChanged;

    public RiskIndexParameterSchemaDto? Schema { get; private set; }

    public IReadOnlyList<RiskIndexPresetDto> Presets { get; private set; } = [];

    public IReadOnlyList<RiskIndexProfileSummaryDto> Profiles { get; private set; } = [];

    public RiskIndexProfileDto? Active { get; private set; }

    /// <summary>The open profile: a draft, or the one pending approval. Null when neither exists.</summary>
    public RiskIndexProfileDto? Draft { get; private set; }

    public bool HasDraft => Draft is not null;

    /// <summary>
    /// The edit is open to a configurer on a draft, or, with no draft at all, on a copy of the active profile that the first
    /// Save turns into a new draft (the Sources tab's path, S40 §6.5). Never while a version is pending approval.
    /// </summary>
    public bool IsEditable => Rights.Configure &&
                              (Draft is null ? Active is not null : Draft.Status == RiskIndexProfileStatus.Draft);

    private RiskIndexParameters _edited = new();

    public RiskIndexParameters Edited => _edited;

    private string _editedName = "";

    public string EditedName
    {
        get => _editedName;
        set
        {
            if (_editedName == value) return;
            this.RaiseAndSetIfChanged(ref _editedName, value);
            Recompute();
        }
    }

    /// <summary>Keys whose text does not parse (a list item that is not a level id or a CIDR): Save stays closed.</summary>
    public HashSet<string> InvalidFields { get; } = new(StringComparer.Ordinal);

    public bool StructurallyValid => InvalidFields.Count == 0 && (Draft is null || !string.IsNullOrWhiteSpace(EditedName));

    public bool IsDirty => Draft is not null
        ? !string.Equals(EditedName, Draft.Name, StringComparison.Ordinal) ||
          RiskIndexProfileRules.ChangedKeys(Draft.Parameters, _edited).Count > 0
        : Active is not null && RiskIndexProfileRules.ChangedKeys(Active.Parameters, _edited).Count > 0;

    public string UnsavedChangesSummary => string.Join(", ", ProfileParameterRows.ChangedGroups(
            Draft?.Parameters ?? Active?.Parameters ?? _edited, _edited)
        .Select(g => Localizer[ProfileParameterRows.GroupLabelKey(g)].Value));

    private RiskIndexValidationResultDto? _liveValidation;

    /// <summary>The server's rules run on the edit, live (S40 §6.2.1); the server's own answer replaces it on Validate.</summary>
    public RiskIndexValidationResultDto? LiveValidation
    {
        get => _liveValidation;
        private set => this.RaiseAndSetIfChanged(ref _liveValidation, value);
    }

    private RiskIndexValidationResultDto? _serverValidation;

    /// <summary>The last <c>POST …/Validate</c> of the saved draft; cleared by any edit or save.</summary>
    public RiskIndexValidationResultDto? ServerValidation
    {
        get => _serverValidation;
        private set => this.RaiseAndSetIfChanged(ref _serverValidation, value);
    }

    private string? _loadError;

    public string? LoadError
    {
        get => _loadError;
        private set => this.RaiseAndSetIfChanged(ref _loadError, value);
    }

    // ------------------------------------------------------------------------------------------------------------------

    public async Task LoadAsync()
    {
        await WithBusyAsync(async () =>
        {
            try
            {
                Schema ??= await _configService.GetSchemaAsync();
                if (Rights.Configure && Presets.Count == 0) Presets = await _configService.GetPresetsAsync();
                Profiles = await _readService.GetProfilesAsync();
                Active = await _configService.GetActiveAsync();
                var open = ProfileWorkflowGates.OpenDraft(Profiles);
                Draft = open is null ? null : await _readService.GetProfileAsync(open.Id);
                ResetEdit();
                LoadError = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading the risk index profiles");
                LoadError = RiskAdminText.Explain(ex);
            }
        });
        SessionChanged?.Invoke(true);
    }

    /// <summary>Applies an edit of the parameters (a row, a band, a source) to the session.</summary>
    public void Edit(Func<RiskIndexParameters, RiskIndexParameters> change)
    {
        if (!IsEditable) return;
        _edited = change(_edited);
        Recompute();
    }

    public void MarkField(string key, bool valid)
    {
        if (valid) InvalidFields.Remove(key);
        else InvalidFields.Add(key);
        Recompute();
    }

    public void Discard()
    {
        ResetEdit();
        SessionChanged?.Invoke(true);
    }

    private void ResetEdit()
    {
        _edited = Draft?.Parameters ?? Active?.Parameters ?? new RiskIndexParameters();
        _editedName = Draft?.Name ?? "";
        this.RaisePropertyChanged(nameof(EditedName));
        InvalidFields.Clear();
        ServerValidation = null;
        LiveValidation = Draft is null && Active is null ? null : Validate(_edited);
    }

    private void Recompute()
    {
        ServerValidation = null;
        LiveValidation = Draft is null && Active is null ? null : Validate(_edited);
        this.RaisePropertyChanged(nameof(IsDirty));
        SessionChanged?.Invoke(false);
    }

    private RiskIndexValidationResultDto Validate(RiskIndexParameters parameters) =>
        RiskIndexProfileRules.Validate(RiskIndexMode.Interim, parameters, Active?.Mode, Active?.Parameters, null, Draft?.Preset,
            Active?.Preset);

    /// <summary>
    /// Saves the edit with <c>If-Match</c>. Without a draft (the Sources tab's case, S40 §6.5) it first creates one from the
    /// active profile and then writes the edit onto it. True when the server accepted it.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        var saved = false;
        await RunRiskAsync(Localizer["RiskAdminDraftSavedMSG"], async () =>
        {
            var draft = Draft;
            if (draft is null)
            {
                if (Active is null) throw new InvalidOperationException("No active profile to draft from.");
                draft = await _configService.CreateDraftAsync(new RiskIndexProfileCreateRequest(
                    string.IsNullOrWhiteSpace(EditedName) ? Active.Name : EditedName, Active.Id, null));
                ProfilesChanged?.Invoke();
            }

            var name = string.IsNullOrWhiteSpace(EditedName) ? draft.Name : EditedName.Trim();
            Draft = await _configService.UpdateDraftAsync(draft.Id, draft.Revision,
                new RiskIndexProfileUpdateRequest(name, RiskIndexMode.Interim, _edited));
            Profiles = await _readService.GetProfilesAsync();
            ResetEdit();
            saved = true;
        });
        SessionChanged?.Invoke(true);
        return saved;
    }

    public async Task ValidateOnServerAsync()
    {
        if (Draft is null) return;
        await RunRiskAsync("", async () => ServerValidation = await _configService.ValidateAsync(Draft.Id));
        SessionChanged?.Invoke(false);
    }

    /// <summary>Re-reads everything after a lifecycle step another tab took (submit, approve, reject, retire, delete).</summary>
    public async Task RefreshAfterLifecycleAsync()
    {
        await LoadAsync();
        ProfilesChanged?.Invoke();
    }

    public void ReplaceDraft(RiskIndexProfileDto? draft)
    {
        Draft = draft;
        ResetEdit();
        SessionChanged?.Invoke(true);
    }
}

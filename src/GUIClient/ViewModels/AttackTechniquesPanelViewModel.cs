using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using Model.ExploitationSignals;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>What an <see cref="AttackTechniquesPanelViewModel"/> edits the techniques of.</summary>
public enum AttackTechniqueTarget
{
    /// <summary>A finding — writes need <c>vulnerabilities_create</c>.</summary>
    Finding = 1,

    /// <summary>A risk scenario — reads and writes need <c>RequireRiskmanagement</c>.</summary>
    Risk = 2
}

/// <summary>One linked technique as the editor lists it.</summary>
public sealed class AttackTechniqueRow(AttackTechniqueDto technique, string linkedOnFormat)
{
    public AttackTechniqueDto Technique { get; } = technique;

    public string Text { get; } = ExploitationSignalsSummary.TechniqueText(technique);

    public string LinkedOnText { get; } = string.Format(CultureInfo.CurrentCulture, linkedOnFormat,
        ExploitationSignalsSummary.Instant(technique.CreatedAt, string.Empty));
}

/// <summary>
/// The MITRE ATT&amp;CK technique editor (Stage 9.4, T302, S45 §7.3), one for both targets: on a finding it
/// sits inside the exploitation-signals panel; on a risk it is the "Scenario ATT&amp;CK techniques" block of
/// the risk detail. Lists the linked techniques, links one by identifier (validated by shape, as the
/// server does) with an optional name, and unlinks the selected one after a confirmation.
///
/// Writes are enabled by <see cref="ExploitationSignalsAccess"/> — that is not the control: the server
/// decides, and its refusal (400, 403, 409) is shown as the sentence it wrote, through
/// <see cref="ViewModelBase.RunAsync"/>. Techniques do not change the priority tier (S45 D7), so a write
/// reloads only this list.
/// </summary>
public class AttackTechniquesPanelViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; }
    public string StrTechniqueId { get; } = Localizer["AttackTechniqueId"];
    public string StrTechniqueName { get; } = Localizer["AttackTechniqueName"];
    public string StrAdd { get; } = Localizer["AddAttackTechnique"];
    public string StrRemove { get; } = Localizer["RemoveAttackTechnique"];
    public string StrNone { get; } = Localizer["AttackTechniquesNone"];
    public string StrReadOnly { get; } = Localizer["AttackTechniquesReadOnly"];

    private string StrLinkedOnFormat { get; } = Localizer["AttackTechniqueLinkedOn"];
    private string StrLinked { get; } = Localizer["AttackTechniqueLinked"];
    private string StrUnlinked { get; } = Localizer["AttackTechniqueUnlinked"];

    #endregion

    private IExploitationSignalsService SignalsService { get; } = GetService<IExploitationSignalsService>();

    public AttackTechniqueTarget Target { get; }

    #region PROPERTIES

    private int? _targetId;

    private bool _isVisible;
    public bool IsVisible { get => _isVisible; private set => this.RaiseAndSetIfChanged(ref _isVisible, value); }

    public ObservableCollection<AttackTechniqueRow> Items { get; } = [];

    private AttackTechniqueRow? _selectedItem;
    public AttackTechniqueRow? SelectedItem
    {
        get => _selectedItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedItem, value);
            this.RaisePropertyChanged(nameof(IsRemoveEnabled));
        }
    }

    private string? _newTechniqueId;
    public string? NewTechniqueId
    {
        get => _newTechniqueId;
        set
        {
            this.RaiseAndSetIfChanged(ref _newTechniqueId, value);
            RaiseInput();
        }
    }

    private string? _newTechniqueName;
    public string? NewTechniqueName
    {
        get => _newTechniqueName;
        set
        {
            this.RaiseAndSetIfChanged(ref _newTechniqueName, value);
            RaiseInput();
        }
    }

    private bool _canWrite;

    /// <summary>The user is in the audience of the target's write policy (for enabling only).</summary>
    public bool CanWrite
    {
        get => _canWrite;
        private set
        {
            this.RaiseAndSetIfChanged(ref _canWrite, value);
            this.RaisePropertyChanged(nameof(IsReadOnly));
            RaiseInput();
            this.RaisePropertyChanged(nameof(IsRemoveEnabled));
        }
    }

    public bool IsReadOnly => !CanWrite;

    public bool IsEmpty => Items.Count == 0;

    private IEnumerable<string> Linked => Items.Select(i => i.Technique.TechniqueId);

    private string? InputErrorKey => AttackTechniqueInput.ErrorKey(NewTechniqueId, NewTechniqueName, Linked);

    /// <summary>Why the typed technique cannot be linked, in words — empty when it can or nothing is typed.</summary>
    public string InputErrorText => InputErrorKey is { } key ? Localizer[key] : string.Empty;

    public bool HasInputError => CanWrite && InputErrorKey is not null;

    public bool IsAddEnabled => CanWrite && _targetId is not null
                                         && AttackTechniqueInput.CanSubmit(NewTechniqueId, NewTechniqueName, Linked);

    public bool IsRemoveEnabled => CanWrite && _targetId is not null && SelectedItem is not null;

    #endregion

    public ReactiveCommand<RxVoid, RxVoid> BtAddTechniqueClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveTechniqueClicked { get; }

    public AttackTechniquesPanelViewModel(AttackTechniqueTarget target)
    {
        Target = target;
        // Two literal lookups rather than one computed key, so LocalizationCoverageTest sees both.
        StrTitle = target == AttackTechniqueTarget.Risk
            ? Localizer["ScenarioAttackTechniques"]
            : Localizer["AttackTechniques"];

        BtAddTechniqueClicked = ReactiveCommand.CreateFromTask(AddAsync);
        BtRemoveTechniqueClicked = ReactiveCommand.CreateFromTask(RemoveAsync);

        Items.CollectionChanged += (_, _) =>
        {
            this.RaisePropertyChanged(nameof(IsEmpty));
            RaiseInput();
        };
    }

    /// <summary>Design-time and XAML previewer constructor.</summary>
    public AttackTechniquesPanelViewModel() : this(AttackTechniqueTarget.Finding)
    {
    }

    /// <summary>
    /// Shows the techniques a finding's signals already carried — the panel of S45 §7.2 reads them in its
    /// own call, so the finding side costs no second request.
    /// </summary>
    public void Show(int findingId, IEnumerable<AttackTechniqueDto> techniques)
    {
        RetargetTo(findingId);
        CanWrite = ExploitationSignalsAccess.CanWriteFindingTechniques(AuthenticationService.AuthenticatedUserInfo);
        Fill(techniques);
        IsVisible = true;
    }

    /// <summary>
    /// Reads the target's techniques. A risk is read only by the <c>RequireRiskmanagement</c> audience; for
    /// anyone else, and on any failed read, the block is hidden rather than claiming "no technique".
    /// </summary>
    public async Task LoadAsync(int? targetId)
    {
        RetargetTo(targetId);
        var user = AuthenticationService.AuthenticatedUserInfo;

        if (targetId is not { } id
            || (Target == AttackTechniqueTarget.Risk && !ExploitationSignalsAccess.CanReadRiskTechniques(user)))
        {
            Clear();
            return;
        }

        CanWrite = Target == AttackTechniqueTarget.Risk
            ? ExploitationSignalsAccess.CanWriteRiskTechniques(user)
            : ExploitationSignalsAccess.CanWriteFindingTechniques(user);

        try
        {
            var techniques = await ReadAsync(id);

            // A later selection wins over a slow earlier answer.
            if (_targetId != id) return;

            Fill(techniques);
            IsVisible = true;
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not load the ATT&CK techniques of {Target} {Id}: {Message}", Target, id, ex.Message);
            if (_targetId == id) Clear();
        }
    }

    /// <summary>
    /// Points the editor at another record. What was typed for the previous one is dropped: left in place, a
    /// technique typed on one finding would be linked to the next one selected.
    /// </summary>
    private void RetargetTo(int? targetId)
    {
        if (_targetId != targetId)
        {
            NewTechniqueId = null;
            NewTechniqueName = null;
        }

        _targetId = targetId;
    }

    /// <summary>Hides the block and forgets the target.</summary>
    public void Clear()
    {
        _targetId = null;
        Items.Clear();
        SelectedItem = null;
        NewTechniqueId = null;
        NewTechniqueName = null;
        IsVisible = false;
    }

    private Task<List<AttackTechniqueDto>> ReadAsync(int id) => Target == AttackTechniqueTarget.Risk
        ? SignalsService.GetRiskTechniquesAsync(id)
        : SignalsService.GetVulnerabilityTechniquesAsync(id);

    private void Fill(IEnumerable<AttackTechniqueDto> techniques)
    {
        Items.Clear();
        foreach (var technique in techniques.OrderBy(t => t.TechniqueId, StringComparer.Ordinal))
            Items.Add(new AttackTechniqueRow(technique, StrLinkedOnFormat));
        SelectedItem = null;
    }

    private async Task ReloadAsync(int id)
    {
        var techniques = await ReadAsync(id);
        if (_targetId == id) Fill(techniques);
    }

    private async Task AddAsync()
    {
        if (!IsAddEnabled || _targetId is not { } id) return;
        if (AttackTechniqueInput.ToRequest(NewTechniqueId, NewTechniqueName) is not { } request) return;

        await RunAsync(StrLinked, async () =>
        {
            if (Target == AttackTechniqueTarget.Risk) await SignalsService.AddRiskTechniqueAsync(id, request);
            else await SignalsService.AddVulnerabilityTechniqueAsync(id, request);

            NewTechniqueId = null;
            NewTechniqueName = null;
            await ReloadAsync(id);
        });
    }

    private async Task RemoveAsync()
    {
        if (!IsRemoveEnabled || _targetId is not { } id || SelectedItem is not { } row) return;
        if (!await ConfirmationDialog.ConfirmDeleteAsync(row.Text)) return;

        var associationId = row.Technique.Id;

        await RunAsync(StrUnlinked, async () =>
        {
            if (Target == AttackTechniqueTarget.Risk) await SignalsService.DeleteRiskTechniqueAsync(id, associationId);
            else await SignalsService.DeleteVulnerabilityTechniqueAsync(id, associationId);

            await ReloadAsync(id);
        });
    }

    private void RaiseInput()
    {
        this.RaisePropertyChanged(nameof(InputErrorText));
        this.RaisePropertyChanged(nameof(HasInputError));
        this.RaisePropertyChanged(nameof(IsAddEnabled));
    }
}

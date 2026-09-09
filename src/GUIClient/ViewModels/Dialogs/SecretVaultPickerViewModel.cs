using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Secrets;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Dialogs;

/// <summary>
/// The picker an operator opens from the button beside a credential field: choose a vault, choose a
/// secret, choose a field of it, and get back a reference.
///
/// Nothing here ever holds a secret value. The API it talks to has no endpoint that returns one — the
/// list is names, paths and field names — so the worst a compromised desktop session can do with this
/// screen is learn what the estate's secrets are called and re-point a NetRisk field at one of them.
/// Re-pointing is a real capability, which is why the whole surface is behind the
/// <c>configuration</c> permission; reading is not a capability it has at all.
/// </summary>
public class SecretVaultPickerViewModel
    : ParameterizedDialogViewModelBaseAsync<SecretVaultPickerResult, SecretVaultPickerParameter>
{
    #region LANGUAGE

    public string StrTitle => Localizer["SelectVaultSecret"];
    public string StrVault { get; } = Localizer["SecretVault"];
    public string StrSecret { get; } = Localizer["Secret"];
    public string StrField { get; } = Localizer["Field"];
    public string StrFilter { get; } = Localizer["Filter"];
    public string StrSelect { get; } = Localizer["Select"];
    public new string StrCancel { get; } = Localizer["Cancel"];
    public string StrNoVaultsMsg { get; } = Localizer["NoSecretVaultsConfiguredMSG"];
    public string StrSingleValueField { get; } = Localizer["WholeSecretValue"];

    #endregion

    #region PROPERTIES

    private readonly IIntegrationsService _integrations;

    /// <summary>The field this picker was opened for, shown in the header.</summary>
    private string _fieldName = string.Empty;
    public string FieldName
    {
        get => _fieldName;
        private set => this.RaiseAndSetIfChanged(ref _fieldName, value);
    }

    public ObservableCollection<SecretVaultConnectionView> Connections { get; } = [];

    /// <summary>The secrets matching <see cref="Filter"/>. Bound to the list.</summary>
    public ObservableCollection<VaultSecretSummary> Secrets { get; } = [];

    /// <summary>
    /// The field names of the selected secret, plus a leading entry meaning "the secret's single
    /// value". The leading entry is what makes a one-field and a no-field secret behave the same in
    /// the UI, rather than the combo box being empty and disabled for half of them.
    /// </summary>
    public ObservableCollection<string> Fields { get; } = [];

    private SecretVaultConnectionView? _selectedConnection;
    public SecretVaultConnectionView? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedConnection, value);
            if (value != null) _ = LoadSecretsAsync(value.Id);
        }
    }

    private VaultSecretSummary? _selectedSecret;
    public VaultSecretSummary? SelectedSecret
    {
        get => _selectedSecret;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSecret, value);
            LoadFields(value);
            this.RaisePropertyChanged(nameof(SelectEnabled));
        }
    }

    private string? _selectedField;
    public string? SelectedField
    {
        get => _selectedField;
        set => this.RaiseAndSetIfChanged(ref _selectedField, value);
    }

    private string _filter = string.Empty;
    public string Filter
    {
        get => _filter;
        set
        {
            this.RaiseAndSetIfChanged(ref _filter, value);
            ApplyFilter();
        }
    }

    private string _status = string.Empty;

    /// <summary>
    /// What went wrong, or what is happening. Shown in the dialog rather than as a toast: a toast
    /// behind a modal window is a message nobody reads.
    /// </summary>
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        private set => this.RaiseAndSetIfChanged(ref _busy, value);
    }

    public bool SelectEnabled => SelectedSecret != null;

    /// <summary>Everything the connection can see, before <see cref="Filter"/> narrows it.</summary>
    private VaultSecretSummary[] _allSecrets = [];

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtSelectClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    #endregion

    public SecretVaultPickerViewModel() : this(GetService<IIntegrationsService>())
    {
    }

    /// <summary>Test seam, and the constructor the container uses once the service is registered.</summary>
    public SecretVaultPickerViewModel(IIntegrationsService integrations)
    {
        _integrations = integrations;

        BtSelectClicked = ReactiveCommand.Create(ExecuteSelect);
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);
    }

    public override async Task ActivateAsync(SecretVaultPickerParameter parameter,
        CancellationToken cancellationToken = default)
    {
        FieldName = parameter.FieldName ?? string.Empty;

        await LoadConnectionsAsync(parameter.CurrentReference);
    }

    private async Task LoadConnectionsAsync(string? currentReference)
    {
        Busy = true;

        try
        {
            var connections = await _integrations.GetSecretVaultConnectionsAsync(includeDisabled: false);

            Connections.Clear();

            // A connection whose plugin is missing cannot list anything, so offering it would produce
            // an empty list and a puzzled operator. The message below names the situation instead.
            foreach (var connection in connections.Where(c => c.PluginAvailable))
                Connections.Add(connection);

            if (Connections.Count == 0)
            {
                Status = StrNoVaultsMsg;
                return;
            }

            // Pre-select the connection the field is already bound to, so re-picking a field starts
            // where the operator left it rather than on whichever vault sorts first.
            SecretVaultConnectionView? preferred = null;

            if (SecretReference.TryParse(currentReference, out var reference))
                preferred = Connections.FirstOrDefault(c => c.Id == reference.ConnectionId);

            SelectedConnection = preferred ?? Connections[0];
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the vault connections: {Message}", ex.Message);
            Status = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LoadSecretsAsync(int connectionId)
    {
        Busy = true;
        Status = string.Empty;

        try
        {
            _allSecrets = (await _integrations.GetVaultSecretsAsync(connectionId)).ToArray();

            ApplyFilter();

            if (_allSecrets.Length == 0)
                Status = Localizer["VaultHasNoVisibleSecretsMSG"];
        }
        catch (Exception ex)
        {
            Logger.Error("Could not list the secrets of vault connection {Id}: {Message}",
                connectionId, ex.Message);

            _allSecrets = [];
            ApplyFilter();
            Status = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private void ApplyFilter()
    {
        var needle = Filter.Trim();

        var matches = string.IsNullOrEmpty(needle)
            ? _allSecrets
            : _allSecrets.Where(s =>
                s.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || s.Id.Contains(needle, StringComparison.OrdinalIgnoreCase)
                || (s.Description ?? string.Empty).Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        Secrets.Clear();
        foreach (var secret in matches) Secrets.Add(secret);

        // Clearing the collection drops the list's selection, and a stale SelectedSecret pointing at a
        // row that is no longer shown would let Select return a secret the operator cannot see.
        if (SelectedSecret != null && !matches.Contains(SelectedSecret)) SelectedSecret = null;
    }

    private void LoadFields(VaultSecretSummary? secret)
    {
        Fields.Clear();

        // Always present, always first: a secret with no named fields still needs a selectable
        // "the whole value" entry, or the combo box is empty and the operator wonders what is missing.
        Fields.Add(StrSingleValueField);

        if (secret != null)
            foreach (var field in secret.Fields)
                Fields.Add(field);

        SelectedField = StrSingleValueField;
    }

    private void ExecuteSelect()
    {
        if (SelectedConnection == null || SelectedSecret == null) return;

        // The sentinel is a display string, so it is compared by reference-equality of intent rather
        // than translated back: anything that is not a real field name means "the whole value".
        var field = string.Equals(SelectedField, StrSingleValueField, StringComparison.Ordinal)
            ? null
            : SelectedField;

        var reference = SecretReference.Create(SelectedConnection.Id, SelectedSecret.Id, field);

        Close(new SecretVaultPickerResult
        {
            Action = ResultActions.Ok,
            Reference = reference.ToString(),
            DisplayName = SelectedConnection.Name + ": " + SelectedSecret.DisplayName
                          + (field == null ? string.Empty : " / " + field)
        });
    }

    private void ExecuteCancel() => Close(new SecretVaultPickerResult { Action = ResultActions.Cancel });
}

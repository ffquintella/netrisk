using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using ClientServices.Interfaces;
using GUIClient.Tools;
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
/// Nothing here ever holds a secret value. The API it talks to has no endpoint that returns one — a
/// listing is names and paths — so the worst a compromised desktop session can do with this screen is
/// learn what the estate's secrets are called and re-point a NetRisk field at one of them.
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
    public string StrFieldHint { get; } = Localizer["VaultSecretFieldHintMSG"];
    public string StrPluginOptionsFailed { get; } = Localizer["VaultPluginOptionsFailedMSG"];

    #endregion

    #region PROPERTIES

    private readonly IIntegrationsService _integrations;

    private string _targetFieldCaption = string.Empty;

    /// <summary>
    /// The NetRisk field this picker was opened for ("Vision One — API key"), shown in the header.
    ///
    /// Distinct from <see cref="FieldName"/>, which is the field *inside the vault secret*. The two
    /// were briefly both called FieldName; naming them apart is what stops the header from being
    /// bound to the input.
    /// </summary>
    public string TargetFieldCaption
    {
        get => _targetFieldCaption;
        private set => this.RaiseAndSetIfChanged(ref _targetFieldCaption, value);
    }

    public ObservableCollection<SecretVaultConnectionView> Connections { get; } = [];

    /// <summary>The secrets matching <see cref="Filter"/>. Bound to the list.</summary>
    public ObservableCollection<VaultSecretSummary> Secrets { get; } = [];

    /// <summary>
    /// Field-name suggestions for the selected secret, when the vault supplies any.
    ///
    /// Often empty, and that is a property of the vault rather than a gap here: a BastionVault
    /// listing returns names only, and the sole way to learn a secret's field names is to read the
    /// secret — which would write an access record in the vault's audit log for every click in this
    /// picker. So the field is typed, with these as suggestions when they exist, and a wrong field is
    /// reported at resolution time by a message naming the fields that do exist.
    /// </summary>
    public ObservableCollection<string> Fields { get; } = [];

    /// <summary>
    /// The controls the connection's plugin contributed to this dialog, in the order it declared
    /// them. Empty for a plugin that declares none, which renders the picker exactly as it was
    /// before plugins could contribute anything.
    ///
    /// This is what replaced a plugin encoding its own concepts inside the secret id: an
    /// environment is now a combo the plugin declared and the host drew, and its value travels in
    /// the reference's own options rather than inside a string the host treats as opaque.
    /// </summary>
    public ObservableCollection<PluginFieldState> PluginFields { get; } = [];

    private bool _hasPluginFields;

    public bool HasPluginFields
    {
        get => _hasPluginFields;
        private set => this.RaiseAndSetIfChanged(ref _hasPluginFields, value);
    }

    /// <summary>
    /// The installed vault plugins, by plugin name. Fetched once when the dialog opens: the
    /// declaration is per plugin, and a connection change is a lookup rather than a round trip.
    /// </summary>
    private Dictionary<string, SecretVaultPluginInfo> _plugins = new(StringComparer.Ordinal);

    /// <summary>The values a previously stored reference carried, to pre-fill the declared controls.</summary>
    private IReadOnlyDictionary<string, string> _initialOptions = new Dictionary<string, string>();

    /// <summary>The reference the field was bound to when the dialog opened, or null.</summary>
    private string? _currentReference;

    private SecretVaultConnectionView? _selectedConnection;
    public SecretVaultConnectionView? SelectedConnection
    {
        get => _selectedConnection;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedConnection, value);

            if (value != null)
            {
                BuildPluginFields(value);
                _ = LoadSecretsAsync(value.Id);
            }
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

            // A field whose options depend on the selection has to ask again: the environments a
            // secret can be read in are the ones its mount declares, not the union of every mount
            // the credential can see.
            _ = LoadPluginOptionsAsync(dependentOnSecretOnly: true);
        }
    }

    private string _fieldName = string.Empty;

    /// <summary>
    /// The field of the secret to bind, or empty for the whole value.
    ///
    /// Free text rather than a selection, because the suggestions may be empty. Empty means "the
    /// secret's single value", which is also what a one-field secret resolves to — so the common case
    /// needs no input at all.
    /// </summary>
    public string FieldName
    {
        get => _fieldName;
        set => this.RaiseAndSetIfChanged(ref _fieldName, value ?? string.Empty);
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
        TargetFieldCaption = parameter.FieldName ?? string.Empty;

        // Kept so the declared controls open on what the field is already bound to, the same way
        // the connection combo does. Re-picking a field should start where the operator left it.
        _currentReference = parameter.CurrentReference;

        if (SecretReference.TryParse(parameter.CurrentReference, out var current))
            _initialOptions = current.Options;

        await LoadPluginsAsync();

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

    private async Task LoadPluginsAsync()
    {
        try
        {
            var plugins = await _integrations.GetSecretVaultPluginsAsync();

            _plugins = plugins
                .GroupBy(p => p.PluginName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            // Not fatal. The picker's own controls are the host's; the plugin's are an addition, and
            // a dialog that cannot list plugins is still a dialog that can pick a secret.
            Logger.Error("Could not load the vault plugin declarations: {Message}", ex.Message);
            _plugins = new Dictionary<string, SecretVaultPluginInfo>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Rebuilds the declared controls for a connection, pre-filled from the reference the field is
    /// already bound to, and fetches the options that do not depend on the selected secret.
    /// </summary>
    private void BuildPluginFields(SecretVaultConnectionView connection)
    {
        PluginFields.Clear();

        var declared = _plugins.TryGetValue(connection.PluginName, out var plugin)
            ? plugin.SecretSelectorFields
            : [];

        // The stored values belong to the connection they were picked against. Carrying them onto a
        // different vault would pre-fill an environment that vault has never heard of.
        var current = SelectedConnectionMatchesStoredReference(connection)
            ? _initialOptions
            : null;

        foreach (var field in PluginFieldState.Build(declared, current)) PluginFields.Add(field);

        HasPluginFields = PluginFields.Count > 0;

        _ = LoadPluginOptionsAsync(dependentOnSecretOnly: false);
    }

    private bool SelectedConnectionMatchesStoredReference(SecretVaultConnectionView connection) =>
        _initialOptions.Count > 0
        && SecretReference.TryParse(_currentReference, out var stored)
        && stored.ConnectionId == connection.Id;

    /// <summary>
    /// Fetches the options for the declared choice fields.
    ///
    /// <paramref name="dependentOnSecretOnly"/> narrows it to the fields that said their list
    /// depends on the selection, which is the set worth re-asking for on every click in the grid.
    /// The rest are asked once per connection.
    /// </summary>
    private async Task LoadPluginOptionsAsync(bool dependentOnSecretOnly)
    {
        var connection = SelectedConnection;
        if (connection == null) return;

        var fields = PluginFields
            .Where(f => f.IsClosedChoice || f.IsOpenChoice)
            .Where(f => !dependentOnSecretOnly || f.OptionsDependOnSecret)
            .ToArray();

        if (fields.Length == 0) return;

        foreach (var field in fields)
        {
            try
            {
                var options = await _integrations.GetVaultFieldOptionsAsync(connection.Id,
                    new VaultFieldOptionsRequest
                    {
                        Screen = VaultScreen.VaultSecretSelector,
                        FieldKey = field.Key,
                        SecretId = SelectedSecret?.Id,
                        Values = PluginFieldState.Values(PluginFields)
                    });

                field.Options = options;
            }
            catch (Exception ex)
            {
                // An empty list, and the reason in the status line. The operator can still pick the
                // secret; what they cannot do is choose a value the vault would not name, and the
                // plugin refuses that on the read path with a message that says which vault.
                Logger.Error("Could not list the values of vault field {Field}: {Message}",
                    field.Key, ex.Message);

                field.Options = [];
                Status = StrPluginOptionsFailed;
            }
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

        if (secret != null)
            foreach (var field in secret.Fields)
                Fields.Add(field);

        // Cleared on every selection change: a field name left over from the previously selected
        // secret is the one mistake this dialog can make that nothing downstream would catch until a
        // third party rejected the credential.
        FieldName = string.Empty;
    }

    private void ExecuteSelect()
    {
        if (SelectedConnection == null || SelectedSecret == null) return;

        // Checked here, and again on the server when the value is used, because the dialog is not
        // the only way a credential column gets written. What this catches is the case the whole
        // feature exists for: a vault that refuses every read which does not name an environment,
        // told to the operator while they are choosing rather than at 3am by a sync job.
        var invalid = PluginFields.Count(field => !field.Validate(key => Localizer[key]));

        if (invalid > 0)
        {
            Status = PluginFields.FirstOrDefault(f => f.HasError)?.Error ?? string.Empty;
            return;
        }

        var field = string.IsNullOrWhiteSpace(FieldName) ? null : FieldName.Trim();

        var options = PluginFieldState.Values(PluginFields);

        var reference = SecretReference.Create(SelectedConnection.Id, SelectedSecret.Id, field, options);

        var declared = options.Count == 0
            ? string.Empty
            : " (" + string.Join(", ", options.OrderBy(o => o.Key, StringComparer.Ordinal)
                .Select(o => o.Key + "=" + o.Value)) + ")";

        Close(new SecretVaultPickerResult
        {
            Action = ResultActions.Ok,
            Reference = reference.ToString(),
            DisplayName = SelectedConnection.Name + ": " + SelectedSecret.DisplayName
                          + (field == null ? string.Empty : " / " + field) + declared
        });
    }

    private void ExecuteCancel() => Close(new SecretVaultPickerResult { Action = ResultActions.Cancel });
}

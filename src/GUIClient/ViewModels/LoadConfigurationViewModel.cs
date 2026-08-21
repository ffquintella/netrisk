using System;
using System.Reactive.Linq;
using ReactiveUI;
using GUIClient.Validation;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// First-run window that asks for the server URL before anything else exists.
/// Validation is enforced here (IX-4) rather than reported after the fact by a
/// message box, and every string is localized (ui-standard §3.2).
/// </summary>
public class LoadConfigurationViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["FirstRunConfiguration"];
    public string StrWelcome { get; } = Localizer["WelcomeToNetRiskMSG"];
    public string StrEnterServerUrl { get; } = Localizer["EnterServerUrlMSG"];
    public string StrUrlInvalid { get; } = Localizer["ServerUrlInvalidMSG"];

    #endregion

    #region PROPERTIES

    private string _serverUrl = string.Empty;

    public string ServerUrl
    {
        get => _serverUrl;
        set => this.RaiseAndSetIfChanged(ref _serverUrl, value);
    }

    private bool _saveEnabled;

    public bool SaveEnabled
    {
        get => _saveEnabled;
        set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
    }

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    #endregion

    #region EVENTS

    /// <summary>Raised with the accepted URL, or <c>null</c> when the user cancelled.</summary>
    public event EventHandler<string?> Completed = delegate { };

    #endregion

    #region CONSTRUCTOR

    public LoadConfigurationViewModel()
    {
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);

        this.ValidationRule(
            viewModel => viewModel.ServerUrl,
            url => IsWellFormedServerUrl(url),
            StrUrlInvalid);

        this.IsValid().Subscribe(valid => SaveEnabled = valid);
    }

    #endregion

    #region METHODS

    /// <summary>
    /// Accepts only an absolute http/https URL. This is the same check the Save button is
    /// gated on, so the button state and the commit path cannot disagree.
    /// </summary>
    public static bool IsWellFormedServerUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private void ExecuteSave()
    {
        if (!SaveEnabled) return;

        Completed.Invoke(this, ServerUrl.Trim());
    }

    private void ExecuteCancel() => Completed.Invoke(this, null);

    #endregion
}

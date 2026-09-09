using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Admin;

/// <summary>
/// CI API tokens (Track 3 milestone 3.5.1): issue a scoped token, see what is outstanding, revoke.
///
/// Its own administration section rather than a fourth tab of the findings-admin screen. Everything
/// else in that screen tunes how scan results are processed — deduplication heuristics, the SLA
/// clock, risk acceptances — and is read by whoever administers the scanners. This is credential
/// issuance: a different act, a different audience, and the only screen in the application that
/// shows a secret. It was reached by pressing an icon whose hint began "Deduplication", which is not
/// where anyone looks for a pipeline credential.
/// </summary>
public class ApiTokensViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrApiTokens { get; } = Localizer["ApiTokens"];
    public string StrName { get; } = Localizer["Name"];
    public string StrScopes { get; } = Localizer["Scopes"];
    public string StrExpiresAt { get; } = Localizer["ExpiresAt"];
    public string StrLastUsed { get; } = Localizer["LastUsed"];
    public string StrIssueToken { get; } = Localizer["IssueToken"];
    public string StrRevoke { get; } = Localizer["Revoke"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrTokenShownOnce { get; } = Localizer["TokenShownOnceMSG"];
    public string StrNoTokens { get; } = Localizer["NoApiTokensMSG"];

    #endregion

    #region SERVICES

    private IFindingsAdminService AdminService { get; } = GetService<IFindingsAdminService>();

    #endregion

    #region PROPERTIES

    public ObservableCollection<ApiTokenSummary> ApiTokens { get; } = new();

    public ObservableCollection<SelectableOption> ScopeOptions { get; } = new();

    private ApiTokenSummary? _selectedToken;
    public ApiTokenSummary? SelectedToken
    {
        get => _selectedToken;
        set => this.RaiseAndSetIfChanged(ref _selectedToken, value);
    }

    private string _newTokenName = "";
    public string NewTokenName
    {
        get => _newTokenName;
        set => this.RaiseAndSetIfChanged(ref _newTokenName, value);
    }

    private DateTimeOffset? _newTokenExpiry = DateTimeOffset.UtcNow.AddDays(90);
    public DateTimeOffset? NewTokenExpiry
    {
        get => _newTokenExpiry;
        set => this.RaiseAndSetIfChanged(ref _newTokenExpiry, value);
    }

    private string _issuedSecret = "";

    /// <summary>
    /// The freshly issued token. Held only in this field, only until the view is left: the server
    /// stores a hash and cannot produce it again, which is the point.
    /// </summary>
    public string IssuedSecret
    {
        get => _issuedSecret;
        set
        {
            this.RaiseAndSetIfChanged(ref _issuedSecret, value);
            this.RaisePropertyChanged(nameof(HasIssuedSecret));
        }
    }

    public bool HasIssuedSecret => !string.IsNullOrWhiteSpace(IssuedSecret);

    private string _tokenMessage = "";

    /// <summary>
    /// What went wrong on this screen, shown on the screen.
    ///
    /// Every failure here used to go to the log and nowhere else, so pressing "Issue token" against
    /// a server that refused the request looked exactly like pressing it against a server that had
    /// not been asked: the form kept its contents and nothing happened. An operator has no reason to
    /// go looking in a log file for a button that appears to do nothing.
    /// </summary>
    public string TokenMessage
    {
        get => _tokenMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _tokenMessage, value);
            this.RaisePropertyChanged(nameof(HasTokenMessage));
        }
    }

    public bool HasTokenMessage => !string.IsNullOrWhiteSpace(TokenMessage);

    private bool _hasNoTokens;

    /// <summary>
    /// True when the list came back empty, so the empty grid can say so. An empty grid on its own is
    /// indistinguishable from a grid whose load failed.
    /// </summary>
    public bool HasNoTokens
    {
        get => _hasNoTokens;
        set => this.RaiseAndSetIfChanged(ref _hasNoTokens, value);
    }

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtIssueTokenClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRevokeTokenClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }

    #endregion

    public ApiTokensViewModel()
    {
        BtIssueTokenClicked = ReactiveCommand.CreateFromTask(IssueTokenAsync);
        BtRevokeTokenClicked = ReactiveCommand.CreateFromTask(RevokeTokenAsync);
        BtReloadClicked = ReactiveCommand.CreateFromTask(InitializeAsync);
    }

    public Task InitializeAsync() => LoadApiTokensAsync();

    #region METHODS

    private async Task LoadApiTokensAsync()
    {
        try
        {
            ApiTokens.Clear();
            foreach (var token in await AdminService.GetApiTokensAsync()) ApiTokens.Add(token);

            if (ScopeOptions.Count == 0)
                foreach (var scope in await AdminService.GetApiTokenScopesAsync())
                    ScopeOptions.Add(new SelectableOption { Name = scope });

            HasNoTokens = ApiTokens.Count == 0;
            TokenMessage = "";
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the API tokens: {Message}", ex.Message);

            // The list is empty either way; only the message distinguishes "no tokens yet" from
            // "the list could not be read".
            HasNoTokens = false;
            TokenMessage = ex.Message;
        }
    }

    private async Task IssueTokenAsync()
    {
        var scopes = string.Join(",", ScopeOptions.Where(o => o.IsSelected).Select(o => o.Name));

        // Both of these used to be a bare `return`, which is the same thing on screen as a button
        // that does not work.
        if (string.IsNullOrWhiteSpace(NewTokenName))
        {
            TokenMessage = Localizer["TokenNeedsANameMSG"];
            return;
        }

        if (string.IsNullOrWhiteSpace(scopes))
        {
            TokenMessage = Localizer["TokenNeedsAScopeMSG"];
            return;
        }

        try
        {
            var issued = await AdminService.IssueApiTokenAsync(NewTokenName, scopes,
                NewTokenExpiry?.UtcDateTime, entityId: null);

            IssuedSecret = issued.Secret;
            NewTokenName = "";
            foreach (var option in ScopeOptions) option.IsSelected = false;

            await LoadApiTokensAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not issue an API token: {Message}", ex.Message);

            // The service's message names the route, the status and — when something other than the
            // API answered — what did. That is the sentence the operator needs, so it is shown
            // rather than replaced with a generic failure.
            TokenMessage = ex.Message;
        }
    }

    private async Task RevokeTokenAsync()
    {
        if (SelectedToken == null)
        {
            TokenMessage = Localizer["SelectATokenToRevokeMSG"];
            return;
        }

        try
        {
            await AdminService.RevokeApiTokenAsync(SelectedToken.Id);
            await LoadApiTokensAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not revoke API token {Id}: {Message}", SelectedToken.Id, ex.Message);
            TokenMessage = ex.Message;
        }
    }

    #endregion
}

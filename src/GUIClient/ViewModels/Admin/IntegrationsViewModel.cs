using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using DAL.Entities;
using DAL.Enums;
using Model.Authentication.Federation;
using Model.Authentication.Scim;
using Model.Integrations;
using Model.Notifications;
using Model.Secrets;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Admin;

/// <summary>
/// The Track 4 administration screen: notification channels and subscriptions with their delivery log
/// (4.1), issue-tracker connections, status mappings and the sync-conflict queue (4.2), identity
/// providers and SCIM provisioning tokens (4.3), and the Trend Micro Vision One and SecurityScorecard
/// connections (4.4, 4.5).
///
/// One view model behind five tabs, for the same reason as <see cref="FindingsAdminViewModel"/>: an
/// administrator wiring up integrations does several of these in one sitting, and five near-identical
/// load/save view models would be more code saying less.
///
/// Credentials are write-only throughout. Every list the server returns carries a has-a-credential flag
/// rather than the credential, and every editor's credential box starts blank and is only sent when the
/// operator typed something — so leaving it blank keeps the stored value and there is no state in which
/// this view model holds a token it read back from the server.
/// </summary>
public class IntegrationsViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrNotificationChannels { get; } = Localizer["NotificationChannels"];
    public string StrSubscriptions { get; } = Localizer["Subscriptions"];
    public string StrDeliveryLog { get; } = Localizer["DeliveryLog"];
    public string StrIssueTrackers { get; } = Localizer["IssueTrackers"];
    public string StrIdentityProviders { get; } = Localizer["IdentityProviders"];
    public string StrPostureProviders { get; } = Localizer["PostureProviders"];
    public string StrName { get; } = Localizer["Name"];
    public string StrChannelKind { get; } = Localizer["ChannelKind"];
    public string StrEnabled { get; } = Localizer["Enabled"];
    public string StrFallbackChannel { get; } = Localizer["FallbackChannel"];
    public string StrWebhookUrl { get; } = Localizer["WebhookUrl"];
    public string StrRecipients { get; } = Localizer["Recipients"];
    public string StrSigningSecret { get; } = Localizer["SigningSecret"];
    public string StrSubjectPrefix { get; } = Localizer["SubjectPrefix"];
    public string StrSendTestMessage { get; } = Localizer["SendTestMessage"];
    public string StrTestConnection { get; } = Localizer["TestConnection"];
    public string StrEvent { get; } = Localizer["Event"];
    public string StrMinSeverity { get; } = Localizer["MinSeverity"];
    public string StrDigestWindow { get; } = Localizer["DigestWindowMinutes"];
    public string StrRequeue { get; } = Localizer["Requeue"];
    public string StrProvider { get; } = Localizer["Provider"];
    public string StrBaseUrl { get; } = Localizer["BaseUrl"];
    public string StrProjectKey { get; } = Localizer["ProjectKey"];
    public string StrIssueType { get; } = Localizer["IssueType"];
    public string StrAuthUser { get; } = Localizer["AuthUser"];
    public string StrApiToken { get; } = Localizer["ApiToken"];
    public string StrApiKey { get; } = Localizer["ApiKey"];
    public string StrWebhookSecret { get; } = Localizer["WebhookSecret"];
    public string StrDefaultLabels { get; } = Localizer["DefaultLabels"];
    public string StrAutoCreateMinSeverity { get; } = Localizer["AutoCreateMinSeverity"];
    public string StrPushFindingUpdates { get; } = Localizer["PushFindingUpdates"];
    public string StrPollInterval { get; } = Localizer["PollIntervalMinutes"];
    public string StrStatusMappings { get; } = Localizer["StatusMappings"];
    public string StrTitleTemplate { get; } = Localizer["TitleTemplate"];
    public string StrDescriptionTemplate { get; } = Localizer["DescriptionTemplate"];
    public string StrPriorityMapping { get; } = Localizer["PriorityMapping"];
    public string StrExternalStatus { get; } = Localizer["ExternalStatus"];
    public string StrLoadFromJira { get; } = Localizer["LoadFromJira"];
    public string StrSaveMappings { get; } = Localizer["SaveMappings"];
    public string StrPlaceholderHelp { get; } = Localizer["IssueTemplatePlaceholdersMSG"];
    public string StrPreview { get; } = Localizer["Preview"];
    public string StrPriority { get; } = Localizer["Priority"];
    public string StrPreviewFinding { get; } = Localizer["PreviewFinding"];
    public string StrRenderedTitle { get; } = Localizer["RenderedTitle"];
    public string StrRenderedBody { get; } = Localizer["RenderedBody"];
    public string StrPreviewHelp { get; } = Localizer["IssueTemplatePreviewMSG"];
    public string StrSyncNow { get; } = Localizer["SyncNow"];
    public string StrSyncConflicts { get; } = Localizer["SyncConflicts"];
    public string StrResolveConflict { get; } = Localizer["ResolveConflict"];
    public string StrProtocol { get; } = Localizer["Protocol"];
    public string StrAuthority { get; } = Localizer["Authority"];
    public string StrClientId { get; } = Localizer["ClientId"];
    public string StrClientSecret { get; } = Localizer["ClientSecret"];
    public string StrMetadataUrl { get; } = Localizer["MetadataUrl"];
    public string StrMetadataXml { get; } = Localizer["MetadataXml"];
    public string StrRequireSignedAssertions { get; } = Localizer["RequireSignedAssertions"];
    public string StrJitProvisioning { get; } = Localizer["JitProvisioning"];
    public string StrClaimMapping { get; } = Localizer["ClaimMapping"];
    public string StrGroupMapping { get; } = Localizer["GroupMapping"];
    public string StrScimTokens { get; } = Localizer["ScimTokens"];
    public string StrIssueScimToken { get; } = Localizer["IssueScimToken"];
    public string StrScimRequestLog { get; } = Localizer["ScimRequestLog"];
    public string StrRegion { get; } = Localizer["Region"];
    public string StrVirtualPatchClosesFinding { get; } = Localizer["VirtualPatchClosesFinding"];
    public string StrPushExemptions { get; } = Localizer["PushExemptions"];
    public string StrSyncVulnerabilities { get; } = Localizer["SyncVulnerabilities"];
    public string StrSyncRiskScores { get; } = Localizer["SyncRiskScores"];
    public string StrSyncIssues { get; } = Localizer["SyncIssues"];
    public string StrSyncInterval { get; } = Localizer["SyncIntervalHours"];
    public string StrDomain { get; } = Localizer["Domain"];
    public string StrFactorHistory { get; } = Localizer["FactorHistory"];
    public string StrSyncLog { get; } = Localizer["SyncLog"];
    public string StrSyncProgress { get; } = Localizer["SyncProgress"];
    public string StrTrendMicro { get; } = Localizer["TrendMicroVisionOne"];

    // Secret vaults.
    public string StrSecretVaults { get; } = Localizer["SecretVaults"];
    public string StrSecretVault { get; } = Localizer["SecretVault"];
    public string StrUseVaultSecret { get; } = Localizer["UseVaultSecret"];
    public string StrStopUsingVault { get; } = Localizer["StopUsingVault"];
    public string StrWillBeSaved { get; } = Localizer["WillBeSaved"];
    public string StrMachineId { get; } = Localizer["MachineId"];
    public string StrMachineIdHint { get; } = Localizer["MachineIdOptionalMSG"];
    public string StrCacheMinutes { get; } = Localizer["CacheMinutes"];
    public string StrSecretCacheHint { get; } = Localizer["SecretCacheHintMSG"];
    public string StrMachineIdRequiredHint { get; } = Localizer["MachineIdRequiredMSG"];
    public string StrAppId { get; } = Localizer["AppId"];
    public string StrAppIdHint { get; } = Localizer["AppIdOptionalMSG"];
    public string StrAppIdRequiredHint { get; } = Localizer["AppIdRequiredMSG"];
    public string StrIgnoreSslErrors { get; } = Localizer["IgnoreSslErrors"];
    public string StrIgnoreSslErrorsHint { get; } = Localizer["IgnoreSslErrorsMSG"];
    public string StrVaultBaseUrlHint { get; } = Localizer["VaultBaseUrlHintMSG"];
    public string StrPlugin { get; } = Localizer["Plugin"];
    public string StrLastTest { get; } = Localizer["LastTest"];
    public string StrNoSecretVaultPlugins { get; } = Localizer["NoSecretVaultPluginsMSG"];
    public string StrSecurityScorecard { get; } = Localizer["SecurityScorecard"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrDelete { get; } = Localizer["Delete"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrSecretsWriteOnly { get; } = Localizer["SecretsWriteOnlyMSG"];
    public string StrTitle { get; } = Localizer["Title"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrDescription { get; } = Localizer["Description"];
    public string StrAttempts { get; } = Localizer["Attempts"];
    public string StrLastError { get; } = Localizer["LastError"];
    public string StrAction { get; } = Localizer["Action"];
    public string StrOutboundTransition { get; } = Localizer["OutboundTransition"];
    public string StrLastUsed { get; } = Localizer["LastUsed"];
    public string StrLastSync { get; } = Localizer["LastSync"];
    public string StrFactor { get; } = Localizer["Factor"];
    public string StrScore { get; } = Localizer["Score"];
    public string StrGrade { get; } = Localizer["Grade"];
    public string StrCapturedAt { get; } = Localizer["CapturedAt"];
    public string StrSecretShownOnce { get; } = Localizer["SecretShownOnceMSG"];
    private static string MsgSaved => Localizer["IntegrationSavedMSG"];
    private static string MsgDeleted => Localizer["IntegrationDeletedMSG"];

    #endregion

    #region SERVICES

    private IIntegrationsService Integrations { get; } = GetService<IIntegrationsService>();

    /// <summary>Opens the secret picker. Resolved once rather than per click.</summary>
    private IDialogService Dialogs { get; } = GetService<IDialogService>();

    #endregion

    #region 4.1 NOTIFICATION CHANNELS

    public ObservableCollection<NotificationChannel> Channels { get; } = new();

    public ObservableCollection<NotificationChannelProvider> ChannelProviders { get; } = new();

    private NotificationChannel? _selectedChannel;

    /// <summary>
    /// Selecting a channel copies it into the editor rather than binding the grid row directly: the
    /// editor's credential boxes start blank, and binding them to the row would show the redaction
    /// placeholder and then save it back as the token.
    /// </summary>
    public NotificationChannel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedChannel, value);
            LoadChannelEditor(value);
        }
    }

    private string _channelName = "";
    public string ChannelName
    {
        get => _channelName;
        set => this.RaiseAndSetIfChanged(ref _channelName, value);
    }

    private NotificationChannelKind _channelKind = NotificationChannelKind.Slack;
    public NotificationChannelKind ChannelKind
    {
        get => _channelKind;
        set => this.RaiseAndSetIfChanged(ref _channelKind, value);
    }

    private bool _channelEnabled = true;
    public bool ChannelEnabled
    {
        get => _channelEnabled;
        set => this.RaiseAndSetIfChanged(ref _channelEnabled, value);
    }

    private string _channelWebhookUrl = "";
    public string ChannelWebhookUrl
    {
        get => _channelWebhookUrl;
        set => this.RaiseAndSetIfChanged(ref _channelWebhookUrl, value);
    }

    private string _channelRecipients = "";
    public string ChannelRecipients
    {
        get => _channelRecipients;
        set => this.RaiseAndSetIfChanged(ref _channelRecipients, value);
    }

    private string _channelSigningSecret = "";
    public string ChannelSigningSecret
    {
        get => _channelSigningSecret;
        set => this.RaiseAndSetIfChanged(ref _channelSigningSecret, value);
    }

    private string _channelSubjectPrefix = "";
    public string ChannelSubjectPrefix
    {
        get => _channelSubjectPrefix;
        set => this.RaiseAndSetIfChanged(ref _channelSubjectPrefix, value);
    }

    private int? _channelFallbackId;
    public int? ChannelFallbackId
    {
        get => _channelFallbackId;
        set => this.RaiseAndSetIfChanged(ref _channelFallbackId, value);
    }

    #endregion

    #region 4.1.3 SUBSCRIPTIONS AND DELIVERY LOG

    public ObservableCollection<NotificationSubscription> Subscriptions { get; } = new();

    public ObservableCollection<NotificationEventDescriptor> NotificationEvents { get; } = new();

    public ObservableCollection<NotificationDelivery> Deliveries { get; } = new();

    private NotificationSubscription? _selectedSubscription;
    public NotificationSubscription? SelectedSubscription
    {
        get => _selectedSubscription;
        set => this.RaiseAndSetIfChanged(ref _selectedSubscription, value);
    }

    private NotificationDelivery? _selectedDelivery;
    public NotificationDelivery? SelectedDelivery
    {
        get => _selectedDelivery;
        set => this.RaiseAndSetIfChanged(ref _selectedDelivery, value);
    }

    private NotificationEventType _subscriptionEvent = NotificationEventType.RiskCreated;
    public NotificationEventType SubscriptionEvent
    {
        get => _subscriptionEvent;
        set => this.RaiseAndSetIfChanged(ref _subscriptionEvent, value);
    }

    private int _subscriptionChannelId;
    public int SubscriptionChannelId
    {
        get => _subscriptionChannelId;
        set => this.RaiseAndSetIfChanged(ref _subscriptionChannelId, value);
    }

    private int? _subscriptionMinSeverity;
    public int? SubscriptionMinSeverity
    {
        get => _subscriptionMinSeverity;
        set => this.RaiseAndSetIfChanged(ref _subscriptionMinSeverity, value);
    }

    private int? _subscriptionDigestMinutes;
    public int? SubscriptionDigestMinutes
    {
        get => _subscriptionDigestMinutes;
        set => this.RaiseAndSetIfChanged(ref _subscriptionDigestMinutes, value);
    }

    #endregion

    #region 4.2 ISSUE TRACKERS

    public ObservableCollection<IssueTrackerConnectionView> IssueTrackers { get; } = new();

    public ObservableCollection<IssueTrackerProviderInfo> IssueTrackerProviders { get; } = new();

    /// <summary>
    /// The status mappings, editable.
    ///
    /// Entities rather than the read-only view type, and a mutable collection rather than a bound
    /// list: milestone 4.2 shipped this grid as <c>IsReadOnly="True"</c> with no way to add a row, so
    /// a mapping could be read and never changed. That was the gap — the server has had a wholesale
    /// PUT for it since 4.2.1 and nothing called it.
    /// </summary>
    public ObservableCollection<IssueStatusMapping> StatusMappings { get; } = new();

    /// <summary>The tracker's own statuses, loaded from Jira, so a mapping row is picked and not typed.</summary>
    public ObservableCollection<string> ExternalStatusOptions { get; } = new();

    public ObservableCollection<IssueSyncAction> SyncActions { get; } =
        new(Enum.GetValues<IssueSyncAction>());

    private IssueStatusMapping? _selectedStatusMapping;
    public IssueStatusMapping? SelectedStatusMapping
    {
        get => _selectedStatusMapping;
        set => this.RaiseAndSetIfChanged(ref _selectedStatusMapping, value);
    }

    /// <summary>
    /// The Jira Service Management and Assets tabs (4.6). Owned here so one place decides which
    /// connection is being edited; hidden by its own <c>IsJira</c> flag for the other providers.
    /// </summary>
    public JiraIntegrationViewModel Jira { get; } = new();

    #region TEMPLATE PREVIEW

    // The rendered title and body for a real finding, without creating anything. The server has had
    // IIssueTrackerService.PreviewAsync since 4.2.1 and nothing called it, so the templates were
    // editable and unverifiable: an operator changed a placeholder and found out what it produced by
    // filing a ticket in somebody else's project.

    private int _previewFindingId;

    /// <summary>
    /// Which finding to render against. An id rather than a picker: the finding register is paged and
    /// filtered and searchable, and duplicating that here to choose a preview subject would be a
    /// second finding browser. The id is what an operator has in front of them.
    /// </summary>
    public int PreviewFindingId
    {
        get => _previewFindingId;
        set => this.RaiseAndSetIfChanged(ref _previewFindingId, value);
    }

    private string _previewTitle = "";
    public string PreviewTitle
    {
        get => _previewTitle;
        private set => this.RaiseAndSetIfChanged(ref _previewTitle, value);
    }

    private string _previewBody = "";
    public string PreviewBody
    {
        get => _previewBody;
        private set => this.RaiseAndSetIfChanged(ref _previewBody, value);
    }

    private string _previewPriority = "";
    public string PreviewPriority
    {
        get => _previewPriority;
        private set => this.RaiseAndSetIfChanged(ref _previewPriority, value);
    }

    private bool _hasPreview;
    public bool HasPreview
    {
        get => _hasPreview;
        private set => this.RaiseAndSetIfChanged(ref _hasPreview, value);
    }

    #endregion

    public ObservableCollection<FindingIssueLinkView> SyncConflicts { get; } = new();

    private IssueTrackerConnectionView? _selectedIssueTracker;
    public IssueTrackerConnectionView? SelectedIssueTracker
    {
        get => _selectedIssueTracker;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedIssueTracker, value);
            LoadIssueTrackerEditor(value);
            ClearPreview();
            if (value != null) _ = LoadStatusMappingsAsync(value.Id);
            _ = Jira.LoadAsync(value?.Id ?? 0, value?.Provider ?? IssueTrackerProviderKind.Jira);
        }
    }

    private FindingIssueLinkView? _selectedConflict;
    public FindingIssueLinkView? SelectedConflict
    {
        get => _selectedConflict;
        set => this.RaiseAndSetIfChanged(ref _selectedConflict, value);
    }

    /// <summary>
    /// The connection being edited. A plain entity rather than a per-field property set: the form is
    /// fifteen fields, and the credential boxes below are the only ones that need special handling.
    /// </summary>
    public IssueTrackerConnection IssueTrackerDraft { get; private set; } = NewIssueTracker();

    /// <summary>
    /// The provider ComboBox's selection, guarded the same way as
    /// <see cref="SelectedTrendMicroRegion"/> and for the same reason: the control resets its selection
    /// to null when it cannot resolve the current value — on first render, and again every time the
    /// provider list is cleared and refilled — and writes that null back through a two-way binding.
    ///
    /// Less damaging here than on the region, because <c>Provider</c> is a non-nullable enum and the
    /// binding rejects the null rather than storing it. The dropdown still went blank next to a draft
    /// that had a provider, and the shape of the binding is the defect either way.
    /// </summary>
    public IssueTrackerProviderKind? SelectedIssueTrackerProvider
    {
        get => IssueTrackerDraft.Provider;
        set
        {
            if (value == null) return;

            IssueTrackerDraft.Provider = value.Value;
            this.RaisePropertyChanged();
        }
    }

    private string _issueTrackerToken = "";
    public string IssueTrackerToken
    {
        get => _issueTrackerToken;
        set => this.RaiseAndSetIfChanged(ref _issueTrackerToken, value);
    }

    private string _issueTrackerWebhookSecret = "";
    public string IssueTrackerWebhookSecret
    {
        get => _issueTrackerWebhookSecret;
        set => this.RaiseAndSetIfChanged(ref _issueTrackerWebhookSecret, value);
    }

    #endregion

    #region 4.3 ENTERPRISE AUTHENTICATION

    public ObservableCollection<IdentityProviderView> IdentityProviders { get; } = new();

    public ObservableCollection<ScimTokenView> ScimTokens { get; } = new();

    public ObservableCollection<ScimRequestLog> ScimLog { get; } = new();

    private IdentityProviderView? _selectedIdentityProvider;
    public IdentityProviderView? SelectedIdentityProvider
    {
        get => _selectedIdentityProvider;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedIdentityProvider, value);
            LoadIdentityProviderEditor(value);
        }
    }

    private ScimTokenView? _selectedScimToken;
    public ScimTokenView? SelectedScimToken
    {
        get => _selectedScimToken;
        set => this.RaiseAndSetIfChanged(ref _selectedScimToken, value);
    }

    public IdentityProvider IdentityProviderDraft { get; private set; } = NewIdentityProvider();

    private string _identityProviderClientSecret = "";
    public string IdentityProviderClientSecret
    {
        get => _identityProviderClientSecret;
        set => this.RaiseAndSetIfChanged(ref _identityProviderClientSecret, value);
    }

    private string _scimTokenName = "";
    public string ScimTokenName
    {
        get => _scimTokenName;
        set => this.RaiseAndSetIfChanged(ref _scimTokenName, value);
    }

    private string _issuedSecret = "";

    /// <summary>
    /// The freshly issued provisioning token. Held only here and only until the view is left: the server
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

    #endregion

    #region 4.4 / 4.5 POSTURE PROVIDERS

    public ObservableCollection<TrendMicroConnectionView> TrendMicroConnections { get; } = new();

    public ObservableCollection<string> TrendMicroRegions { get; } = new();

    public ObservableCollection<SecurityScorecardConnectionView> ScorecardConnections { get; } = new();

    public ObservableCollection<SecurityScorecardFactor> ScorecardHistory { get; } = new();

    public ObservableCollection<IntegrationSyncLog> SyncLog { get; } = new();

    private IntegrationSyncLog? _selectedSyncLog;

    /// <summary>The run whose progress trail the panel below the log is showing.</summary>
    public IntegrationSyncLog? SelectedSyncLog
    {
        get => _selectedSyncLog;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSyncLog, value);
            this.RaisePropertyChanged(nameof(SelectedSyncLogProgress));
        }
    }

    /// <summary>
    /// The selected run's progress trail, or the prompt to pick one.
    ///
    /// A run that is still going has a trail and no summary, which is the case this panel exists for:
    /// the log's own columns can only say a sync is Running, and "Running" is the least useful thing to
    /// know about a sync that has been running for twenty minutes.
    /// </summary>
    public string SelectedSyncLogProgress =>
        SelectedSyncLog == null
            ? Localizer["SyncProgressEmptyMSG"]
            : string.IsNullOrWhiteSpace(SelectedSyncLog.ProgressLog)
                ? Localizer["SyncProgressNoneMSG"]
                : SelectedSyncLog.ProgressLog!;

    private TrendMicroConnectionView? _selectedTrendMicro;
    public TrendMicroConnectionView? SelectedTrendMicro
    {
        get => _selectedTrendMicro;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTrendMicro, value);
            LoadTrendMicroEditor(value);
        }
    }

    private SecurityScorecardConnectionView? _selectedScorecard;
    public SecurityScorecardConnectionView? SelectedScorecard
    {
        get => _selectedScorecard;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedScorecard, value);
            LoadScorecardEditor(value);
            if (value != null) _ = LoadScorecardHistoryAsync(value.Id);
        }
    }

    public TrendMicroConnection TrendMicroDraft { get; private set; } = NewTrendMicro();

    /// <summary>
    /// The region ComboBox's selection, guarded against the null the control writes back on its own.
    ///
    /// The ComboBox used to bind <c>SelectedItem</c> straight to <c>TrendMicroDraft.Region</c>.
    /// <c>SelectedItem</c> is a two-way binding by default, and a ComboBox whose <c>ItemsSource</c> does
    /// not contain the current value resets its selection to null and writes that null into the source.
    /// Two things did that here: the region list is empty on first render, so the draft's default "us"
    /// could not resolve, and <see cref="LoadPostureProvidersAsync"/> clears the collection on load and
    /// after every save. So unless the operator happened to touch the dropdown, the connection was sent
    /// with no region at all and refused by model validation — "The Region field is required", from a
    /// 400 the desktop client did not show at the time.
    ///
    /// Ignoring the empty write is the fix rather than repopulating without clearing: the control resets
    /// its selection whenever it cannot resolve a value, and the model should not be destroyed by a
    /// control's view state either way.
    /// </summary>
    public string? SelectedTrendMicroRegion
    {
        get => TrendMicroDraft.Region;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            TrendMicroDraft.Region = value;
            this.RaisePropertyChanged();
        }
    }

    private string _trendMicroApiKey = "";
    public string TrendMicroApiKey
    {
        get => _trendMicroApiKey;
        set => this.RaiseAndSetIfChanged(ref _trendMicroApiKey, value);
    }

    public SecurityScorecardConnection ScorecardDraft { get; private set; } = NewScorecard();

    private string _scorecardApiToken = "";
    public string ScorecardApiToken
    {
        get => _scorecardApiToken;
        set => this.RaiseAndSetIfChanged(ref _scorecardApiToken, value);
    }

    #endregion

    #region SECRET VAULTS

    /// <summary>
    /// Whether this installation has a usable vault. Every picker button hangs off it, so an
    /// installation with no vault plugin sees no new controls at all.
    /// </summary>
    private bool _vaultAvailable;
    public bool VaultAvailable
    {
        get => _vaultAvailable;
        private set
        {
            this.RaiseAndSetIfChanged(ref _vaultAvailable, value);
            foreach (var field in VaultFields.Values) field.VaultAvailable = value;
        }
    }

    public ObservableCollection<SecretVaultConnectionView> VaultConnections { get; } = new();

    public ObservableCollection<SecretVaultPluginInfo> VaultPlugins { get; } = new();

    private SecretVaultConnectionView? _selectedVault;
    public SecretVaultConnectionView? SelectedVault
    {
        get => _selectedVault;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedVault, value);
            LoadVaultEditor(value);
        }
    }

    public SecretVaultConnectionInput VaultDraft { get; private set; } = NewVault();

    /// <summary>
    /// The plugin ComboBox's selection, guarded against the null the control writes back when its
    /// items are replaced — the same trap the Vision One region field fell into, and the same fix.
    /// </summary>
    public string? SelectedVaultPluginName
    {
        get => VaultDraft.PluginName;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            VaultDraft.PluginName = value;
            this.RaisePropertyChanged();

            // The machine-ID hint is a property of the *plugin*, so it changes with this selection and
            // not with anything else on the form. Raised explicitly because the two properties below
            // are computed rather than stored, and ReactiveUI has no way to know they depend on this.
            this.RaisePropertyChanged(nameof(VaultRequiresMachineId));
            this.RaisePropertyChanged(nameof(StrVaultMachineIdHint));
            this.RaisePropertyChanged(nameof(VaultRequiresAppId));
            this.RaisePropertyChanged(nameof(StrVaultAppIdHint));
        }
    }

    /// <summary>
    /// Whether the selected plugin declares that it cannot work without a machine ID.
    ///
    /// Read from the plugin list rather than from the connection, because it has to be right for a
    /// connection that does not exist yet — which is precisely when an operator needs to be told.
    /// The server enforces the same rule on save; this is so the form says it first.
    /// </summary>
    public bool VaultRequiresMachineId =>
        VaultPlugins.FirstOrDefault(p =>
            string.Equals(p.PluginName, VaultDraft.PluginName, StringComparison.Ordinal))
            ?.RequiresMachineId ?? false;

    /// <summary>The machine-ID hint, in the "required" wording when the plugin demands one.</summary>
    public string StrVaultMachineIdHint =>
        VaultRequiresMachineId ? StrMachineIdRequiredHint : StrMachineIdHint;

    /// <summary>
    /// Whether the selected plugin declares that it authorizes by application identity — BastionVault's
    /// <c>app_id</c> — and so cannot work without one. Read from the plugin list for the same reason
    /// as <see cref="VaultRequiresMachineId"/>: it has to be right before the connection exists.
    /// </summary>
    public bool VaultRequiresAppId =>
        VaultPlugins.FirstOrDefault(p =>
            string.Equals(p.PluginName, VaultDraft.PluginName, StringComparison.Ordinal))
            ?.RequiresAppId ?? false;

    /// <summary>The app-ID hint, in the "required" wording when the plugin demands one.</summary>
    public string StrVaultAppIdHint =>
        VaultRequiresAppId ? StrAppIdRequiredHint : StrAppIdHint;

    private string _vaultApiKey = "";
    public string VaultApiKey
    {
        get => _vaultApiKey;
        set => this.RaiseAndSetIfChanged(ref _vaultApiKey, value);
    }

    private int _vaultUsageCount;

    /// <summary>How many credential fields resolve through the selected connection.</summary>
    public int VaultUsageCount
    {
        get => _vaultUsageCount;
        private set
        {
            this.RaiseAndSetIfChanged(ref _vaultUsageCount, value);
            this.RaisePropertyChanged(nameof(VaultUsageCaption));
        }
    }

    /// <summary>
    /// The usage count as a sentence. Shown because the delete refusal that mentions it should not be
    /// the first time an operator hears that the connection is load-bearing.
    /// </summary>
    public string VaultUsageCaption => string.Format(Localizer["VaultUsageMSG"], VaultUsageCount);

    /// <summary>True when no secret-vault plugin is installed, so the tab can explain itself.</summary>
    public bool HasNoVaultPlugins => VaultPlugins.Count == 0;

    /// <summary>
    /// One state object per credential field on this screen, keyed by the string the view passes as
    /// the picker button's <c>CommandParameter</c>.
    ///
    /// A dictionary plus two commands rather than fourteen commands: the seven fields differ only in
    /// which secret they bind, and a command per field is seven near-identical methods that drift.
    /// The individual properties below exist because compiled XAML bindings cannot index a dictionary
    /// with a string key.
    /// </summary>
    private Dictionary<string, VaultSecretFieldState> VaultFields { get; }

    public VaultSecretFieldState TrendMicroApiKeyVault { get; } = new();
    public VaultSecretFieldState ScorecardApiTokenVault { get; } = new();
    public VaultSecretFieldState IssueTrackerTokenVault { get; } = new();
    public VaultSecretFieldState IssueTrackerWebhookSecretVault { get; } = new();
    public VaultSecretFieldState IdentityProviderClientSecretVault { get; } = new();
    public VaultSecretFieldState ChannelWebhookUrlVault { get; } = new();
    public VaultSecretFieldState ChannelSigningSecretVault { get; } = new();

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveChannelClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteChannelClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtTestChannelClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewChannelClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddSubscriptionClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteSubscriptionClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRequeueDeliveryClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveIssueTrackerClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteIssueTrackerClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtTestIssueTrackerClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSyncIssueTrackerClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewIssueTrackerClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddStatusMappingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveStatusMappingClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveStatusMappingsClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtLoadStatusesFromJiraClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtPreviewTemplateClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtResolveConflictClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveIdentityProviderClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteIdentityProviderClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtTestIdentityProviderClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewIdentityProviderClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtIssueScimTokenClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRevokeScimTokenClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveTrendMicroClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteTrendMicroClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtTestTrendMicroClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSyncTrendMicroClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewTrendMicroClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveScorecardClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteScorecardClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtTestScorecardClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSyncScorecardClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewScorecardClicked { get; }

    /// <summary>Opens the picker for the credential field named by the command parameter.</summary>
    public ReactiveCommand<string, RxVoid> BtPickVaultSecretClicked { get; }

    /// <summary>Detaches the named credential field from the vault so a literal can be typed.</summary>
    public ReactiveCommand<string, RxVoid> BtDetachVaultSecretClicked { get; }

    public ReactiveCommand<RxVoid, RxVoid> BtSaveVaultClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeleteVaultClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtTestVaultClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewVaultClicked { get; }

    #endregion

    public IntegrationsViewModel()
    {
        BtReloadClicked = ReactiveCommand.CreateFromTask(InitializeAsync);

        BtSaveChannelClicked = ReactiveCommand.CreateFromTask(SaveChannelAsync);
        BtDeleteChannelClicked = ReactiveCommand.CreateFromTask(DeleteChannelAsync);
        BtTestChannelClicked = ReactiveCommand.CreateFromTask(TestChannelAsync);
        BtNewChannelClicked = ReactiveCommand.Create(NewChannel);

        BtAddSubscriptionClicked = ReactiveCommand.CreateFromTask(AddSubscriptionAsync);
        BtDeleteSubscriptionClicked = ReactiveCommand.CreateFromTask(DeleteSubscriptionAsync);
        BtRequeueDeliveryClicked = ReactiveCommand.CreateFromTask(RequeueDeliveryAsync);

        BtSaveIssueTrackerClicked = ReactiveCommand.CreateFromTask(SaveIssueTrackerAsync);
        BtDeleteIssueTrackerClicked = ReactiveCommand.CreateFromTask(DeleteIssueTrackerAsync);
        BtTestIssueTrackerClicked = ReactiveCommand.CreateFromTask(TestIssueTrackerAsync);
        BtSyncIssueTrackerClicked = ReactiveCommand.CreateFromTask(SyncIssueTrackerAsync);
        BtNewIssueTrackerClicked = ReactiveCommand.Create(NewIssueTrackerDraft);
        BtAddStatusMappingClicked = ReactiveCommand.Create(AddStatusMapping);
        BtRemoveStatusMappingClicked = ReactiveCommand.Create(RemoveStatusMapping);
        BtSaveStatusMappingsClicked = ReactiveCommand.CreateFromTask(SaveStatusMappingsAsync);
        BtLoadStatusesFromJiraClicked = ReactiveCommand.CreateFromTask(LoadStatusesFromJiraAsync);
        BtPreviewTemplateClicked = ReactiveCommand.CreateFromTask(PreviewTemplateAsync);
        BtResolveConflictClicked = ReactiveCommand.CreateFromTask(ResolveConflictAsync);

        BtSaveIdentityProviderClicked = ReactiveCommand.CreateFromTask(SaveIdentityProviderAsync);
        BtDeleteIdentityProviderClicked = ReactiveCommand.CreateFromTask(DeleteIdentityProviderAsync);
        BtTestIdentityProviderClicked = ReactiveCommand.CreateFromTask(TestIdentityProviderAsync);
        BtNewIdentityProviderClicked = ReactiveCommand.Create(NewIdentityProviderDraft);
        BtIssueScimTokenClicked = ReactiveCommand.CreateFromTask(IssueScimTokenAsync);
        BtRevokeScimTokenClicked = ReactiveCommand.CreateFromTask(RevokeScimTokenAsync);

        BtSaveTrendMicroClicked = ReactiveCommand.CreateFromTask(SaveTrendMicroAsync);
        BtDeleteTrendMicroClicked = ReactiveCommand.CreateFromTask(DeleteTrendMicroAsync);
        BtTestTrendMicroClicked = ReactiveCommand.CreateFromTask(TestTrendMicroAsync);
        BtSyncTrendMicroClicked = ReactiveCommand.CreateFromTask(SyncTrendMicroAsync);
        BtNewTrendMicroClicked = ReactiveCommand.Create(NewTrendMicroDraft);

        BtSaveScorecardClicked = ReactiveCommand.CreateFromTask(SaveScorecardAsync);
        BtDeleteScorecardClicked = ReactiveCommand.CreateFromTask(DeleteScorecardAsync);
        BtTestScorecardClicked = ReactiveCommand.CreateFromTask(TestScorecardAsync);
        BtSyncScorecardClicked = ReactiveCommand.CreateFromTask(SyncScorecardAsync);
        BtNewScorecardClicked = ReactiveCommand.Create(NewScorecardDraft);

        BtPickVaultSecretClicked = ReactiveCommand.CreateFromTask<string>(PickVaultSecretAsync);
        BtDetachVaultSecretClicked = ReactiveCommand.Create<string>(DetachVaultSecret);

        BtSaveVaultClicked = ReactiveCommand.CreateFromTask(SaveVaultAsync);
        BtDeleteVaultClicked = ReactiveCommand.CreateFromTask(DeleteVaultAsync);
        BtTestVaultClicked = ReactiveCommand.CreateFromTask(TestVaultAsync);
        BtNewVaultClicked = ReactiveCommand.Create(NewVaultDraft);

        VaultFields = new Dictionary<string, VaultSecretFieldState>(StringComparer.Ordinal)
        {
            [VaultFieldKeys.TrendMicroApiKey] = TrendMicroApiKeyVault,
            [VaultFieldKeys.ScorecardApiToken] = ScorecardApiTokenVault,
            [VaultFieldKeys.IssueTrackerToken] = IssueTrackerTokenVault,
            [VaultFieldKeys.IssueTrackerWebhookSecret] = IssueTrackerWebhookSecretVault,
            [VaultFieldKeys.IdentityProviderClientSecret] = IdentityProviderClientSecretVault,
            [VaultFieldKeys.ChannelWebhookUrl] = ChannelWebhookUrlVault,
            [VaultFieldKeys.ChannelSigningSecret] = ChannelSigningSecretVault
        };
    }

    /// <summary>
    /// The <c>CommandParameter</c> values the view passes to the two vault commands.
    ///
    /// Constants rather than literals scattered through the XAML: a typo in a binding parameter is a
    /// button that silently does nothing, and there is no compiler to catch it on that side. What
    /// does catch it is <c>GUIClient.Tests.Views.IntegrationsVaultBindingTests</c>, which parses the
    /// view and asserts every CommandParameter it passes is one of these — and that each of these has
    /// both a picker and a detach button.
    /// </summary>
    public static class VaultFieldKeys
    {
        public const string TrendMicroApiKey = "trendmicro-apikey";
        public const string ScorecardApiToken = "scorecard-apitoken";
        public const string IssueTrackerToken = "issuetracker-token";
        public const string IssueTrackerWebhookSecret = "issuetracker-webhooksecret";
        public const string IdentityProviderClientSecret = "idp-clientsecret";
        public const string ChannelWebhookUrl = "channel-webhookurl";
        public const string ChannelSigningSecret = "channel-signingsecret";

        /// <summary>Every key, so a test can assert the view-model wires all of them.</summary>
        public static readonly string[] All =
        [
            TrendMicroApiKey, ScorecardApiToken, IssueTrackerToken, IssueTrackerWebhookSecret,
            IdentityProviderClientSecret, ChannelWebhookUrl, ChannelSigningSecret
        ];
    }

    /// <summary>
    /// Loads everything the five tabs need.
    ///
    /// Each section is loaded in its own try/catch rather than one around the lot: a server that has no
    /// SecurityScorecard connections configured should not leave the notification tab empty because the
    /// call after it failed.
    /// </summary>
    public async Task InitializeAsync()
    {
        await WithBusyAsync(async () =>
        {
            await LoadChannelsAsync();
            await LoadSubscriptionsAsync();
            await LoadDeliveriesAsync();
            await LoadIssueTrackersAsync();
            await LoadConflictsAsync();
            await LoadIdentityProvidersAsync();
            await LoadScimAsync();
            await LoadPostureProvidersAsync();
            await LoadSecretVaultsAsync();
        });
    }

    #region 4.1 CHANNEL METHODS

    private async Task LoadChannelsAsync()
    {
        try
        {
            var providers = await Integrations.GetChannelProvidersAsync();
            ChannelProviders.Clear();
            foreach (var provider in providers) ChannelProviders.Add(provider);

            var channels = await Integrations.GetChannelsAsync();
            Channels.Clear();
            foreach (var channel in channels) Channels.Add(channel);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the notification channels: {Message}", ex.Message);
        }
    }

    private void NewChannel()
    {
        SelectedChannel = null;
        ChannelName = "";
        ChannelKind = NotificationChannelKind.Slack;
        ChannelEnabled = true;
        ChannelWebhookUrl = "";
        ChannelRecipients = "";
        ChannelSigningSecret = "";
        ChannelWebhookUrlVault.Reset();
        ChannelSigningSecretVault.Reset();
        ChannelSubjectPrefix = "";
        ChannelFallbackId = null;
    }

    private void LoadChannelEditor(NotificationChannel? channel)
    {
        if (channel == null) return;

        var configuration = ChannelConfiguration.Parse(channel.ConfigurationJson);

        ChannelName = channel.Name;
        ChannelKind = channel.Kind;
        ChannelEnabled = channel.Enabled;
        ChannelFallbackId = channel.FallbackChannelId;
        ChannelRecipients = configuration.Recipients ?? "";
        ChannelSubjectPrefix = configuration.SubjectPrefix ?? "";

        // Deliberately blank rather than the placeholder the server sent: a blank box means "unchanged",
        // and showing bullets that then get saved as the token is the bug this avoids.
        ChannelWebhookUrl = "";
        ChannelSigningSecret = "";

        // Reset rather than LoadFrom: a channel's secrets live inside its configuration JSON as
        // ciphertext, and the server does not report which of them are vault references. So the
        // client genuinely does not know, and showing nothing is the honest answer — picking a secret
        // still works and still binds.
        ChannelWebhookUrlVault.Reset();
        ChannelSigningSecretVault.Reset();
    }

    /// <summary>
    /// Composes the configuration to send. An untouched credential box becomes the redaction
    /// placeholder, which is what tells the server to keep the stored value.
    /// </summary>
    private string ComposeChannelConfiguration()
    {
        var configuration = new ChannelConfiguration
        {
            Recipients = string.IsNullOrWhiteSpace(ChannelRecipients) ? null : ChannelRecipients.Trim(),
            SubjectPrefix = string.IsNullOrWhiteSpace(ChannelSubjectPrefix)
                ? null
                : ChannelSubjectPrefix.Trim(),
            // The vault branch first, then the typed literal, then the redaction placeholder that
            // means "keep what is stored". Getting that order wrong would send the placeholder for a
            // field the operator just bound to the vault.
            WebhookUrl = VaultAwareSecret(VaultFieldKeys.ChannelWebhookUrl, ChannelWebhookUrl)
                         ?? (SelectedChannel == null ? null : ChannelConfiguration.RedactedPlaceholder),
            SigningSecret = VaultAwareSecret(VaultFieldKeys.ChannelSigningSecret, ChannelSigningSecret)
                            ?? (SelectedChannel == null ? null : ChannelConfiguration.RedactedPlaceholder)
        };

        return configuration.ToJson();
    }

    private async Task SaveChannelAsync()
    {
        if (string.IsNullOrWhiteSpace(ChannelName))
        {
            Toasts.Warning(Localizer["NameRequiredMSG"]);
            return;
        }

        var channel = new NotificationChannel
        {
            Id = SelectedChannel?.Id ?? 0,
            Name = ChannelName.Trim(),
            Kind = ChannelKind,
            Enabled = ChannelEnabled,
            FallbackChannelId = ChannelFallbackId,
            ConfigurationJson = ComposeChannelConfiguration()
        };

        try
        {
            var saved = channel.Id == 0
                ? await Integrations.CreateChannelAsync(channel)
                : await Integrations.UpdateChannelAsync(channel);

            Toasts.Success($"{saved.Name} — {MsgSaved}");

            await LoadChannelsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the notification channel: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task DeleteChannelAsync()
    {
        if (SelectedChannel == null) return;

        try
        {
            await Integrations.DeleteChannelAsync(SelectedChannel.Id);
            Toasts.Success(MsgDeleted);
            NewChannel();
            await LoadChannelsAsync();
        }
        catch (Exception ex)
        {
            // A channel other channels fall back to is refused with a reason worth showing.
            Logger.Error("Could not delete the notification channel: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task TestChannelAsync()
    {
        if (SelectedChannel == null) return;

        try
        {
            var result = await Integrations.TestChannelAsync(SelectedChannel.Id);

            if (result.Success) Toasts.Success(result.Message);
            else Toasts.Error(result.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not test the notification channel: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    #endregion

    #region 4.1.3 SUBSCRIPTION METHODS

    private async Task LoadSubscriptionsAsync()
    {
        try
        {
            var events = await Integrations.GetNotificationEventsAsync();
            NotificationEvents.Clear();
            foreach (var descriptor in events) NotificationEvents.Add(descriptor);

            var subscriptions = await Integrations.GetSubscriptionsAsync();
            Subscriptions.Clear();
            foreach (var subscription in subscriptions) Subscriptions.Add(subscription);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the notification subscriptions: {Message}", ex.Message);
        }
    }

    private async Task LoadDeliveriesAsync()
    {
        try
        {
            var deliveries = await Integrations.GetDeliveriesAsync();
            Deliveries.Clear();
            foreach (var delivery in deliveries) Deliveries.Add(delivery);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the notification delivery log: {Message}", ex.Message);
        }
    }

    private async Task AddSubscriptionAsync()
    {
        if (SubscriptionChannelId == 0)
        {
            Toasts.Warning(Localizer["ChannelRequiredMSG"]);
            return;
        }

        try
        {
            await Integrations.CreateSubscriptionAsync(new NotificationSubscription
            {
                EventType = SubscriptionEvent,
                ChannelId = SubscriptionChannelId,
                MinSeverity = SubscriptionMinSeverity,
                DigestWindowMinutes = SubscriptionDigestMinutes,
                Enabled = true
            });

            Toasts.Success(Localizer["SubscriptionCreatedMSG"]);

            await LoadSubscriptionsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not create the subscription: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task DeleteSubscriptionAsync()
    {
        if (SelectedSubscription == null) return;

        try
        {
            await Integrations.DeleteSubscriptionAsync(SelectedSubscription.Id);
            Toasts.Success(MsgDeleted);
            await LoadSubscriptionsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not delete the subscription: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task RequeueDeliveryAsync()
    {
        if (SelectedDelivery == null) return;

        try
        {
            await Integrations.RequeueDeliveryAsync(SelectedDelivery.Id);
            Toasts.Success(Localizer["DeliveryRequeuedMSG"]);
            await LoadDeliveriesAsync();
        }
        catch (Exception ex)
        {
            // A delivery that already went out is refused, so the button cannot duplicate an alert.
            Logger.Error("Could not requeue the delivery: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    #endregion

    #region 4.2 ISSUE TRACKER METHODS

    private static IssueTrackerConnection NewIssueTracker() => new()
    {
        Provider = IssueTrackerProviderKind.Jira,
        BaseUrl = "",
        ProjectKey = "",
        Name = "",
        Enabled = true,
        PushFindingUpdates = true,
        PollIntervalMinutes = 15
    };

    private async Task LoadIssueTrackersAsync()
    {
        try
        {
            var providers = await Integrations.GetIssueTrackerProvidersAsync();
            IssueTrackerProviders.Clear();
            foreach (var provider in providers) IssueTrackerProviders.Add(provider);

            // Clearing the collection blanks the ComboBox; the draft keeps its provider, so the control
            // has to be told to re-read it.
            this.RaisePropertyChanged(nameof(SelectedIssueTrackerProvider));

            var connections = await Integrations.GetIssueTrackersAsync();
            IssueTrackers.Clear();
            foreach (var connection in connections) IssueTrackers.Add(connection);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the issue-tracker connections: {Message}", ex.Message);
        }
    }

    private async Task LoadStatusMappingsAsync(int connectionId)
    {
        try
        {
            var mappings = await Integrations.GetStatusMappingsAsync(connectionId);

            StatusMappings.Clear();

            foreach (var mapping in mappings)
                StatusMappings.Add(new IssueStatusMapping
                {
                    Id = mapping.Id,
                    ConnectionId = connectionId,
                    ExternalStatus = mapping.ExternalStatus,
                    Action = mapping.Action,
                    OutboundTransition = mapping.OutboundTransition
                });
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the status mappings: {Message}", ex.Message);
        }
    }

    private async Task LoadConflictsAsync()
    {
        try
        {
            var conflicts = await Integrations.GetIssueSyncConflictsAsync();
            SyncConflicts.Clear();
            foreach (var conflict in conflicts) SyncConflicts.Add(conflict);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the issue-sync conflicts: {Message}", ex.Message);
        }
    }

    private void AddStatusMapping()
    {
        if (SelectedIssueTracker == null) return;

        var mapping = new IssueStatusMapping
        {
            ConnectionId = SelectedIssueTracker.Id,
            ExternalStatus = "",
            Action = IssueSyncAction.None
        };

        StatusMappings.Add(mapping);
        SelectedStatusMapping = mapping;
    }

    private void RemoveStatusMapping()
    {
        if (SelectedStatusMapping == null) return;

        StatusMappings.Remove(SelectedStatusMapping);
        SelectedStatusMapping = null;
    }

    /// <summary>
    /// Saves the mapping table wholesale, which is what the server's endpoint does.
    ///
    /// Wholesale rather than per row because the mapping is edited as a table and a partial save
    /// leaves a half-configured mapping applying to live findings — the same reasoning the endpoint
    /// was written with in 4.2.1.
    /// </summary>
    private async Task SaveStatusMappingsAsync()
    {
        if (SelectedIssueTracker == null) return;

        // Refused here rather than at the server, because the server's unique index would answer with
        // a database error rather than a sentence: two rows mapping the same status is a configuration
        // whose behaviour depends on row order.
        var duplicate = StatusMappings
            .GroupBy(m => (m.ExternalStatus ?? "").Trim().ToLowerInvariant())
            .FirstOrDefault(g => g.Count() > 1);

        if (duplicate != null)
        {
            Toasts.Error(string.Format(Localizer["DuplicateStatusMappingMSG"], duplicate.Key));
            return;
        }

        if (StatusMappings.Any(m => string.IsNullOrWhiteSpace(m.ExternalStatus)))
        {
            Toasts.Error(Localizer["EmptyStatusMappingMSG"]);
            return;
        }

        try
        {
            var saved = await Integrations.SetStatusMappingsAsync(SelectedIssueTracker.Id,
                StatusMappings.ToList());

            StatusMappings.Clear();

            foreach (var mapping in saved)
                StatusMappings.Add(new IssueStatusMapping
                {
                    Id = mapping.Id,
                    ConnectionId = SelectedIssueTracker.Id,
                    ExternalStatus = mapping.ExternalStatus,
                    Action = mapping.Action,
                    OutboundTransition = mapping.OutboundTransition
                });

            Toasts.Success(MsgSaved);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the status mappings: {Message}", ex.Message);
            Toasts.Error(ExplainError(ex));
        }
    }

    /// <summary>
    /// Fills the status picker from the connection's Jira project.
    ///
    /// Jira only: the other three providers have no status endpoint to read — GitHub and GitLab have
    /// two states, and Azure DevOps' are per work-item type. For those the column stays free text,
    /// which is what their vocabulary actually is.
    /// </summary>
    private async Task LoadStatusesFromJiraAsync()
    {
        if (SelectedIssueTracker is not { Provider: IssueTrackerProviderKind.Jira } connection) return;

        try
        {
            var statuses = await Integrations.GetJiraStatusesAsync(connection.Id);

            ExternalStatusOptions.Clear();
            foreach (var status in statuses) ExternalStatusOptions.Add(status);

            if (ExternalStatusOptions.Count == 0) Toasts.Error(Localizer["NoJiraStatusesMSG"]);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read the Jira statuses: {Message}", ex.Message);
            Toasts.Error(ExplainError(ex));
        }
    }

    /// <summary>
    /// Renders the connection's templates against a real finding, without creating anything.
    ///
    /// The preview reads the *saved* connection, so an unsaved template edit is not what it shows.
    /// Rather than silently previewing the old text, the draft is saved first when it differs — the
    /// alternative is an operator tweaking a placeholder, pressing Preview, seeing no change and
    /// concluding the placeholder is wrong.
    /// </summary>
    private async Task PreviewTemplateAsync()
    {
        if (SelectedIssueTracker == null)
        {
            Toasts.Error(Localizer["SelectAConnectionFirstMSG"]);
            return;
        }

        if (PreviewFindingId <= 0)
        {
            Toasts.Error(Localizer["PreviewNeedsAFindingMSG"]);
            return;
        }

        try
        {
            // The four fields the rendered draft is built from. Anything else on the form cannot
            // change what the preview shows.
            if (IssueTemplateDraft.AnyChanged(
                    (SelectedIssueTracker.TitleTemplate, IssueTrackerDraft.TitleTemplate),
                    (SelectedIssueTracker.DescriptionTemplate, IssueTrackerDraft.DescriptionTemplate),
                    (SelectedIssueTracker.PriorityMappingJson, IssueTrackerDraft.PriorityMappingJson),
                    (SelectedIssueTracker.DefaultLabels, IssueTrackerDraft.DefaultLabels)))
            {
                await Integrations.UpdateIssueTrackerAsync(IssueTrackerDraft, null, null);
                await LoadIssueTrackersAsync();
            }

            var draft = await Integrations.PreviewIssueAsync(SelectedIssueTracker.Id, PreviewFindingId);

            PreviewTitle = draft.Title;
            PreviewBody = draft.Description;
            // Shown beside the rendered text because the priority is the other half of the mapping and
            // the only way to check the severity mapping without filing a ticket.
            PreviewPriority = draft.Priority ?? Localizer["ProjectDefault"];
            HasPreview = true;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not render the issue template preview: {Message}", ex.Message);
            Toasts.Error(ExplainError(ex));
            HasPreview = false;
        }
    }

    private void NewIssueTrackerDraft()
    {
        SelectedIssueTracker = null;
        IssueTrackerDraft = NewIssueTracker();
        IssueTrackerToken = "";
        IssueTrackerWebhookSecret = "";
        IssueTrackerTokenVault.Reset();
        IssueTrackerWebhookSecretVault.Reset();
        this.RaisePropertyChanged(nameof(IssueTrackerDraft));
        this.RaisePropertyChanged(nameof(SelectedIssueTrackerProvider));
        StatusMappings.Clear();
        ExternalStatusOptions.Clear();
        SelectedStatusMapping = null;
        ClearPreview();
        _ = Jira.LoadAsync(0, IssueTrackerProviderKind.Jira);
    }

    /// <summary>
    /// Drops a rendered preview.
    ///
    /// Called when the selection changes, because a preview left on screen from the previous
    /// connection reads as this connection's — which is worse than no preview at all.
    /// </summary>
    private void ClearPreview()
    {
        PreviewTitle = "";
        PreviewBody = "";
        PreviewPriority = "";
        HasPreview = false;
    }

    private void LoadIssueTrackerEditor(IssueTrackerConnectionView? connection)
    {
        if (connection == null) return;

        IssueTrackerDraft = new IssueTrackerConnection
        {
            Id = connection.Id,
            Name = connection.Name,
            Provider = connection.Provider,
            BaseUrl = connection.BaseUrl,
            ProjectKey = connection.ProjectKey,
            IssueType = connection.IssueType,
            AuthUser = connection.AuthUser,
            PriorityMappingJson = connection.PriorityMappingJson,
            TitleTemplate = connection.TitleTemplate,
            DescriptionTemplate = connection.DescriptionTemplate,
            DefaultLabels = connection.DefaultLabels,
            EntityId = connection.EntityId,
            Enabled = connection.Enabled,
            AutoCreateMinSeverity = connection.AutoCreateMinSeverity,
            PushFindingUpdates = connection.PushFindingUpdates,
            PollIntervalMinutes = connection.PollIntervalMinutes
        };

        IssueTrackerToken = "";
        IssueTrackerWebhookSecret = "";
        IssueTrackerTokenVault.LoadFrom(connection.TokenVaultReference);
        IssueTrackerWebhookSecretVault.LoadFrom(connection.WebhookSecretVaultReference);

        this.RaisePropertyChanged(nameof(IssueTrackerDraft));
        this.RaisePropertyChanged(nameof(SelectedIssueTrackerProvider));
    }

    private async Task SaveIssueTrackerAsync()
    {
        try
        {
            // Empty means unchanged, which is what lets the form round-trip without the client ever
            // holding the stored token.
            var token = VaultAwareSecret(VaultFieldKeys.IssueTrackerToken, IssueTrackerToken);
            var secret = VaultAwareSecret(VaultFieldKeys.IssueTrackerWebhookSecret,
                IssueTrackerWebhookSecret);

            var saved = IssueTrackerDraft.Id == 0
                ? await Integrations.CreateIssueTrackerAsync(IssueTrackerDraft, token, secret)
                : await Integrations.UpdateIssueTrackerAsync(IssueTrackerDraft, token, secret);

            Toasts.Success($"{saved.Name} — {MsgSaved}");

            await LoadIssueTrackersAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the issue-tracker connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task DeleteIssueTrackerAsync()
    {
        if (SelectedIssueTracker == null) return;

        try
        {
            await Integrations.DeleteIssueTrackerAsync(SelectedIssueTracker.Id);
            Toasts.Success(MsgDeleted);
            NewIssueTrackerDraft();
            await LoadIssueTrackersAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not delete the issue-tracker connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task TestIssueTrackerAsync()
    {
        if (SelectedIssueTracker == null) return;

        try
        {
            var result = await Integrations.TestIssueTrackerAsync(SelectedIssueTracker.Id);

            if (result.Success) Toasts.Success(result.Message);
            else Toasts.Error(result.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not test the issue-tracker connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task SyncIssueTrackerAsync()
    {
        if (SelectedIssueTracker == null) return;

        Toasts.Info(string.Format(Localizer["IssueSyncStartedMSG"], SelectedIssueTracker.Name));

        await WithBusyAsync(async () =>
        {
            try
            {
                var result = await Integrations.SyncIssueTrackerAsync(SelectedIssueTracker.Id);

                Toasts.Info(string.Format(Localizer["IssueSyncFinishedMSG"],
                    result.Examined, result.Applied, result.Conflicts));

                await LoadConflictsAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Could not synchronize the issue-tracker connection: {Message}", ex.Message);
                Toasts.Error(ex.Message);
            }
        });
    }

    private async Task ResolveConflictAsync()
    {
        if (SelectedConflict == null) return;

        try
        {
            await Integrations.ResolveIssueSyncConflictAsync(SelectedConflict.Id);
            Toasts.Success(Localizer["ConflictResolvedMSG"]);
            await LoadConflictsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not resolve the sync conflict: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    #endregion

    #region 4.3 ENTERPRISE AUTHENTICATION METHODS

    private static IdentityProvider NewIdentityProvider() => new()
    {
        Name = "",
        Protocol = IdentityProviderProtocol.Oidc,
        Enabled = true,
        RequireSignedAssertions = true,
        ClockSkewSeconds = 120
    };

    private async Task LoadIdentityProvidersAsync()
    {
        try
        {
            var providers = await Integrations.GetIdentityProvidersAsync();
            IdentityProviders.Clear();
            foreach (var provider in providers) IdentityProviders.Add(provider);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the identity providers: {Message}", ex.Message);
        }
    }

    private async Task LoadScimAsync()
    {
        try
        {
            var tokens = await Integrations.GetScimTokensAsync();
            ScimTokens.Clear();
            foreach (var token in tokens) ScimTokens.Add(token);

            var log = await Integrations.GetScimLogAsync();
            ScimLog.Clear();
            foreach (var entry in log) ScimLog.Add(entry);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the SCIM configuration: {Message}", ex.Message);
        }
    }

    private void NewIdentityProviderDraft()
    {
        SelectedIdentityProvider = null;
        IdentityProviderDraft = NewIdentityProvider();
        IdentityProviderClientSecret = "";
        IdentityProviderClientSecretVault.Reset();
        this.RaisePropertyChanged(nameof(IdentityProviderDraft));
    }

    private void LoadIdentityProviderEditor(IdentityProviderView? provider)
    {
        if (provider == null) return;

        IdentityProviderDraft = new IdentityProvider
        {
            Id = provider.Id,
            Name = provider.Name,
            Protocol = provider.Protocol,
            Enabled = provider.Enabled,
            Authority = provider.Authority,
            ClientId = provider.ClientId,
            Scopes = provider.Scopes,
            MetadataUrl = provider.MetadataUrl,
            EntityIdValue = provider.EntityIdValue,
            AssertionConsumerServiceUrl = provider.AssertionConsumerServiceUrl,
            RequireSignedAssertions = provider.RequireSignedAssertions,
            ClockSkewSeconds = provider.ClockSkewSeconds,
            SupportsSingleLogout = provider.SupportsSingleLogout,
            JitProvisioning = provider.JitProvisioning,
            DefaultRoleId = provider.DefaultRoleId,
            DefaultEntityId = provider.DefaultEntityId,
            ClaimMappingJson = System.Text.Json.JsonSerializer.Serialize(provider.ClaimMapping),
            GroupMappingJson = System.Text.Json.JsonSerializer.Serialize(provider.GroupMapping)
        };

        IdentityProviderClientSecret = "";
        IdentityProviderClientSecretVault.LoadFrom(provider.ClientSecretVaultReference);

        this.RaisePropertyChanged(nameof(IdentityProviderDraft));
    }

    private async Task SaveIdentityProviderAsync()
    {
        try
        {
            var secret = VaultAwareSecret(VaultFieldKeys.IdentityProviderClientSecret,
                IdentityProviderClientSecret);

            var saved = IdentityProviderDraft.Id == 0
                ? await Integrations.CreateIdentityProviderAsync(IdentityProviderDraft, secret)
                : await Integrations.UpdateIdentityProviderAsync(IdentityProviderDraft, secret);

            Toasts.Success($"{saved.Name} — {MsgSaved}");

            await LoadIdentityProvidersAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the identity provider: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task DeleteIdentityProviderAsync()
    {
        if (SelectedIdentityProvider == null) return;

        try
        {
            await Integrations.DeleteIdentityProviderAsync(SelectedIdentityProvider.Id);
            Toasts.Success(MsgDeleted);
            NewIdentityProviderDraft();
            await LoadIdentityProvidersAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not delete the identity provider: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task TestIdentityProviderAsync()
    {
        if (SelectedIdentityProvider == null) return;

        try
        {
            var result = await Integrations.TestIdentityProviderAsync(SelectedIdentityProvider.Id);

            if (result.Success) Toasts.Success(result.Message);
            else Toasts.Error(result.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not test the identity provider: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task IssueScimTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(ScimTokenName))
        {
            Toasts.Warning(Localizer["NameRequiredMSG"]);
            return;
        }

        try
        {
            var issued = await Integrations.IssueScimTokenAsync(ScimTokenName.Trim(),
                SelectedIdentityProvider?.Id);

            // Shown once, in the view, with the warning beside it. The server keeps only a hash.
            IssuedSecret = issued.Secret ?? "";

            ScimTokenName = "";

            await LoadScimAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not issue the SCIM token: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task RevokeScimTokenAsync()
    {
        if (SelectedScimToken == null) return;

        try
        {
            await Integrations.RevokeScimTokenAsync(SelectedScimToken.Id);
            Toasts.Success(Localizer["ScimTokenRevokedMSG"]);
            await LoadScimAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not revoke the SCIM token: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    #endregion

    #region 4.4 / 4.5 POSTURE PROVIDER METHODS

    private static TrendMicroConnection NewTrendMicro() => new()
    {
        Name = "",
        Region = "us",
        BaseUrl = "",
        Enabled = true,
        SyncIntervalHours = 24,
        SyncVulnerabilities = true,
        SyncRiskScores = true
    };

    private static SecurityScorecardConnection NewScorecard() => new()
    {
        Name = "",
        Domain = "",
        BaseUrl = "https://api.securityscorecard.io",
        Enabled = true,
        SyncIntervalHours = 24,
        SyncVulnerabilities = true,
        SyncIssues = true
    };

    private async Task LoadPostureProvidersAsync()
    {
        try
        {
            var regions = await Integrations.GetTrendMicroRegionsAsync();
            TrendMicroRegions.Clear();
            foreach (var region in regions.Keys.OrderBy(r => r, StringComparer.OrdinalIgnoreCase))
                TrendMicroRegions.Add(region);

            // Clearing the collection makes the ComboBox drop its selection. The draft's region is
            // protected from that by SelectedTrendMicroRegion, but the control still has to be told to
            // re-read it, or the field shows empty next to a draft that has one.
            this.RaisePropertyChanged(nameof(SelectedTrendMicroRegion));

            var trendMicro = await Integrations.GetTrendMicroConnectionsAsync();
            TrendMicroConnections.Clear();
            foreach (var connection in trendMicro) TrendMicroConnections.Add(connection);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the Vision One connections: {Message}", ex.Message);
        }

        try
        {
            var scorecards = await Integrations.GetSecurityScorecardConnectionsAsync();
            ScorecardConnections.Clear();
            foreach (var connection in scorecards) ScorecardConnections.Add(connection);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the SecurityScorecard connections: {Message}", ex.Message);
        }

        await LoadSyncLogAsync();
    }

    private async Task LoadSyncLogAsync()
    {
        try
        {
            var trendMicro = await Integrations.GetTrendMicroLogAsync(25);
            var scorecard = await Integrations.GetSecurityScorecardLogAsync(25);

            SyncLog.Clear();

            // Interleaved by start time so the log reads as one history rather than two lists.
            foreach (var entry in trendMicro.Concat(scorecard).OrderByDescending(l => l.StartedAt))
                SyncLog.Add(entry);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the integration sync log: {Message}", ex.Message);
        }
    }

    private async Task LoadScorecardHistoryAsync(int connectionId)
    {
        try
        {
            var history = await Integrations.GetSecurityScorecardHistoryAsync(connectionId);
            ScorecardHistory.Clear();
            foreach (var row in history) ScorecardHistory.Add(row);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the factor history: {Message}", ex.Message);
        }
    }

    private void NewTrendMicroDraft()
    {
        SelectedTrendMicro = null;
        TrendMicroDraft = NewTrendMicro();
        TrendMicroApiKey = "";
        TrendMicroApiKeyVault.Reset();
        this.RaisePropertyChanged(nameof(TrendMicroDraft));
        this.RaisePropertyChanged(nameof(SelectedTrendMicroRegion));
    }

    private void LoadTrendMicroEditor(TrendMicroConnectionView? connection)
    {
        if (connection == null) return;

        TrendMicroDraft = new TrendMicroConnection
        {
            Id = connection.Id,
            Name = connection.Name,
            Region = connection.Region,
            BaseUrl = connection.BaseUrl,
            EntityId = connection.EntityId,
            Enabled = connection.Enabled,
            SyncIntervalHours = connection.SyncIntervalHours,
            SyncVulnerabilities = connection.SyncVulnerabilities,
            SyncRiskScores = connection.SyncRiskScores,
            VirtualPatchClosesFinding = connection.VirtualPatchClosesFinding,
            PushExemptions = connection.PushExemptions
        };

        TrendMicroApiKey = "";
        TrendMicroApiKeyVault.LoadFrom(connection.ApiKeyVaultReference);

        this.RaisePropertyChanged(nameof(TrendMicroDraft));
        this.RaisePropertyChanged(nameof(SelectedTrendMicroRegion));
    }

    private void NewScorecardDraft()
    {
        SelectedScorecard = null;
        ScorecardDraft = NewScorecard();
        ScorecardApiToken = "";
        ScorecardApiTokenVault.Reset();
        this.RaisePropertyChanged(nameof(ScorecardDraft));
        ScorecardHistory.Clear();
    }

    private void LoadScorecardEditor(SecurityScorecardConnectionView? connection)
    {
        if (connection == null) return;

        ScorecardDraft = new SecurityScorecardConnection
        {
            Id = connection.Id,
            Name = connection.Name,
            Domain = connection.Domain,
            BaseUrl = connection.BaseUrl,
            EntityId = connection.EntityId,
            Enabled = connection.Enabled,
            SyncIntervalHours = connection.SyncIntervalHours,
            SyncVulnerabilities = connection.SyncVulnerabilities,
            SyncIssues = connection.SyncIssues
        };

        ScorecardApiToken = "";
        ScorecardApiTokenVault.LoadFrom(connection.ApiTokenVaultReference);

        this.RaisePropertyChanged(nameof(ScorecardDraft));
    }

    private async Task SaveTrendMicroAsync()
    {
        try
        {
            var apiKey = VaultAwareSecret(VaultFieldKeys.TrendMicroApiKey, TrendMicroApiKey);

            var saved = TrendMicroDraft.Id == 0
                ? await Integrations.CreateTrendMicroConnectionAsync(TrendMicroDraft, apiKey)
                : await Integrations.UpdateTrendMicroConnectionAsync(TrendMicroDraft, apiKey);

            Toasts.Success($"{saved.Name} — {MsgSaved}");

            await LoadPostureProvidersAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the Vision One connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task DeleteTrendMicroAsync()
    {
        if (SelectedTrendMicro == null) return;

        try
        {
            await Integrations.DeleteTrendMicroConnectionAsync(SelectedTrendMicro.Id);
            Toasts.Success(MsgDeleted);
            NewTrendMicroDraft();
            await LoadPostureProvidersAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not delete the Vision One connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task TestTrendMicroAsync()
    {
        if (SelectedTrendMicro == null) return;

        try
        {
            var result = await Integrations.TestTrendMicroConnectionAsync(SelectedTrendMicro.Id);

            if (result.Success) Toasts.Success(result.Message);
            else Toasts.Error(result.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not test the Vision One connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task SyncTrendMicroAsync()
    {
        if (SelectedTrendMicro == null) return;

        // Announced before the call, not after. A posture sync is minutes long, and the busy indicator
        // alone does not say what is busy — the finish toast used to be the first and only sign that a
        // click had done anything at all.
        Toasts.Info(string.Format(Localizer["PostureSyncStartedMSG"], SelectedTrendMicro.Name));

        await WithBusyAsync(async () =>
        {
            try
            {
                var result = await Integrations.SyncTrendMicroConnectionAsync(SelectedTrendMicro.Id);

                Toasts.Info(string.Format(Localizer["PostureSyncFinishedMSG"],
                    result.HostsCreated, result.HostsUpdated, result.FindingsCreated,
                    result.FindingsUpdated));

                await LoadPostureProvidersAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Could not synchronize the Vision One connection: {Message}", ex.Message);
                Toasts.Error(ex.Message);
            }
        });
    }

    private async Task SaveScorecardAsync()
    {
        try
        {
            var apiToken = VaultAwareSecret(VaultFieldKeys.ScorecardApiToken, ScorecardApiToken);

            var saved = ScorecardDraft.Id == 0
                ? await Integrations.CreateSecurityScorecardConnectionAsync(ScorecardDraft, apiToken)
                : await Integrations.UpdateSecurityScorecardConnectionAsync(ScorecardDraft, apiToken);

            Toasts.Success($"{saved.Name} — {MsgSaved}");

            await LoadPostureProvidersAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the SecurityScorecard connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task DeleteScorecardAsync()
    {
        if (SelectedScorecard == null) return;

        try
        {
            await Integrations.DeleteSecurityScorecardConnectionAsync(SelectedScorecard.Id);
            Toasts.Success(MsgDeleted);
            NewScorecardDraft();
            await LoadPostureProvidersAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not delete the SecurityScorecard connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task TestScorecardAsync()
    {
        if (SelectedScorecard == null) return;

        try
        {
            var result = await Integrations.TestSecurityScorecardConnectionAsync(SelectedScorecard.Id);

            if (result.Success) Toasts.Success(result.Message);
            else Toasts.Error(result.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not test the SecurityScorecard connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task SyncScorecardAsync()
    {
        if (SelectedScorecard == null) return;

        Toasts.Info(string.Format(Localizer["ScorecardSyncStartedMSG"], SelectedScorecard.Name));

        await WithBusyAsync(async () =>
        {
            try
            {
                var result = await Integrations.SyncSecurityScorecardConnectionAsync(SelectedScorecard.Id);

                Toasts.Info(string.Format(Localizer["ScorecardSyncFinishedMSG"],
                    result.PostureRowsWritten, result.FindingsCreated,
                    result.CyberRiskIndex?.ToString("0.0") ?? "—"));

                await LoadScorecardHistoryAsync(SelectedScorecard.Id);
                await LoadPostureProvidersAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Could not synchronize the SecurityScorecard connection: {Message}",
                    ex.Message);
                Toasts.Error(ex.Message);
            }
        });
    }

    #endregion

    #region SECRET VAULT METHODS

    private static SecretVaultConnectionInput NewVault() => new()
    {
        Name = "",
        PluginName = "",
        BaseUrl = "",
        MachineId = "",
        AppId = "",
        IgnoreSslErrors = false,
        Enabled = true,
        CacheTtlMinutes = SecretVaultDefaults.CacheTtlMinutes
    };

    private async Task LoadSecretVaultsAsync()
    {
        try
        {
            var plugins = await Integrations.GetSecretVaultPluginsAsync();
            VaultPlugins.Clear();
            foreach (var plugin in plugins) VaultPlugins.Add(plugin);

            // Replacing the items makes the ComboBox drop its selection; the draft's plugin name is
            // protected by SelectedVaultPluginName, but the control still has to be told to re-read it.
            this.RaisePropertyChanged(nameof(SelectedVaultPluginName));
            this.RaisePropertyChanged(nameof(HasNoVaultPlugins));

            // The list the requirement is read from has just been replaced, so a hint rendered
            // before the plugins arrived is stale until this fires.
            this.RaisePropertyChanged(nameof(VaultRequiresMachineId));
            this.RaisePropertyChanged(nameof(StrVaultMachineIdHint));
            this.RaisePropertyChanged(nameof(VaultRequiresAppId));
            this.RaisePropertyChanged(nameof(StrVaultAppIdHint));

            var connections = await Integrations.GetSecretVaultConnectionsAsync();
            VaultConnections.Clear();
            foreach (var connection in connections) VaultConnections.Add(connection);

            // Asked of the server rather than inferred from the two lists above: "usable" also means
            // the connection's plugin is the one that is enabled, and the server is the only side that
            // knows that.
            VaultAvailable = await Integrations.IsSecretVaultAvailableAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the secret vault connections: {Message}", ex.Message);

            // Not merely a log: with this left true from a previous load, every picker button stays
            // visible and every click fails.
            VaultAvailable = false;
        }
    }

    private void NewVaultDraft()
    {
        SelectedVault = null;
        VaultDraft = NewVault();
        VaultApiKey = "";
        VaultUsageCount = 0;

        // A single installed plugin is the overwhelmingly common case, so pre-selecting it saves the
        // one interaction that has no decision in it.
        if (VaultPlugins.Count == 1) VaultDraft.PluginName = VaultPlugins[0].PluginName;

        this.RaisePropertyChanged(nameof(VaultDraft));
        this.RaisePropertyChanged(nameof(SelectedVaultPluginName));
        this.RaisePropertyChanged(nameof(VaultRequiresMachineId));
        this.RaisePropertyChanged(nameof(StrVaultMachineIdHint));
        this.RaisePropertyChanged(nameof(VaultRequiresAppId));
        this.RaisePropertyChanged(nameof(StrVaultAppIdHint));
    }

    private void LoadVaultEditor(SecretVaultConnectionView? connection)
    {
        if (connection == null) return;

        VaultDraft = new SecretVaultConnectionInput
        {
            Id = connection.Id,
            Name = connection.Name,
            PluginName = connection.PluginName,
            BaseUrl = connection.BaseUrl,
            MachineId = connection.MachineId ?? "",
            AppId = connection.AppId ?? "",
            IgnoreSslErrors = connection.IgnoreSslErrors,
            Enabled = connection.Enabled,
            CacheTtlMinutes = connection.CacheTtlMinutes
        };

        VaultApiKey = "";

        this.RaisePropertyChanged(nameof(VaultDraft));
        this.RaisePropertyChanged(nameof(SelectedVaultPluginName));
        this.RaisePropertyChanged(nameof(VaultRequiresMachineId));
        this.RaisePropertyChanged(nameof(StrVaultMachineIdHint));
        this.RaisePropertyChanged(nameof(VaultRequiresAppId));
        this.RaisePropertyChanged(nameof(StrVaultAppIdHint));

        _ = LoadVaultUsageAsync(connection.Id);
    }

    private async Task LoadVaultUsageAsync(int connectionId)
    {
        try
        {
            VaultUsageCount = await Integrations.GetSecretVaultUsageAsync(connectionId);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not count the references to vault connection {Id}: {Message}",
                connectionId, ex.Message);
            VaultUsageCount = 0;
        }
    }

    private async Task SaveVaultAsync()
    {
        try
        {
            var apiKey = string.IsNullOrWhiteSpace(VaultApiKey) ? null : VaultApiKey.Trim();

            var saved = VaultDraft.Id == 0
                ? await Integrations.CreateSecretVaultConnectionAsync(VaultDraft, apiKey)
                : await Integrations.UpdateSecretVaultConnectionAsync(VaultDraft, apiKey);

            Toasts.Success($"{saved.Name} — {MsgSaved}");

            await LoadSecretVaultsAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the vault connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task DeleteVaultAsync()
    {
        if (SelectedVault == null) return;

        try
        {
            await Integrations.DeleteSecretVaultConnectionAsync(SelectedVault.Id);
            Toasts.Success(MsgDeleted);
            NewVaultDraft();
            await LoadSecretVaultsAsync();
        }
        catch (Exception ex)
        {
            // The interesting case is the server refusing because fields still resolve through this
            // connection. That refusal names the count, so showing ex.Message is showing the reason.
            Logger.Error("Could not delete the vault connection: {Message}", ex.Message);
            Toasts.Error(ex.Message);
        }
    }

    private async Task TestVaultAsync()
    {
        if (SelectedVault == null) return;

        await WithBusyAsync(async () =>
        {
            try
            {
                var result = await Integrations.TestSecretVaultConnectionAsync(SelectedVault.Id);

                if (result.Success) Toasts.Success(result.Message);
                else Toasts.Error(result.Message);

                // The row carries the outcome, and the operator should see it recorded rather than
                // only as a toast that disappears.
                await LoadSecretVaultsAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Could not test the vault connection: {Message}", ex.Message);
                Toasts.Error(ex.Message);
            }
        });
    }

    /// <summary>
    /// Opens the picker for one credential field and records what came back.
    ///
    /// Nothing is saved here: binding a field marks it pending, and the connection's own Save button
    /// is what sends it. Writing straight through would mean picking a secret silently committed
    /// every other unsaved edit on the form as well.
    /// </summary>
    private async Task PickVaultSecretAsync(string fieldKey)
    {
        if (!VaultFields.TryGetValue(fieldKey, out var field))
        {
            Logger.Error("No vault field is registered under the key {Key}", fieldKey);
            return;
        }

        var parameter = new SecretVaultPickerParameter
        {
            FieldName = VaultFieldCaption(fieldKey),
            CurrentReference = field.EffectiveReference
        };

        var result = await Dialogs.ShowDialogAsync<SecretVaultPickerResult, SecretVaultPickerParameter>(
            nameof(SecretVaultPickerViewModel), parameter);

        if (result is not { Action: ResultActions.Ok } || string.IsNullOrWhiteSpace(result.Reference))
            return;

        field.Bind(result.Reference, result.DisplayName);

        ClearTypedSecret(fieldKey);
    }

    private void DetachVaultSecret(string fieldKey)
    {
        if (!VaultFields.TryGetValue(fieldKey, out var field)) return;

        field.Detach();
    }

    /// <summary>
    /// Blanks the text box behind a field that has just been bound to the vault.
    ///
    /// The state object already forgets its own copy, but the bound view-model property is what the
    /// operator sees and what the save path reads. Leaving a half-typed key in it would send that
    /// literal the moment they detached the field again.
    /// </summary>
    private void ClearTypedSecret(string fieldKey)
    {
        switch (fieldKey)
        {
            case VaultFieldKeys.TrendMicroApiKey: TrendMicroApiKey = ""; break;
            case VaultFieldKeys.ScorecardApiToken: ScorecardApiToken = ""; break;
            case VaultFieldKeys.IssueTrackerToken: IssueTrackerToken = ""; break;
            case VaultFieldKeys.IssueTrackerWebhookSecret: IssueTrackerWebhookSecret = ""; break;
            case VaultFieldKeys.IdentityProviderClientSecret: IdentityProviderClientSecret = ""; break;
            case VaultFieldKeys.ChannelWebhookUrl: ChannelWebhookUrl = ""; break;
            case VaultFieldKeys.ChannelSigningSecret: ChannelSigningSecret = ""; break;
        }
    }

    private string VaultFieldCaption(string fieldKey) => fieldKey switch
    {
        VaultFieldKeys.TrendMicroApiKey => StrTrendMicro + " — " + StrApiKey,
        VaultFieldKeys.ScorecardApiToken => StrSecurityScorecard + " — " + StrApiToken,
        VaultFieldKeys.IssueTrackerToken => StrIssueTrackers + " — " + StrApiToken,
        VaultFieldKeys.IssueTrackerWebhookSecret => StrIssueTrackers + " — " + StrWebhookSecret,
        VaultFieldKeys.IdentityProviderClientSecret => StrIdentityProviders + " — " + StrClientSecret,
        VaultFieldKeys.ChannelWebhookUrl => StrNotificationChannels + " — " + StrWebhookUrl,
        VaultFieldKeys.ChannelSigningSecret => StrNotificationChannels + " — " + StrSigningSecret,
        _ => fieldKey
    };

    /// <summary>
    /// The value to send for a credential field: the picked reference, the typed literal, or null for
    /// "leave the stored one alone".
    ///
    /// Every save path goes through here rather than through its own
    /// <c>string.IsNullOrWhiteSpace(...) ? null : ...</c>, because the vault branch has to come first
    /// and seven copies of that ordering is seven chances to get it backwards.
    /// </summary>
    private string? VaultAwareSecret(string fieldKey, string typed)
    {
        if (!VaultFields.TryGetValue(fieldKey, out var field))
            return string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();

        field.TypedValue = typed;
        return field.ValueToSend();
    }

    #endregion
}

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
    public string StrTrendMicro { get; } = Localizer["TrendMicroVisionOne"];
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
            WebhookUrl = string.IsNullOrWhiteSpace(ChannelWebhookUrl)
                ? SelectedChannel == null ? null : ChannelConfiguration.RedactedPlaceholder
                : ChannelWebhookUrl.Trim(),
            SigningSecret = string.IsNullOrWhiteSpace(ChannelSigningSecret)
                ? SelectedChannel == null ? null : ChannelConfiguration.RedactedPlaceholder
                : ChannelSigningSecret.Trim()
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
        this.RaisePropertyChanged(nameof(IssueTrackerDraft));
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

        this.RaisePropertyChanged(nameof(IssueTrackerDraft));
    }

    private async Task SaveIssueTrackerAsync()
    {
        try
        {
            // Empty means unchanged, which is what lets the form round-trip without the client ever
            // holding the stored token.
            var token = string.IsNullOrWhiteSpace(IssueTrackerToken) ? null : IssueTrackerToken.Trim();
            var secret = string.IsNullOrWhiteSpace(IssueTrackerWebhookSecret)
                ? null
                : IssueTrackerWebhookSecret.Trim();

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

        this.RaisePropertyChanged(nameof(IdentityProviderDraft));
    }

    private async Task SaveIdentityProviderAsync()
    {
        try
        {
            var secret = string.IsNullOrWhiteSpace(IdentityProviderClientSecret)
                ? null
                : IdentityProviderClientSecret.Trim();

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
        this.RaisePropertyChanged(nameof(TrendMicroDraft));
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

        this.RaisePropertyChanged(nameof(TrendMicroDraft));
    }

    private void NewScorecardDraft()
    {
        SelectedScorecard = null;
        ScorecardDraft = NewScorecard();
        ScorecardApiToken = "";
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

        this.RaisePropertyChanged(nameof(ScorecardDraft));
    }

    private async Task SaveTrendMicroAsync()
    {
        try
        {
            var apiKey = string.IsNullOrWhiteSpace(TrendMicroApiKey) ? null : TrendMicroApiKey.Trim();

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
            var apiToken = string.IsNullOrWhiteSpace(ScorecardApiToken) ? null : ScorecardApiToken.Trim();

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
}

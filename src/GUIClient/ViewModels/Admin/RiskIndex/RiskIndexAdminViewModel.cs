using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.RiskIndex;
using ReactiveUI;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>
/// The Risk index administration pane (S40 §6.0; M55): six tabs — Profiles, Draft, Preview and submission, Approval, Sources
/// and freshness, Risk context — each enabled, read-only or closed by <see cref="RiskIndexAdminAccess"/>. A tab is built only
/// when the user may open it, so a user without a permission never fires the requests it would refuse with 403. The Draft
/// and Sources tabs edit one <see cref="RiskProfileDraftSession"/>.
/// </summary>
public class RiskIndexAdminViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrProfilesTab { get; } = Localizer["RiskAdminTabProfiles"];
    public string StrDraftTab { get; } = Localizer["RiskAdminTabDraft"];
    public string StrPreviewTab { get; } = Localizer["RiskAdminTabPreview"];
    public string StrApprovalTab { get; } = Localizer["RiskAdminTabApproval"];
    public string StrSourcesTab { get; } = Localizer["RiskAdminTabSources"];
    public string StrRiskContextTab { get; } = Localizer["RiskAdminTabRiskContext"];

    #endregion

    public RiskIndexAdminViewModel() : this(RiskIndexAdminRights.From(GetService<IAuthenticationService>().AuthenticatedUserInfo))
    {
    }

    public RiskIndexAdminViewModel(RiskIndexAdminRights rights)
    {
        Rights = rights;
        ProfilesAccess = RiskIndexAdminAccess.For(RiskIndexAdminTab.Profiles, rights);
        DraftAccess = RiskIndexAdminAccess.For(RiskIndexAdminTab.Draft, rights);
        PreviewAccess = RiskIndexAdminAccess.For(RiskIndexAdminTab.Preview, rights);
        ApprovalAccess = RiskIndexAdminAccess.For(RiskIndexAdminTab.Approval, rights);
        SourcesAccess = RiskIndexAdminAccess.For(RiskIndexAdminTab.Sources, rights);
        RiskContextAccess = RiskIndexAdminAccess.For(RiskIndexAdminTab.RiskContext, rights);
        _selectedTabIndex = (int)RiskIndexAdminAccess.FirstOpen(rights);

        var readService = GetService<IRiskIndexService>();
        var configService = GetService<IRiskIndexConfigService>();
        if (rights.View)
        {
            Session = new RiskProfileDraftSession(rights, readService, configService);
            if (ProfilesAccess.Enabled)
                ProfilesVM = new RiskProfilesViewModel(Session, readService, configService, () => SelectTab(RiskIndexAdminTab.Draft));
            if (DraftAccess.Enabled)
                EditorVM = new RiskProfileEditorViewModel(Session, () => SelectTab(RiskIndexAdminTab.Preview));
            if (PreviewAccess.Enabled) PreviewVM = new RiskProfilePreviewViewModel(Session, configService);
            if (ApprovalAccess.Enabled)
                ApprovalVM = new RiskProfileApprovalViewModel(Session, readService, configService, GetService<IFilesService>());
            if (SourcesAccess.Enabled) SourcesVM = new RiskSourcesViewModel(Session);
        }

        if (RiskContextAccess.Enabled) RiskContextVM = new EntityRiskContextViewModel(rights, GetService<IEntitiesService>());
    }

    public RiskIndexAdminRights Rights { get; }

    public RiskProfileDraftSession? Session { get; }
    public RiskProfilesViewModel? ProfilesVM { get; }
    public RiskProfileEditorViewModel? EditorVM { get; }
    public RiskProfilePreviewViewModel? PreviewVM { get; }
    public RiskProfileApprovalViewModel? ApprovalVM { get; }
    public RiskSourcesViewModel? SourcesVM { get; }
    public EntityRiskContextViewModel? RiskContextVM { get; }

    public RiskIndexTabAccess ProfilesAccess { get; }
    public RiskIndexTabAccess DraftAccess { get; }
    public RiskIndexTabAccess PreviewAccess { get; }
    public RiskIndexTabAccess ApprovalAccess { get; }
    public RiskIndexTabAccess SourcesAccess { get; }
    public RiskIndexTabAccess RiskContextAccess { get; }

    public string StrProfilesDisabledReason => Reason(ProfilesAccess);
    public string StrDraftDisabledReason => Reason(DraftAccess);
    public string StrPreviewDisabledReason => Reason(PreviewAccess);
    public string StrApprovalDisabledReason => Reason(ApprovalAccess);
    public string StrSourcesDisabledReason => Reason(SourcesAccess);
    public string StrRiskContextDisabledReason => Reason(RiskContextAccess);

    private int _selectedTabIndex;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
    }

    private bool _initialized;

    /// <summary>Loads the tabs once, when the pane is first shown.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;
        if (Session is not null) await Session.LoadAsync();
        if (RiskContextVM is not null) await RiskContextVM.LoadAsync();
    }

    public void SelectTab(RiskIndexAdminTab tab)
    {
        if (RiskIndexAdminAccess.For(tab, Rights).Enabled) SelectedTabIndex = (int)tab;
    }

    private static string Reason(RiskIndexTabAccess access) =>
        access.DisabledReasonKey is { } key ? RiskAdminText.Text(key) : "";
}

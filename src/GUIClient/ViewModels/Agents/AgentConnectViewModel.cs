using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ClientServices.Interfaces;
using ClientServices.Services;
using GUIClient.Agents;
using GUIClient.Tools;
using ReactiveUI;
using Serilog;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Agents;

/// <summary>One AI client as the "Connect an agent" window lists it.</summary>
public class AgentClientRow
{
    public required AgentClientStatus Status { get; init; }
    public string Name { get; init; } = "";
    public string State { get; init; } = "";
    public string Detail { get; init; } = "";
    public string ConfigPath { get; init; } = "";
}

/// <summary>
/// "Connect an agent" (M65, T363–T365, S55): the AI clients on this computer, each with its configuration file and
/// state; Register and Unregister prepare a change and show its diff, and only Apply writes it. All decisions are in
/// <see cref="AgentClientRegistrar"/> (Core, tested headless); this class only presents them.
/// </summary>
public class AgentConnectViewModel : ViewModelBase
{
    public string StrTitle { get; } = Localizer["AgentConnectTitle"];
    public string StrIntro { get; } = Localizer["AgentConnectIntroMSG"];
    public string StrRegister { get; } = Localizer["AgentConnectRegister"];
    public string StrUnregister { get; } = Localizer["AgentConnectUnregister"];
    public string StrChooseFile { get; } = Localizer["AgentConnectChooseFile"];
    public string StrRefresh { get; } = Localizer["AgentConnectRefresh"];
    public string StrClient { get; } = Localizer["AgentConnectColumnClient"];
    public string StrState { get; } = Localizer["AgentConnectColumnState"];
    public string StrDetail { get; } = Localizer["AgentConnectColumnDetail"];
    public string StrPath { get; } = Localizer["AgentConnectColumnPath"];
    public string StrPreviewHint { get; } = Localizer["AgentConnectPreviewHintMSG"];
    public string StrApply { get; } = Localizer["AgentConnectApply"];
    public string StrGuideCommand { get; } = Localizer["AgentConnectGuideCommand"];

    private readonly ILogger _log = Log.ForContext<AgentConnectViewModel>();
    private readonly AgentClientRegistrar? _registrar;
    private readonly AgentBridgeLaunch? _launch;
    private readonly Dictionary<string, string> _manualPaths = new(StringComparer.Ordinal);
    private AgentClientPlan? _plan;

    public ObservableCollection<AgentClientRow> Clients { get; } = new();

    private AgentClientRow? _selectedClient;
    public AgentClientRow? SelectedClient
    {
        get => _selectedClient;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedClient, value);
            UpdateSelection();
        }
    }

    private string _bridgeStatus = "";
    public string BridgeStatus
    {
        get => _bridgeStatus;
        set => this.RaiseAndSetIfChanged(ref _bridgeStatus, value);
    }

    private bool _isRegisterEnabled;
    public bool IsRegisterEnabled
    {
        get => _isRegisterEnabled;
        set => this.RaiseAndSetIfChanged(ref _isRegisterEnabled, value);
    }

    private bool _isUnregisterEnabled;
    public bool IsUnregisterEnabled
    {
        get => _isUnregisterEnabled;
        set => this.RaiseAndSetIfChanged(ref _isUnregisterEnabled, value);
    }

    private bool _isChooseFileEnabled;
    public bool IsChooseFileEnabled
    {
        get => _isChooseFileEnabled;
        set => this.RaiseAndSetIfChanged(ref _isChooseFileEnabled, value);
    }

    private bool _isPreviewVisible;
    public bool IsPreviewVisible
    {
        get => _isPreviewVisible;
        set => this.RaiseAndSetIfChanged(ref _isPreviewVisible, value);
    }

    private string _previewTitle = "";
    public string PreviewTitle
    {
        get => _previewTitle;
        set => this.RaiseAndSetIfChanged(ref _previewTitle, value);
    }

    private string _previewDiff = "";
    public string PreviewDiff
    {
        get => _previewDiff;
        set => this.RaiseAndSetIfChanged(ref _previewDiff, value);
    }

    private string _previewNote = "";
    public string PreviewNote
    {
        get => _previewNote;
        set => this.RaiseAndSetIfChanged(ref _previewNote, value);
    }

    private bool _isGuideVisible;
    public bool IsGuideVisible
    {
        get => _isGuideVisible;
        set => this.RaiseAndSetIfChanged(ref _isGuideVisible, value);
    }

    private string _guideTitle = "";
    public string GuideTitle
    {
        get => _guideTitle;
        set => this.RaiseAndSetIfChanged(ref _guideTitle, value);
    }

    private string _guideText = "";
    public string GuideText
    {
        get => _guideText;
        set => this.RaiseAndSetIfChanged(ref _guideText, value);
    }

    private string _guideCommand = "";
    public string GuideCommand
    {
        get => _guideCommand;
        set => this.RaiseAndSetIfChanged(ref _guideCommand, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRegisterClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtUnregisterClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtChooseFileClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRefreshClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtApplyClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    public AgentConnectViewModel()
    {
        BtRegisterClicked = ReactiveCommand.Create(() => Preview(AgentClientAction.Register));
        BtUnregisterClicked = ReactiveCommand.Create(() => Preview(AgentClientAction.Unregister));
        BtChooseFileClicked = ReactiveCommand.CreateFromTask(ChooseFileAsync);
        BtRefreshClicked = ReactiveCommand.Create(Refresh);
        BtApplyClicked = ReactiveCommand.Create(Apply);
        BtCancelClicked = ReactiveCommand.Create(ClosePreview);

        if (AgentPlatforms.Current is { } platform)
        {
            _registrar = new AgentClientRegistrar(AgentKnownFolders.FromSystem(), platform,
                Environment.GetEnvironmentVariable("PATH"), TimeProvider.System);
            _launch = LocateBridge(platform);
        }

        BridgeStatus = _launch is not null
            ? string.Format(Localizer["AgentConnectBridgeFound"], _launch.Command)
            : _registrar is null
                ? Localizer["AgentConnectStateUnsupported"]
                : Localizer["AgentConnectProblemBridgeNotFound"];
        Refresh();
    }

    /// <summary>The bridge beside NetRisk (or in the build tree), the running environment, and DOTNET_ROOT if shared.</summary>
    private AgentBridgeLaunch? LocateBridge(AgentPlatform platform)
    {
        if (AgentBridgeLocator.Find(AppContext.BaseDirectory, platform) is not { } bridge) return null;
        var environment = GetService<IEnvironmentService>() is EnvironmentService service ? service.Environment : "production";
        var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "";
        try
        {
            return AgentBridgeLaunch.Create(bridge, environment, AgentBridgeLocator.DotnetRootOf(runtime, platform));
        }
        catch (ArgumentException ex)
        {
            _log.Warning(ex, "The MCP bridge was found but cannot be registered");
            return null;
        }
    }

    private void Refresh()
    {
        if (_registrar is null) return;
        var selectedId = SelectedClient?.Status.Client.Id;
        Clients.Clear();
        foreach (var client in _registrar.Clients)
        {
            var status = _registrar.Status(client, _launch, ManualPath(client));
            Clients.Add(new AgentClientRow
            {
                Status = status,
                Name = Localizer[client.NameKey],
                State = Localizer[AgentClientTexts.StateKey(status.State)],
                Detail = DetailOf(status),
                ConfigPath = status.ConfigPath ?? ""
            });
        }
        SelectedClient = Clients.FirstOrDefault(c => c.Status.Client.Id == selectedId);
    }

    private static string DetailOf(AgentClientStatus status)
    {
        if (AgentClientTexts.ProblemKey(status.Problem) is { } key && status.Problem != AgentClientProblem.Guided)
            return Localizer[key];
        if (status.IsManualPath) return Localizer["AgentConnectManualPath"];
        return status.DetectedAt is { } at ? string.Format(Localizer["AgentConnectDetectedBy"], at) : "";
    }

    private string? ManualPath(AgentClientDefinition client) => _manualPaths.GetValueOrDefault(client.Id);

    private void UpdateSelection()
    {
        var row = SelectedClient;
        var previewing = IsPreviewVisible;
        IsRegisterEnabled = !previewing && _launch is not null && row?.Status.CanRegister == true;
        IsUnregisterEnabled = !previewing && row?.Status.CanUnregister == true;
        IsChooseFileEnabled = !previewing && row?.Status.Client.Support == AgentClientSupport.Automatic;

        IsGuideVisible = row?.Status.Client.GuideKey is not null;
        if (row?.Status.Client.GuideKey is not { } guide) return;
        GuideTitle = string.Format(Localizer["AgentConnectGuideTitle"], row.Name);
        GuideText = Localizer[guide];
        GuideCommand = _launch is null ? Localizer["AgentConnectProblemBridgeNotFound"] : LaunchText(_launch);
    }

    /// <summary>What a user types into a client that NetRisk does not configure. Field names are the clients' own.</summary>
    private static string LaunchText(AgentBridgeLaunch launch)
    {
        var text = new StringBuilder();
        text.Append("command: ").Append(launch.Command).Append('\n');
        text.Append("args: ").Append(string.Join(' ', launch.Args)).Append('\n');
        foreach (var (name, value) in launch.Env) text.Append("env: ").Append(name).Append('=').Append(value).Append('\n');
        return text.ToString();
    }

    /// <summary>Prepares the change and shows its diff. Nothing is written here.</summary>
    private void Preview(AgentClientAction action)
    {
        if (_registrar is null || SelectedClient is not { } row) return;

        var plan = _registrar.Plan(row.Status.Client, action, _launch, ManualPath(row.Status.Client));
        if (plan.Problem != AgentClientProblem.None)
        {
            Toasts.Warning(string.Format(Localizer["AgentConnectFailedMSG"], row.Name,
                Localizer[AgentClientTexts.ProblemKey(plan.Problem)!]));
            Refresh();
            return;
        }
        if (!plan.HasChanges)
        {
            Toasts.Info(string.Format(Localizer["AgentConnectNoChangeMSG"], row.Name));
            Refresh();
            return;
        }

        _plan = plan;
        PreviewTitle = string.Format(action == AgentClientAction.Register
            ? Localizer["AgentConnectPreviewRegisterTitle"]
            : Localizer["AgentConnectPreviewUnregisterTitle"], row.Name);
        PreviewDiff = plan.Diff;
        PreviewNote = plan.KeepsBackup
            ? string.Format(Localizer["AgentConnectPreviewBackupMSG"], Path.GetFileName(plan.ConfigPath))
            : Localizer["AgentConnectPreviewNewFileMSG"];
        IsPreviewVisible = true;
        UpdateSelection();
    }

    /// <summary>The user's explicit consent for this one client: writes exactly the previewed change, or nothing.</summary>
    private void Apply()
    {
        if (_registrar is null || _plan is not { } plan) return;

        var result = _registrar.Apply(plan);
        var name = Localizer[plan.Client.NameKey];
        // Which client and what happened; never the path (it names the OS user) and never file content.
        _log.Information("Agent client registration: {Client} {Action} -> {Outcome}, written {Written}",
            plan.Client.Id, plan.Action, result.Problem, result.Written);
        if (result.Problem == AgentClientProblem.None)
            Toasts.Success(string.Format(Localizer["AgentConnectDoneMSG"], name));
        else
            Toasts.Warning(string.Format(Localizer["AgentConnectFailedMSG"], name,
                Localizer[AgentClientTexts.ProblemKey(result.Problem)!]));

        ClosePreview();
        Refresh();
    }

    private void ClosePreview()
    {
        _plan = null;
        IsPreviewVisible = false;
        PreviewDiff = "";
        UpdateSelection();
    }

    /// <summary>For a client installed somewhere the table does not know (S55: "point at a config file manually").</summary>
    private async Task ChooseFileAsync()
    {
        if (SelectedClient is not { } row || StorageProviderAccessor.Current is not { } storage) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = string.Format(Localizer["AgentConnectChooseFileTitle"], row.Name),
            AllowMultiple = false
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;

        _manualPaths[row.Status.Client.Id] = path;
        Refresh();
    }
}

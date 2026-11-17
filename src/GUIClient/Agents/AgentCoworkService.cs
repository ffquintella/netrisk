using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Serilog;

namespace GUIClient.Agents;

/// <summary>
/// The desktop side of agent co-work (Track 11, M61): the off-by-default setting, and the runtime's lifecycle tied to
/// the session — started on sign-in when enabled, closed on sign-out or session expiry, every lease ended on lock and
/// exit. Everything that decides anything lives in <see cref="AgentCoworkRuntime"/>, which is tested headless.
/// </summary>
public sealed class AgentCoworkService
{
    /// <summary>The per-user setting. Absent means off (S55: "off by default").</summary>
    public const string EnabledSettingName = AgentServerGate.EnabledSettingName;

    private readonly IMutableConfigurationService _configuration;
    private readonly IEnvironmentService _environment;
    private readonly IAuthenticationService _authentication;
    private readonly ILogger _log = Log.ForContext<AgentCoworkService>();
    private bool _attached;

    /// <param name="tools">
    /// The M62 read tools (<c>ReadTools.AgentReadToolCatalog</c>), built by the composition root over the GUI's own
    /// ClientServices instances, so an agent's call takes the user's session and the same API path as the user's screens.
    /// </param>
    /// <param name="classification">
    /// T362: where the S52/S46 data classification is read, through the same ClientServices; the exclusion policy is read
    /// from this user's settings store on every call (<see cref="ExclusionPolicy"/>).
    /// </param>
    public AgentCoworkService(IMutableConfigurationService configuration, IEnvironmentService environment,
        IAuthenticationService authentication, IAgentApprovalPrompter prompter, IReadOnlyList<IAgentTool> tools,
        IAgentClassificationSource classification)
    {
        _configuration = configuration;
        _environment = environment;
        _authentication = authentication;
        var version = typeof(AgentCoworkService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        Runtime = new AgentCoworkRuntime(new MutableConfigurationAgentRegistrationStore(configuration), prompter,
            new SerilogAgentAuditSink(), TimeProvider.System,
            new AgentCoworkOptions
            {
                Server = new McpServerOptions { ServerVersion = version },
                Tools = tools,
                // The permission source the GUI already uses for the session (T350), read on every call. Logout and an
                // expired token clear IsAuthenticated but leave AuthenticatedUserInfo set, so the flag decides.
                CurrentUser = () => AgentSessionUser.Of(_authentication),
                Classification = new AgentClassificationOptions(classification, () => ExclusionPolicy)
            });
        Runtime.StateChanged += () => StateChanged?.Invoke();
    }

    public AgentCoworkRuntime Runtime { get; }

    /// <summary>
    /// On only when switched on <b>and</b> the current LGPD disclosure was accepted (<see cref="AgentServerGate.MayRun"/>):
    /// a switch set without it — by M61–M63, or by editing the store — shows off and starts nothing.
    /// </summary>
    public bool IsEnabled => AgentServerGate.MayRun(_configuration.GetConfigurationValue(EnabledSettingName),
        _configuration.GetConfigurationValue(AgentServerGate.DisclosureSettingName));

    /// <summary>T361: whether the user accepted the current version of the LGPD disclosure (asked before the first start).</summary>
    public bool DisclosureAccepted =>
        AgentServerGate.DisclosureAccepted(_configuration.GetConfigurationValue(AgentServerGate.DisclosureSettingName));

    public void AcceptDisclosure() =>
        _configuration.SetConfigurationValue(AgentServerGate.DisclosureSettingName, AgentServerGate.DisclosureVersion);

    /// <summary>T362: the data classes withheld from agents, read on every tool call. Unreadable reads as every class.</summary>
    public AgentExclusionPolicy ExclusionPolicy =>
        AgentExclusionPolicy.Parse(_configuration.GetConfigurationValue(AgentExclusionPolicy.SettingName));

    /// <summary>
    /// T362: changes the policy — only for a signed-in user holding <see cref="AgentExclusionPolicy.ChangePermission"/>.
    /// No agent call reaches this; it is the Agents window's Save. False when refused.
    /// </summary>
    public bool SetExclusionPolicy(AgentExclusionPolicy policy)
    {
        if (!AgentExclusionPolicyAccess.CanChange(AgentSessionUser.Of(_authentication))) return false;
        _configuration.SetConfigurationValue(AgentExclusionPolicy.SettingName, policy.Serialize());
        _log.Information("Agent data-class exclusion changed to {Policy} by user {UserId}", policy.ToString(),
            _authentication.AuthenticatedUserInfo?.UserId);
        StateChanged?.Invoke();
        return true;
    }

    /// <summary>Set when the server could not start; cleared on the next successful start.</summary>
    public bool StartFailed { get; private set; }

    /// <summary>Beside the client's other per-user data; the bridge derives the same path (AgentDiscoveryFile.DefaultPath).</summary>
    public string DiscoveryPath =>
        System.IO.Path.Combine(_environment.ApplicationDataFolder, "agents", AgentDiscoveryFile.FileName);

    public event Action? StateChanged;

    /// <summary>Hooks the session events. Called once, when the shell is created.</summary>
    public void Attach()
    {
        if (_attached) return;
        _attached = true;

        _authentication.AuthenticationSucceeded += (_, _) => _ = OnSignedInAsync();
        _authentication.SessionExpired += (_, _) => _ = RunLoggedAsync(() => Runtime.SignOutAsync(LeaseEndReason.Logout));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Runtime.StopNow(LeaseEndReason.Exit);
        if (OperatingSystem.IsWindows()) SubscribeToSessionLock();

        if (_authentication.IsAuthenticated) _ = OnSignedInAsync();
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        _configuration.SetConfigurationValue(EnabledSettingName, enabled ? "true" : "false");
        if (enabled) await OnSignedInAsync().ConfigureAwait(false);
        else await RunLoggedAsync(() => Runtime.StopAsync(LeaseEndReason.ServerStopped)).ConfigureAwait(false);
        StateChanged?.Invoke();
    }

    /// <summary>Explicit sign-out, right before the application exits.</summary>
    public void SignOut() => Runtime.StopNow(LeaseEndReason.Logout);

    /// <summary>Application exit from the shell's own Exit (the ProcessExit hook covers the rest).</summary>
    public void Exit() => Runtime.StopNow(LeaseEndReason.Exit);

    private async Task OnSignedInAsync()
    {
        if (_authentication.AuthenticatedUserInfo?.UserId is not { } userId) return;

        // Registrations are listed in the Agents window even while the server is off.
        Runtime.Registry.SetUser(userId);
        if (!IsEnabled) return;

        try
        {
            // Checked again under the runtime's lifecycle lock: a switch turned off meanwhile starts nothing.
            await Runtime.StartAsync(userId, DiscoveryPath, stillWanted: () => IsEnabled).ConfigureAwait(false);
            StartFailed = false;
        }
        catch (Exception ex)
        {
            StartFailed = true;
            _log.Error(ex, "Could not start the agent MCP server");
        }
        StateChanged?.Invoke();
    }

    private async Task RunLoggedAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Agent co-work lifecycle step failed");
        }
    }

    [SupportedOSPlatform("windows")]
    private void SubscribeToSessionLock()
    {
        try
        {
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Session lock notifications are unavailable; agent leases will not end on lock");
        }
    }

    [SupportedOSPlatform("windows")]
    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionLock
            or Microsoft.Win32.SessionSwitchReason.ConsoleDisconnect
            or Microsoft.Win32.SessionSwitchReason.RemoteDisconnect)
            Runtime.EndAllLeases(LeaseEndReason.Lock);
    }
}

/// <summary>Keeps registrations in the client's encrypted per-user configuration store. Only key hashes are written.</summary>
public sealed class MutableConfigurationAgentRegistrationStore : IAgentRegistrationStore
{
    public const string SettingName = "AgentRegistrations";

    private readonly IMutableConfigurationService _configuration;

    public MutableConfigurationAgentRegistrationStore(IMutableConfigurationService configuration) =>
        _configuration = configuration;

    public IReadOnlyList<AgentRegistration> Load() =>
        AgentRegistrationSerializer.Deserialize(_configuration.GetConfigurationValue(SettingName));

    public void Save(IReadOnlyList<AgentRegistration> registrations) =>
        _configuration.SetConfigurationValue(SettingName, AgentRegistrationSerializer.Serialize(registrations));
}

/// <summary>Writes the agent audit events to the client log (S55 "Local log"). Records never hold a key or token.</summary>
public sealed class SerilogAgentAuditSink : IAgentAuditSink
{
    private readonly ILogger _log = Log.ForContext("SourceContext", "AgentCowork");

    public void Record(AgentAuditEvent auditEvent)
    {
        var warning = auditEvent.Kind is AgentAuditKind.RequestRejected or AgentAuditKind.AuthenticationFailed
            or AgentAuditKind.RegistrationRefused or AgentAuditKind.AccessRefused or AgentAuditKind.ToolRefused
            or AgentAuditKind.ToolFailed;
        _log.Write(warning ? Serilog.Events.LogEventLevel.Warning : Serilog.Events.LogEventLevel.Information,
            "Agent audit {Kind} agent={AgentId} name={AgentName}: {Detail}",
            auditEvent.Kind, auditEvent.AgentId, auditEvent.AgentName, auditEvent.Detail);
    }
}

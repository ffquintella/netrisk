using System;

namespace GUIClient.Agents;

/// <summary>The events S55 "Audit and attribution" requires to be recorded locally.</summary>
public enum AgentAuditKind
{
    ServerStarted,
    ServerStopped,
    RequestRejected,
    RegistrationRequested,
    RegistrationRefused,
    RegistrationApproved,
    RegistrationDenied,
    RegistrationExpired,
    RegistrationAbandoned,
    KeyIssued,
    RegistrationRenamed,
    RegistrationRevoked,
    AuthenticationFailed,
    AccessRequested,
    AccessRefused,
    AccessGranted,
    AccessDenied,
    AccessRequestExpired,
    LeaseEnded,
    LeasePaused,
    LeaseResumed,

    /// <summary>A leased tool call refused before it ran: no signed-in user, another user, or a permission it lacks.</summary>
    ToolRefused,

    /// <summary>A tool that ran and failed — the API refused it, or the server or the tool failed. No item data.</summary>
    ToolFailed
}

/// <summary>
/// One audit record. <see cref="Detail"/> is composed by this code from fixed text and bounded values; it never carries
/// a key, a launch token or a request body (<c>AgentKeyHandlingTest</c> asserts the key is absent from every record).
/// </summary>
public sealed record AgentAuditEvent(
    AgentAuditKind Kind,
    DateTimeOffset At,
    string? AgentId,
    string? AgentName,
    string Detail);

/// <summary>Where audit records go. The GUI writes them to the client log; tests capture them.</summary>
public interface IAgentAuditSink
{
    void Record(AgentAuditEvent auditEvent);
}

public sealed class NullAgentAuditSink : IAgentAuditSink
{
    public static readonly NullAgentAuditSink Instance = new();

    public void Record(AgentAuditEvent auditEvent)
    {
    }
}

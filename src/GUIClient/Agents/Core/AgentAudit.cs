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
    ToolFailed,

    /// <summary>An edit tool's change was written (T354): the item and the field names, never a value.</summary>
    ToolEdited,

    /// <summary>The user approved one write in the per-write dialog (T357).</summary>
    WriteConfirmed,

    /// <summary>The user declined one write, or it was not confirmed in time (T357).</summary>
    WriteDeclined
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

using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One service-desk queue whose requests are pulled into the mirror (Track 4 milestone 4.6).
///
/// The queue itself is never mirrored — a queue is a saved JQL filter whose membership changes on
/// every triage action, so a stored copy of it is wrong the moment it is written. What is stored is
/// the *selection*: which queues feed the mirror, and how many requests to take from each.
/// </summary>
public class JiraQueueImport
{
    public int Id { get; set; }

    public int ConnectionId { get; set; }

    public int ServiceDeskId { get; set; }

    public int QueueId { get; set; }

    /// <summary>Cached label, so the admin grid reads without calling Jira.</summary>
    public string? QueueName { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>What requests from this queue link to, overriding the connection's default.</summary>
    public IssueLinkTargetKind? LinkTargetKind { get; set; }

    /// <summary>
    /// Ceiling on requests taken per sync. A bound rather than "all", because a first-time import
    /// against a five-year-old service desk is otherwise a hundred thousand rows and an hour of
    /// somebody else's rate limit.
    /// </summary>
    public int MaxRequests { get; set; } = 500;

    public DateTime CreatedAt { get; set; }

    public virtual JiraConnectionSettings? Settings { get; set; }
}

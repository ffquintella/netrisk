using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// One configured delivery destination — a Slack webhook, a Teams Workflows URL, an SMTP recipient
/// list, a generic HTTP endpoint (Track 4 milestone 4.1.2).
///
/// The provider-specific settings live in <see cref="ConfigurationJson"/> rather than in a column
/// per provider: the four providers share almost nothing (a Slack channel has no SMTP recipient, a
/// webhook has no Adaptive Card), and a wide table of mostly-null columns makes it impossible to
/// tell a missing setting from an inapplicable one. Secrets inside that JSON are encrypted at rest —
/// see <see cref="SecretsEncrypted"/>.
/// </summary>
public class NotificationChannel
{
    public int Id { get; set; }

    /// <summary>Human label — "SOC Slack", "Exec Teams". Shown in the subscription matrix.</summary>
    public string Name { get; set; } = null!;

    public NotificationChannelKind Kind { get; set; }

    /// <summary>
    /// Provider settings as JSON. Every secret-bearing field (webhook URL, bearer token, signing
    /// secret) is stored ciphertext, not plaintext, so a database dump does not hand over the
    /// ability to post into someone's Slack.
    /// </summary>
    public string ConfigurationJson { get; set; } = "{}";

    /// <summary>
    /// False only for a row written before encryption was available, or by a fixture. The dispatcher
    /// reads it to decide whether the JSON needs decrypting, which is what lets the two coexist
    /// during an upgrade instead of the first read failing.
    /// </summary>
    public bool SecretsEncrypted { get; set; } = true;

    /// <summary>Disabled channels are skipped by the dispatcher but keep their subscriptions.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The channel to try when this one has exhausted its retries — the "Slack failed, send the
    /// email" chain. Self-references and cycles are rejected by the service, because a cycle here
    /// is an infinite retry loop rather than a resilience feature.
    /// </summary>
    public int? FallbackChannelId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual NotificationChannel? FallbackChannel { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual ICollection<NotificationSubscription> Subscriptions { get; set; }
        = new List<NotificationSubscription>();
}

using System;

namespace Model.DTO;

/// <summary>
/// One row of the host-to-entity backfill report (M58, T282, S39 D-20): a host with no entity, what created it, and the
/// entity its connection would give it, so an administrator can assign the rest by hand through the hosts API.
/// </summary>
public class UnassignedHostDto
{
    public int Id { get; set; }

    public string? HostName { get; set; }

    public string? Ip { get; set; }

    public string? Fqdn { get; set; }

    /// <summary><c>hosts.source</c> — the importer or integration that created the host, or <c>manual</c>.</summary>
    public string? Source { get; set; }

    public string? ExternalProvider { get; set; }

    public DateTime? LastVerificationDate { get; set; }

    public DateTime? LastAssessedAt { get; set; }

    /// <summary>The target entity of the Vision One or Jira Assets connection that reports the host, when it has one.</summary>
    public int? SuggestedEntityId { get; set; }

    /// <summary>Which connection the suggestion comes from: <c>visionone:&lt;id&gt;</c> or <c>jira-assets:&lt;id&gt;</c>.</summary>
    public string? SuggestedBy { get; set; }
}

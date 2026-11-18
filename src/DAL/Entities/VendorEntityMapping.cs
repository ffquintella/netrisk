using System;
using DAL.Enums;

namespace DAL.Entities;

/// <summary>
/// A vendor → entity rule (T263, T268; S40 §6.6, §7.2): the hosts a connection reports with <see cref="MatchValue"/> are
/// attributed to <see cref="EntityId"/>. Applied by the connection's next sync; the enabled rule of lowest
/// <see cref="Priority"/> that matches wins, then the connection's default entity. Global configuration with no scope filter
/// (S40 §7.4), audited (§7.5), and written with global scope only, because moving a host moves who sees it.
/// </summary>
public class VendorEntityMapping
{
    public int Id { get; set; }

    /// <summary>The integration (<see cref="IntegrationKind"/>): Vision One today, Tenable with T268.</summary>
    public IntegrationKind Provider { get; set; }

    /// <summary>The connection row within the provider's own table; not a foreign key, the table differs per provider.</summary>
    public int ConnectionId { get; set; }

    public VendorMatchKind MatchKind { get; set; }

    /// <summary>The tag or group as the vendor spells it, in canonical form (<c>key: value</c>); matched case-insensitively.</summary>
    public string MatchValue { get; set; } = null!;

    /// <summary>A label for the value when the vendor gives one (a group's name); display only.</summary>
    public string? MatchLabel { get; set; }

    public int EntityId { get; set; }

    public int Priority { get; set; }

    public RestampPolicy RestampPolicy { get; set; } = RestampPolicy.OnlyUnassigned;

    public bool Enabled { get; set; } = true;

    public int? CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Entity Entity { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

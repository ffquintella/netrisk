using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class User
{
    public int Value { get; set; }

    public bool? Enabled { get; set; }

    public sbyte Lockout { get; set; }

    public string Type { get; set; } = null!;

    public byte[] Username { get; set; } = null!;

    public string Name { get; set; } = null!;

    public byte[] Email { get; set; } = null!;

    public string? Salt { get; set; }

    public byte[] Password { get; set; } = null!;

    public DateTime? LastLogin { get; set; }

    public DateTime LastPasswordChangeDate { get; set; }

    public int RoleId { get; set; }

    public string? Lang { get; set; }

    public bool Admin { get; set; }

    public int MultiFactor { get; set; }

    public sbyte ChangePassword { get; set; }

    public int? Manager { get; set; }

    public virtual ICollection<MgmtReview> MgmtReviews { get; set; } = new List<MgmtReview>();

    public virtual ICollection<Mitigation> MitigationMitigationOwnerNavigations { get; set; } = new List<Mitigation>();

    public virtual ICollection<Mitigation> MitigationSubmittedByNavigations { get; set; } = new List<Mitigation>();

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<Vulnerability> Vulnerabilities { get; set; } = new List<Vulnerability>();

    public virtual ICollection<Permission> Permissions { get; set; } = new List<Permission>();

    public virtual ICollection<Team> Teams { get; set; } = new List<Team>();
}

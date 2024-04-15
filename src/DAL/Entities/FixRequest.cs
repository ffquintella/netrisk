using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class FixRequest
{
    public int Id { get; set; }

    public int VulnerabilityId { get; set; }

    public string Identifier { get; set; } = null!;

    public DateTime CreationDate { get; set; }

    public DateTime? LastInteraction { get; set; }

    public int Status { get; set; }

    public int? FixTeamId { get; set; }

    public bool? IsTeamFix { get; set; }

    public string? SingleFixDestination { get; set; }

    public DateTime? TargetDate { get; set; }

    public DateTime? FixDate { get; set; }

    public int? LastReportingUserId { get; set; }

    public int? RequestingUserId { get; set; }

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public virtual Team? FixTeam { get; set; }

    public virtual User? LastReportingUser { get; set; }

    public virtual User? RequestingUser { get; set; }

    public virtual Vulnerability Vulnerability { get; set; } = null!;
}

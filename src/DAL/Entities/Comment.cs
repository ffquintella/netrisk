using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Comment
{
    public int Id { get; set; }

    public int? UserId { get; set; }

    public sbyte IsAnonymous { get; set; }

    public string? CommenterName { get; set; }

    public DateTime Date { get; set; }

    public int? ReplyTo { get; set; }

    public string? Type { get; set; }

    public string? Text { get; set; }

    public int? FixRequestId { get; set; }

    public int? RiskId { get; set; }

    public int? VulnerabilityId { get; set; }

    public int? HostId { get; set; }

    public virtual FixRequest? FixRequest { get; set; }

    public virtual Host? Host { get; set; }

    public virtual Risk? Risk { get; set; }

    public virtual User? User { get; set; }

    public virtual Vulnerability? Vulnerability { get; set; }
}

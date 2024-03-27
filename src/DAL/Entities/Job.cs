using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Job
{
    public int Id { get; set; }

    public int Status { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? LastUpdate { get; set; }

    public int? OwnerId { get; set; }

    public byte[]? Result { get; set; }

    public byte[]? Parameters { get; set; }

    public string? Title { get; set; }

    public byte[]? CancellationToken { get; set; }

    public int Progress { get; set; }

    public virtual User? Owner { get; set; }
}

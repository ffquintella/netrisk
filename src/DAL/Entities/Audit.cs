using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Audit
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Type { get; set; } = null!;

    public string TableName { get; set; } = null!;

    public DateTime DateTime { get; set; }

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    public string AffectedColumns { get; set; } = null!;

    public string? PrimaryKey { get; set; }
}

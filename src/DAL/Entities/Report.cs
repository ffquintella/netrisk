using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Report
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int CreatorId { get; set; }

    public int FileId { get; set; }

    public string? Parameters { get; set; }

    public DateTime CreationDate { get; set; }

    public int Type { get; set; }

    public virtual User Creator { get; set; } = null!;

    public virtual NrFile File { get; set; } = null!;
}

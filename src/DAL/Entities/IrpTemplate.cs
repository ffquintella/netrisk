using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class IrpTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string MatchingRulesJson { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public virtual ICollection<IrpTemplateTask> Tasks { get; set; } = new List<IrpTemplateTask>();
}

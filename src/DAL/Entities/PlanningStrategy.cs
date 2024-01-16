using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class PlanningStrategy
{
    public int Value { get; set; }

    public string Name { get; set; } = null!;

    public virtual ICollection<Mitigation> Mitigations { get; set; } = new List<Mitigation>();
}

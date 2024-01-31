using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class EntitiesProperty
{
    public int Id { get; set; }

    public string Type { get; set; } = null!;

    public string Value { get; set; } = null!;

    public string OldValue { get; set; } = null!;

    public int Entity { get; set; }

    public string Name { get; set; } = null!;

    public virtual Entity EntityNavigation { get; set; } = null!;
}

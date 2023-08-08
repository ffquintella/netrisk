using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Entity
{
    public int Id { get; set; }

    public string DefinitionName { get; set; } = null!;

    public string DefinitionVersion { get; set; } = null!;

    public DateTime Created { get; set; }

    public DateTime Updated { get; set; }

    public int CreatedBy { get; set; }

    public int UpdatedBy { get; set; }

    public string Status { get; set; } = null!;

    public int? Parent { get; set; }

    public virtual ICollection<EntitiesProperty> EntitiesProperties { get; set; } = new List<EntitiesProperty>();

    public virtual ICollection<Entity> InverseParentNavigation { get; set; } = new List<Entity>();

    public virtual Entity? ParentNavigation { get; set; }
}

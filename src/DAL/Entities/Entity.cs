using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Entity
    {
        public Entity()
        {
            EntitiesProperties = new HashSet<EntitiesProperty>();
        }

        public int Id { get; set; }
        public string Definition { get; set; } = null!;
        public string DefinitionName { get; set; } = null!;

        public virtual ICollection<EntitiesProperty> EntitiesProperties { get; set; }
    }
}

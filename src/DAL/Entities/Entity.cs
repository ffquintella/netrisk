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
        public string DefinitionName { get; set; } = null!;
        public string DefinitionVersion { get; set; } = null!;
        public DateTime Created { get; set; }
        public DateTime Updated { get; set; }
        public int CreatedBy { get; set; }
        public int UpdatedBy { get; set; }
        public string Status { get; set; } = null!;

        public virtual ICollection<EntitiesProperty> EntitiesProperties { get; set; }
    }
}

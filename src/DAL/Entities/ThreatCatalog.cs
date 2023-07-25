using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class ThreatCatalog
    {
        public int Id { get; set; }
        public string Number { get; set; } = null!;
        public int Grouping { get; set; }
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public int Order { get; set; }
    }
}

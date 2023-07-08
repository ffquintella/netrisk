using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class TempRrshLastUpdateAge
    {
        public string? AgeRange { get; set; }
        public int Id { get; set; }
        public int RiskId { get; set; }
        public DateTime LastUpdate { get; set; }
        public int? Age { get; set; }
        public float ResidualRisk { get; set; }
    }
}

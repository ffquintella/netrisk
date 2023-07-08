using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskScoringHistory
    {
        public int Id { get; set; }
        public int RiskId { get; set; }
        public float CalculatedRisk { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}

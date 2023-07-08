using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class ResidualRiskScoringHistory
    {
        public int Id { get; set; }
        public int RiskId { get; set; }
        public float ResidualRisk { get; set; }
        public DateTime LastUpdate { get; set; }
    }
}

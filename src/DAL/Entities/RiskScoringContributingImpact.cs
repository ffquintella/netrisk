using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskScoringContributingImpact
    {
        public int Id { get; set; }
        public int RiskScoringId { get; set; }
        public int ContributingRiskId { get; set; }
        public int Impact { get; set; }
    }
}

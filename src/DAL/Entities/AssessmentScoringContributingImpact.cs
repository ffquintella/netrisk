using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AssessmentScoringContributingImpact
    {
        public int Id { get; set; }
        public int AssessmentScoringId { get; set; }
        public int ContributingRiskId { get; set; }
        public int Impact { get; set; }
    }
}

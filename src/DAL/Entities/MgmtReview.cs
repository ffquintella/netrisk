using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class MgmtReview
    {
        public int Id { get; set; }
        public int RiskId { get; set; }
        public DateTime SubmissionDate { get; set; }
        public int Review { get; set; }
        public int Reviewer { get; set; }
        public int NextStep { get; set; }
        public string Comments { get; set; } = null!;
        public DateOnly NextReview { get; set; }
    }
}

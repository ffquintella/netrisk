using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class PendingRisk
    {
        public int Id { get; set; }
        public int AssessmentId { get; set; }
        public int AssessmentAnswerId { get; set; }
        public byte[] Subject { get; set; } = null!;
        public float Score { get; set; }
        public int? Owner { get; set; }
        public string? AffectedAssets { get; set; }
        public string Comment { get; set; } = null!;
        public DateTime SubmissionDate { get; set; }
    }
}

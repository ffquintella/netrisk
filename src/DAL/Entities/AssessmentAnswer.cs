using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AssessmentAnswer
    {
        public int Id { get; set; }
        public int AssessmentId { get; set; }
        public int QuestionId { get; set; }
        public string Answer { get; set; } = null!;
        public bool SubmitRisk { get; set; }
        public byte[] RiskSubject { get; set; } = null!;
        public float RiskScore { get; set; }
        public int AssessmentScoringId { get; set; }
        public int? RiskOwner { get; set; }
        public int Order { get; set; }
    }
}

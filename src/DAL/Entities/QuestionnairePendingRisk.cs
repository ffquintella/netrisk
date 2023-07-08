using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class QuestionnairePendingRisk
    {
        public int Id { get; set; }
        public int QuestionnaireTrackingId { get; set; }
        public int QuestionnaireScoringId { get; set; }
        public byte[] Subject { get; set; } = null!;
        public int? Owner { get; set; }
        public string? Asset { get; set; }
        public string? Comment { get; set; }
        public DateTime SubmissionDate { get; set; }
    }
}

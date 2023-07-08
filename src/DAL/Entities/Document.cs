using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Document
    {
        public int Id { get; set; }
        public int SubmittedBy { get; set; }
        public int UpdatedBy { get; set; }
        public string DocumentType { get; set; } = null!;
        public string DocumentName { get; set; } = null!;
        public int Parent { get; set; }
        public int? DocumentStatus { get; set; }
        public int FileId { get; set; }
        public DateOnly CreationDate { get; set; }
        public DateOnly? LastReviewDate { get; set; }
        public int ReviewFrequency { get; set; }
        public DateOnly NextReviewDate { get; set; }
        public DateOnly? ApprovalDate { get; set; }
        public string ControlIds { get; set; } = null!;
        public string FrameworkIds { get; set; } = null!;
        public int DocumentOwner { get; set; }
        public string AdditionalStakeholders { get; set; } = null!;
        public int Approver { get; set; }
        public string TeamIds { get; set; } = null!;
    }
}

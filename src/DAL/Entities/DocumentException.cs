using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class DocumentException
    {
        public int Value { get; set; }
        public string Name { get; set; } = null!;
        public int? PolicyDocumentId { get; set; }
        public int FrameworkId { get; set; }
        public int? ControlFrameworkId { get; set; }
        public int? Owner { get; set; }
        public string AdditionalStakeholders { get; set; } = null!;
        public DateOnly CreationDate { get; set; }
        public int ReviewFrequency { get; set; }
        public DateOnly NextReviewDate { get; set; }
        public DateOnly ApprovalDate { get; set; }
        public int? Approver { get; set; }
        public bool Approved { get; set; }
        public byte[] Description { get; set; } = null!;
        public byte[] Justification { get; set; } = null!;
        public int FileId { get; set; }
        public string AssociatedRisks { get; set; } = null!;
        public int Status { get; set; }
    }
}

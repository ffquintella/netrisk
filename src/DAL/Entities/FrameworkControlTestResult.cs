using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class FrameworkControlTestResult
    {
        public int Id { get; set; }
        public int TestAuditId { get; set; }
        public string TestResult { get; set; } = null!;
        public string Summary { get; set; } = null!;
        public DateOnly TestDate { get; set; }
        public int SubmittedBy { get; set; }
        public DateTime SubmissionDate { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}

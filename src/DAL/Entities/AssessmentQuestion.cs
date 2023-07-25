using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AssessmentQuestion
    {
        public int Id { get; set; }
        public int AssessmentId { get; set; }
        public string Question { get; set; } = null!;
        public int Order { get; set; }
    }
}

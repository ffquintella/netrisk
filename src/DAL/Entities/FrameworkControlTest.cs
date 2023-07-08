using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class FrameworkControlTest
    {
        public int Id { get; set; }
        public int Tester { get; set; }
        public int TestFrequency { get; set; }
        public DateOnly LastDate { get; set; }
        public DateOnly NextDate { get; set; }
        public string Name { get; set; } = null!;
        public string Objective { get; set; } = null!;
        public string TestSteps { get; set; } = null!;
        public int ApproximateTime { get; set; }
        public string ExpectedResults { get; set; } = null!;
        public int FrameworkControlId { get; set; }
        public int? DesiredFrequency { get; set; }
        public int Status { get; set; }
        public DateOnly? CreatedAt { get; set; }
        public string AdditionalStakeholders { get; set; } = null!;
    }
}

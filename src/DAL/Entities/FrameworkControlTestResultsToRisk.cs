using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class FrameworkControlTestResultsToRisk
    {
        public int Id { get; set; }
        public int? TestResultsId { get; set; }
        public int? RiskId { get; set; }
    }
}

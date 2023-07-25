using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Closure
    {
        public int Id { get; set; }
        public int RiskId { get; set; }
        public int UserId { get; set; }
        public DateTime ClosureDate { get; set; }
        public int CloseReason { get; set; }
        public string Note { get; set; } = null!;
    }
}

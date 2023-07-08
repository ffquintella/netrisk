using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AuditLog
    {
        public DateTime Timestamp { get; set; }
        public int RiskId { get; set; }
        public int UserId { get; set; }
        public string Message { get; set; } = null!;
        public string LogType { get; set; } = null!;
    }
}

using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class MitigationAcceptUser
    {
        public int Id { get; set; }
        public int RiskId { get; set; }
        public int UserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

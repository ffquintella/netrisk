using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskToAdditionalStakeholder
    {
        public int RiskId { get; set; }
        public int UserId { get; set; }
    }
}

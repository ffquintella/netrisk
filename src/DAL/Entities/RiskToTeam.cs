using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskToTeam
    {
        public int RiskId { get; set; }
        public int TeamId { get; set; }
    }
}

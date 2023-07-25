using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskToLocation
    {
        public int RiskId { get; set; }
        public int LocationId { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class CustomRiskModelValue
    {
        public int Impact { get; set; }
        public int Likelihood { get; set; }
        public double Value { get; set; }
    }
}

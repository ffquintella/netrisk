using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskGrouping
    {
        public int Value { get; set; }
        public string Name { get; set; } = null!;
        public bool Default { get; set; }
        public int Order { get; set; }
    }
}

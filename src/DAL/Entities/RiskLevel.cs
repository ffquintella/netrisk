using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskLevel
    {
        public decimal Value { get; set; }
        public string Name { get; set; } = null!;
        public string Color { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
    }
}

using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RiskFunction
    {
        public int Value { get; set; }
        public string Name { get; set; } = null!;
    }
}

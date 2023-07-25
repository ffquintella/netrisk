using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class AssetValue
    {
        public int Id { get; set; }
        public int MinValue { get; set; }
        public int? MaxValue { get; set; }
        public string ValuationLevelName { get; set; } = null!;
    }
}

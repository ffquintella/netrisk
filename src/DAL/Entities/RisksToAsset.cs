using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class RisksToAsset
    {
        public int? RiskId { get; set; }
        public int AssetId { get; set; }
    }
}

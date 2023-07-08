using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class ItemsToTeam
    {
        public int ItemId { get; set; }
        public int TeamId { get; set; }
        public string Type { get; set; } = null!;
    }
}

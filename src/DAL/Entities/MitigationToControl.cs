using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class MitigationToControl
    {
        public int MitigationId { get; set; }
        public int ControlId { get; set; }
        public string? ValidationDetails { get; set; }
        public int? ValidationOwner { get; set; }
        public int? ValidationMitigationPercent { get; set; }
    }
}

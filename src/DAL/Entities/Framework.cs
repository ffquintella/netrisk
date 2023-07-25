using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Framework
    {
        public int Value { get; set; }
        public int Parent { get; set; }
        public byte[] Name { get; set; } = null!;
        public byte[] Description { get; set; } = null!;
        public int Status { get; set; }
        public int Order { get; set; }
        public DateOnly? LastAuditDate { get; set; }
        public DateOnly? NextAuditDate { get; set; }
        public int? DesiredFrequency { get; set; }
    }
}

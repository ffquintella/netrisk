using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class ValidationFile
    {
        public int Id { get; set; }
        public int MitigationId { get; set; }
        public int ControlId { get; set; }
        public string Name { get; set; } = null!;
        public string Type { get; set; } = null!;
        public int Size { get; set; }
        public DateTime Timestamp { get; set; }
        public int User { get; set; }
        public byte[] Content { get; set; } = null!;
    }
}

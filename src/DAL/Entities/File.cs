using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class File
    {
        public int Id { get; set; }
        public int? RiskId { get; set; }
        public int? ViewType { get; set; }
        public string Name { get; set; } = null!;
        public string UniqueName { get; set; } = null!;
        public string? Type { get; set; }
        public int Size { get; set; }
        public DateTime Timestamp { get; set; }
        public int User { get; set; }
        public byte[] Content { get; set; } = null!;
    }
}

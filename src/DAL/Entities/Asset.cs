using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Asset
    {
        public int Id { get; set; }
        public string? Ip { get; set; }
        public string Name { get; set; } = null!;
        public int? Value { get; set; }
        public string? Location { get; set; }
        public string? Teams { get; set; }
        public string? Details { get; set; }
        public DateTime Created { get; set; }
        public sbyte Verified { get; set; }
    }
}

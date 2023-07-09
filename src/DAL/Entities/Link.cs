using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Link
    {
        public int Id { get; set; }
        public string KeyHash { get; set; } = null!;
        public string Type { get; set; } = null!;
        public DateTime CreationDate { get; set; }
        public DateTime? ExpirationDate { get; set; }
        public byte[]? Data { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Backup
    {
        public int Id { get; set; }
        public string RandomId { get; set; } = null!;
        public DateTime Timestamp { get; set; }
        public string AppZipFileName { get; set; } = null!;
        public string DbZipFileName { get; set; } = null!;
    }
}

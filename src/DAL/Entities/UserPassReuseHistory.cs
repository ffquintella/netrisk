using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class UserPassReuseHistory
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public byte[] Password { get; set; } = null!;
        public int Counts { get; set; }
    }
}

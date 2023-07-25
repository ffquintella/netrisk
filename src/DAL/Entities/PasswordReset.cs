using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class PasswordReset
    {
        public string? Username { get; set; }
        public string Token { get; set; } = null!;
        public int Attempts { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

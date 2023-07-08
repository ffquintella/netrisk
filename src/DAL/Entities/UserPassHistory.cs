using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class UserPassHistory
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Salt { get; set; } = null!;
        public byte[] Password { get; set; } = null!;
        public DateTime AddDate { get; set; }
    }
}

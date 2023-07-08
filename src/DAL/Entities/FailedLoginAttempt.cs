using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class FailedLoginAttempt
    {
        public int Id { get; set; }
        public sbyte? Expired { get; set; }
        public int UserId { get; set; }
        public string? Ip { get; set; }
        public DateTime Date { get; set; }
    }
}

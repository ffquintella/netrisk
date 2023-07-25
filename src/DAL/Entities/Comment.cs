using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Comment
    {
        public int Id { get; set; }
        public int RiskId { get; set; }
        public DateTime Date { get; set; }
        public int User { get; set; }
        public string Comment1 { get; set; } = null!;
    }
}

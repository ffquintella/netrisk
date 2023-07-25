using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class FrameworkControlTestComment
    {
        public int Id { get; set; }
        public int TestAuditId { get; set; }
        public DateTime Date { get; set; }
        public int User { get; set; }
        public string Comment { get; set; } = null!;
    }
}

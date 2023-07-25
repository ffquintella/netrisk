using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class ContributingRisk
    {
        public int Id { get; set; }
        public string Subject { get; set; } = null!;
        public float Weight { get; set; }
    }
}

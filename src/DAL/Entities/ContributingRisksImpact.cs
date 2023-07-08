using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class ContributingRisksImpact
    {
        public int Id { get; set; }
        public int ContributingRisksId { get; set; }
        public int Value { get; set; }
        public string Name { get; set; } = null!;
    }
}

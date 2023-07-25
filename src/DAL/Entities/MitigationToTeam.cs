using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class MitigationToTeam
    {
        public int MitigationId { get; set; }
        public int TeamId { get; set; }
    }
}

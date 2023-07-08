using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class FrameworkControlMapping
    {
        public int Id { get; set; }
        public int ControlId { get; set; }
        public int Framework { get; set; }
        public string ReferenceName { get; set; } = null!;
    }
}

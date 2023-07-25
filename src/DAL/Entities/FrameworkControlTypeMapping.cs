using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class FrameworkControlTypeMapping
    {
        public int Id { get; set; }
        public int ControlId { get; set; }
        public int ControlTypeId { get; set; }
    }
}

using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Role
    {
        public int Value { get; set; }
        public string Name { get; set; } = null!;
        public bool Admin { get; set; }
        public bool? Default { get; set; }
    }
}

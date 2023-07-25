using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class ControlType
    {
        public int Value { get; set; }
        public string Name { get; set; } = null!;
    }
}

using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Setting
    {
        public string Name { get; set; } = null!;
        public string? Value { get; set; }
    }
}

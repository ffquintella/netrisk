using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Field
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Type { get; set; } = null!;
    }
}

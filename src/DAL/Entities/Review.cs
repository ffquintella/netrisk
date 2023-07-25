using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Review
    {
        public int Value { get; set; }
        public string Name { get; set; } = null!;
    }
}

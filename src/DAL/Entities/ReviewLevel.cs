using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class ReviewLevel
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public string Name { get; set; } = null!;
    }
}

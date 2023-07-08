using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Assessment
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public DateTime Created { get; set; }
    }
}

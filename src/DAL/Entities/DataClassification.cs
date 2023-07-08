using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class DataClassification
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public int Order { get; set; }
    }
}

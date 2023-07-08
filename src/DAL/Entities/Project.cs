using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class Project
    {
        public int Value { get; set; }
        public string Name { get; set; } = null!;
        public DateTime? DueDate { get; set; }
        public int? Consultant { get; set; }
        public int? BusinessOwner { get; set; }
        public int? DataClassification { get; set; }
        public int Order { get; set; }
        public int Status { get; set; }
    }
}

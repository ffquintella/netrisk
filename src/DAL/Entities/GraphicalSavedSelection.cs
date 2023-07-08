using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class GraphicalSavedSelection
    {
        public int Value { get; set; }
        public int UserId { get; set; }
        public string Type { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string GraphicalDisplaySettings { get; set; } = null!;
    }
}

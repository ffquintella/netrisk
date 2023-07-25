using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class DynamicSavedSelection
    {
        public int Value { get; set; }
        public int UserId { get; set; }
        public string Type { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? CustomDisplaySettings { get; set; }
        public string CustomSelectionSettings { get; set; } = null!;
        public string CustomColumnFilters { get; set; } = null!;
    }
}

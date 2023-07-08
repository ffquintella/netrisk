using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class TestResult
    {
        public int Value { get; set; }
        public string Name { get; set; } = null!;
        public string BackgroundClass { get; set; } = null!;
    }
}

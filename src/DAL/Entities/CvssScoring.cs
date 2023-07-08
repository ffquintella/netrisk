using System;
using System.Collections.Generic;

namespace DAL.Entities
{
    public partial class CvssScoring
    {
        public int Id { get; set; }
        public string MetricName { get; set; } = null!;
        public string AbrvMetricName { get; set; } = null!;
        public string MetricValue { get; set; } = null!;
        public string AbrvMetricValue { get; set; } = null!;
        public float NumericValue { get; set; }
    }
}

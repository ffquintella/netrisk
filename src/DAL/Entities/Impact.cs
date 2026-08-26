using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Impact
{
    public string? Name { get; set; }

    public int Value { get; set; }

    /// <summary>
    /// What this level actually means, shown at rating time (Track 8 milestone 8.7.1).
    /// </summary>
    public string? Definition { get; set; }

    /// <summary>Lower bound of the monetary loss this level denotes.</summary>
    public double? ImpactMin { get; set; }

    /// <summary>Upper bound of the monetary loss this level denotes.</summary>
    public double? ImpactMax { get; set; }
}

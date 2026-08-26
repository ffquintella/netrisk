using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Likelihood
{
    public string? Name { get; set; }

    public int Value { get; set; }

    /// <summary>
    /// What this level actually means, in words the rater reads at rating time (Track 8 milestone
    /// 8.7.1). Bare labels invite raters to substitute their own meanings — the finding behind
    /// Budescu et al. and Cox 2008 — and a scale whose levels mean different things to different
    /// people cannot be aggregated.
    /// </summary>
    public string? Definition { get; set; }

    /// <summary>Lower bound of the annual probability this level denotes, 0–1.</summary>
    public double? ProbabilityMin { get; set; }

    /// <summary>Upper bound of the annual probability this level denotes, 0–1.</summary>
    public double? ProbabilityMax { get; set; }
}

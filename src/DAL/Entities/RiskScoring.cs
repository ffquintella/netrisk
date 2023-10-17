using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class RiskScoring
{
    public int Id { get; set; }

    public int ScoringMethod { get; set; }

    public float CalculatedRisk { get; set; }

    public float ClassicLikelihood { get; set; }

    public float ClassicImpact { get; set; }

    public float? Custom { get; set; }

    public double? ContributingScore { get; set; }
}

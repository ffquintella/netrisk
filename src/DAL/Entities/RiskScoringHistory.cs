using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class RiskScoringHistory
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public float CalculatedRisk { get; set; }

    public DateTime LastUpdate { get; set; }

    /// <summary>
    /// The residual score as it stood at this point (Track 8 milestone 8.2.1). Historized beside the
    /// inherent score rather than in a table of its own: the pair is what a trend chart needs, and
    /// splitting them would make "was the treatment working in March" a join.
    /// </summary>
    public float? ResidualRisk { get; set; }
}

namespace Model.TailRisk;

/// <summary>The outcome of Gate B on the tail (Stage 9.7, S48 §4.7.2). Computed enums start at 1 (S43 D17).</summary>
public enum TailAppetiteState
{
    /// <summary>No appetite governs the risk, or it has no monetary tail tolerance: the tail is not gated.</summary>
    NotConfigured = 1,

    /// <summary>A tolerance exists but the comparison cannot be made — never read as within tolerance. The reasons say why.</summary>
    NotAssessable = 2,

    /// <summary>Every configured tolerance holds.</summary>
    WithinTolerance = 3,

    /// <summary>At least one statistic is above its tolerance: treat or escalate (Gate B).</summary>
    ExceedsTolerance = 4
}

/// <summary>Why Gate B on the tail could not be assessed (S48 §4.7.2).</summary>
public enum TailAppetiteNotAssessableReason
{
    /// <summary>The risk has no tail statistics: no quantitative analysis, or one computed before schema 96.</summary>
    NoTailStatistics = 1,

    /// <summary>The portfolio has no quantified risk at all.</summary>
    NoQuantifiedRisks = 2,

    /// <summary>
    /// Some risks of the portfolio are not quantified and the quantified part is within tolerance. Unquantified
    /// risks can only add loss, so a breach on the quantified part is conclusive and "within" is not.
    /// </summary>
    IncompleteCoverage = 3
}

/// <summary>The statistics Gate B compares (S48 §4.7).</summary>
public enum TailStatistic
{
    ExpectedLoss = 1,
    P95 = 2,
    Cvar95 = 3
}

/// <summary>Which run of each risk a portfolio aggregates (S48 §4.6).</summary>
public enum PortfolioBasis
{
    /// <summary>Each risk's residual run where it exists, its inherent run otherwise — the appetite's rule.</summary>
    Residual = 1,

    /// <summary>Each risk's inherent run.</summary>
    Inherent = 2
}

/// <summary>How the dependence between the risks of a portfolio was modelled (S48 §4.6).</summary>
public enum PortfolioDependence
{
    /// <summary>No correlation is declared between any two members: independence is an assumption, said so.</summary>
    AssumedIndependent = 1,

    /// <summary>At least one pair of members has a declared correlation; undeclared pairs are independent.</summary>
    Declared = 2
}

/// <summary>Why a risk of a portfolio was not aggregated (S48 §4.6).</summary>
public enum PortfolioExclusionReason
{
    /// <summary>The risk has no tail statistics on the requested basis. Never summed as zero.</summary>
    NotQuantified = 1
}

using DAL.Enums;

namespace Model.TailRisk;

/// <summary>
/// The bounds every input of Stage 9.7 is validated against (S48 §4) — one place, so the service, the pure rules and
/// the tests agree.
/// </summary>
public static class TailRiskLimits
{
    /// <summary>Monte Carlo iterations below this are raised to it, as the Track 8 simulator always did.</summary>
    public const int MinIterations = 1_000;

    /// <summary>Above this a request is refused: the bootstrap multiplies the cost of every iteration (S48 §4.3).</summary>
    public const int MaxIterations = 100_000;

    /// <summary>The largest per-event loss of a component, in the unit of the FAIR inputs (10¹²).</summary>
    public const double MaxLoss = 1_000_000_000_000d;

    /// <summary>The largest tolerance accepted on an appetite (10¹²).</summary>
    public const decimal MaxLimit = 1_000_000_000_000m;

    public const int MaxBasisLength = 1000;
    public const int MaxRationaleLength = 2000;

    /// <summary>The risks one portfolio request may aggregate.</summary>
    public const int MaxPortfolioRisks = 500;

    /// <summary>members × iterations held in memory by one aggregation (40 MB of samples).</summary>
    public const long MaxPortfolioSamples = 5_000_000;

    /// <summary>The largest connected group of correlated risks (S48 §4.5).</summary>
    public const int MaxCorrelationGroup = 200;

    /// <summary>Decimal places a correlation coefficient is stored with (<c>decimal(4,3)</c>).</summary>
    public const int CoefficientDecimals = 3;

    /// <summary>The default seed of a portfolio aggregation — the Track 8 simulator's default.</summary>
    public const int DefaultPortfolioSeed = 20260826;
}

/// <summary>One component's range as simulated and its contribution to a run (S48 §4.4).</summary>
public class LossComponentContributionDto
{
    public LossComponent Component { get; set; }

    public double Min { get; set; }

    public double MostLikely { get; set; }

    public double Max { get; set; }

    /// <summary>The component's mean annual loss; the components add up to the run's E[L].</summary>
    public double ExpectedLoss { get; set; }

    /// <summary>The component's Euler share of the run's CVaR95; the components add up to it.</summary>
    public double Cvar95 { get; set; }
}

/// <summary>The tail statistics of one Monte Carlo run, with their 95 % confidence intervals (S48 §4.2–4.3).</summary>
public class TailStatisticsDto
{
    public TailRun Run { get; set; }

    public int Iterations { get; set; }

    public int Seed { get; set; }

    /// <summary>The confidence level of the intervals (0.95). They measure Monte Carlo error, not the uncertainty of the inputs.</summary>
    public decimal ConfidenceLevel { get; set; }

    public double ExpectedLoss { get; set; }

    public double ExpectedLossCiLow { get; set; }

    public double ExpectedLossCiHigh { get; set; }

    public double P95 { get; set; }

    public double P95CiLow { get; set; }

    public double P95CiHigh { get; set; }

    /// <summary>Expected shortfall at 95 %: the mean of the worst 5 % of simulated years.</summary>
    public double Cvar95 { get; set; }

    public double Cvar95CiLow { get; set; }

    public double Cvar95CiHigh { get; set; }

    /// <summary>The fraction of simulated years with any loss.</summary>
    public double ProbabilityOfLoss { get; set; }

    /// <summary>E[L | L &gt; 0]; null when no simulated year had a loss.</summary>
    public double? ConditionalLoss { get; set; }

    public MagnitudeSource MagnitudeSource { get; set; }

    public double MitigationEffectiveness { get; set; }

    public DateTime ComputedAt { get; set; }

    /// <summary>Empty for a single-range run.</summary>
    public List<LossComponentContributionDto> Components { get; set; } = [];
}

/// <summary>A declared loss component of a risk (S48 §4.1).</summary>
public class LossComponentDto
{
    public LossComponent Component { get; set; }

    public double Min { get; set; }

    public double MostLikely { get; set; }

    public double Max { get; set; }

    public string? Basis { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }
}

/// <summary>One component of <see cref="LossComponentsRequest"/>: a per-event range, and its basis (required for a fine).</summary>
public class LossComponentRequest
{
    public LossComponent? Component { get; set; }

    public double? Min { get; set; }

    public double? MostLikely { get; set; }

    public double? Max { get; set; }

    /// <summary>≤ 1 000; required for <see cref="LossComponent.Fine"/> — the legal basis.</summary>
    public string? Basis { get; set; }
}

/// <summary><c>PUT /TailRisk/Risks/{id}/LossComponents</c> — replaces the risk's components (1–7, no repeats).</summary>
public class LossComponentsRequest
{
    public List<LossComponentRequest>? Components { get; set; }
}

/// <summary>A tolerance a statistic exceeded (S48 §4.7.2).</summary>
public class TailBreachDto
{
    public TailStatistic Statistic { get; set; }

    public double Value { get; set; }

    public double Limit { get; set; }
}

/// <summary>
/// Gate B on the tail — the comparison of the appetite's monetary tolerances with E[L], P95 and CVaR95, for one risk or
/// for a portfolio (S48 §4.7). Never "within" when it could not be assessed.
/// </summary>
public class TailAppetiteEvaluation
{
    public TailAppetiteState State { get; set; }

    /// <summary>Why it is not assessable; empty otherwise.</summary>
    public List<TailAppetiteNotAssessableReason> Reasons { get; set; } = [];

    /// <summary>The appetite whose tolerances applied, when one governs.</summary>
    public int? AppetiteId { get; set; }

    /// <summary>The entity of that appetite, or null for the organization-wide one.</summary>
    public int? EntityId { get; set; }

    /// <summary>The run compared — the residual where it exists, the inherent otherwise. Null for a portfolio.</summary>
    public TailRun? Run { get; set; }

    public double? ExpectedLoss { get; set; }

    public double? P95 { get; set; }

    public double? Cvar95 { get; set; }

    public double? MaxExpectedLoss { get; set; }

    public double? MaxP95 { get; set; }

    public double? MaxCvar95 { get; set; }

    public List<TailBreachDto> Breaches { get; set; } = [];

    /// <summary>A tolerance lies inside a statistic's confidence interval: the outcome depends on Monte Carlo noise. Informational.</summary>
    public bool Marginal { get; set; }

    /// <summary>Portfolio only: the risks aggregated, and the risks in the portfolio.</summary>
    public int? QuantifiedRisks { get; set; }

    public int? TotalRisks { get; set; }

    /// <summary>A sentence the clients can show verbatim.</summary>
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>Whether a risk meets the flag 8 derivation criterion, and the thresholds in force (S48 §4.8).</summary>
public class TailFlagCriterionDto
{
    /// <summary>Null when the risk has no inherent tail statistics.</summary>
    public bool? Holds { get; set; }

    /// <summary>The basis text the reconciliation records when it holds.</summary>
    public string? Basis { get; set; }

    public double MaxAnnualProbability { get; set; }

    public double CatastrophicLoss { get; set; }

    /// <summary>The persisted state of flag 8 — derived as of the last reconciliation, and declared.</summary>
    public bool Derived { get; set; }

    public bool Declared { get; set; }
}

/// <summary><c>GET /TailRisk/Risks/{id}</c> — the tail of a risk, its decomposition, Gate B and the flag 8 criterion.</summary>
public class RiskTailDto
{
    public int RiskId { get; set; }

    public TailStatisticsDto? Inherent { get; set; }

    public TailStatisticsDto? Residual { get; set; }

    /// <summary>The components currently declared (the runs carry the ones they simulated).</summary>
    public List<LossComponentDto> DeclaredComponents { get; set; } = [];

    /// <summary>The risk has a quantitative analysis whose statistics were recomputed by this request.</summary>
    public bool Recomputed { get; set; }

    public TailAppetiteEvaluation Appetite { get; set; } = new();

    public TailFlagCriterionDto TailFlag { get; set; } = new();
}

/// <summary>A declared correlation between two scenarios (S48 §4.5).</summary>
public class RiskCorrelationDto
{
    public int Id { get; set; }

    /// <summary>The smaller risk id of the pair.</summary>
    public int RiskAId { get; set; }

    public int RiskBId { get; set; }

    public decimal Coefficient { get; set; }

    public string Rationale { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }
}

/// <summary><c>PUT /TailRisk/Correlations</c> — declares or updates the correlation of a pair, in either order.</summary>
public class RiskCorrelationRequest
{
    public int? RiskAId { get; set; }

    public int? RiskBId { get; set; }

    /// <summary>0–1, rounded to three decimals.</summary>
    public decimal? Coefficient { get; set; }

    /// <summary>Required, 1–2 000: why these scenarios move together.</summary>
    public string? Rationale { get; set; }
}

/// <summary><c>POST /TailRisk/Portfolio</c> — the risks to aggregate and how (S48 §4.6).</summary>
public class PortfolioTailRequest
{
    /// <summary>At most 500; null = every visible open risk, optionally of <see cref="EntityId"/>.</summary>
    public List<int>? RiskIds { get; set; }

    /// <summary>Restricts the default universe to one entity's risks, and selects that entity's appetite.</summary>
    public int? EntityId { get; set; }

    public PortfolioBasis? Basis { get; set; }

    /// <summary>The copula's seed; default 20260826.</summary>
    public int? Seed { get; set; }
}

/// <summary>One aggregated risk of a portfolio.</summary>
public class PortfolioMemberDto
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public int? EntityId { get; set; }

    public TailRun Run { get; set; }

    /// <summary>Iterations of the stored run; the member was re-simulated at the portfolio's when they differ.</summary>
    public int StoredIterations { get; set; }

    public double ExpectedLoss { get; set; }

    public double P95 { get; set; }

    public double Cvar95 { get; set; }

    /// <summary>The member's Euler share of the portfolio CVaR95; the members add up to it.</summary>
    public double Cvar95Contribution { get; set; }

    /// <summary>Gate B on the member's own tail, against its own appetite's scenario tolerances.</summary>
    public TailAppetiteState ScenarioAppetite { get; set; }
}

/// <summary>A risk of the portfolio that was not aggregated, and why.</summary>
public class PortfolioExcludedRiskDto
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public PortfolioExclusionReason Reason { get; set; }
}

/// <summary>
/// The aggregated tail of a portfolio (S48 §4.6). Computed on request and never stored. E[L] is the sum of the members'
/// E[L]; P95 is not a sum and <see cref="SumOfP95"/> is shown only as a reference; <see cref="SumOfCvar95"/> is an upper
/// bound on the portfolio CVaR95 under any dependence.
/// </summary>
public class PortfolioTailDto
{
    public DateTime GeneratedAt { get; set; }

    public PortfolioBasis Basis { get; set; }

    public int Seed { get; set; }

    public int Iterations { get; set; }

    public decimal ConfidenceLevel { get; set; }

    public PortfolioDependence Dependence { get; set; }

    /// <summary>Declared pairs between members (a coefficient of 0 included).</summary>
    public int DeclaredPairs { get; set; }

    public double ExpectedLoss { get; set; }

    public double ExpectedLossCiLow { get; set; }

    public double ExpectedLossCiHigh { get; set; }

    public double P95 { get; set; }

    public double P95CiLow { get; set; }

    public double P95CiHigh { get; set; }

    public double Cvar95 { get; set; }

    public double Cvar95CiLow { get; set; }

    public double Cvar95CiHigh { get; set; }

    public double ProbabilityOfLoss { get; set; }

    public double SumOfExpectedLoss { get; set; }

    public double SumOfP95 { get; set; }

    public double SumOfCvar95 { get; set; }

    /// <summary>Σ CVaR95 − portfolio CVaR95: what the dependence assumption buys. Never negative.</summary>
    public double Diversification { get; set; }

    public List<PortfolioMemberDto> Members { get; set; } = [];

    public List<PortfolioExcludedRiskDto> NotQuantified { get; set; } = [];

    /// <summary>Gate B on the portfolio, against the portfolio tolerances of the governing appetite.</summary>
    public TailAppetiteEvaluation Appetite { get; set; } = new();
}

/// <summary>The monetary tolerances of an appetite (S48 §4.7.1).</summary>
public class RiskAppetiteTailLimitsDto
{
    public int AppetiteId { get; set; }

    public int? EntityId { get; set; }

    public decimal? MaxScenarioExpectedLoss { get; set; }

    public decimal? MaxScenarioP95 { get; set; }

    public decimal? MaxScenarioCvar95 { get; set; }

    public decimal? MaxPortfolioExpectedLoss { get; set; }

    public decimal? MaxPortfolioP95 { get; set; }

    public decimal? MaxPortfolioCvar95 { get; set; }

    public string Rationale { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }
}

/// <summary><c>PUT /RiskAppetites/{id}/TailLimits</c> — at least one tolerance, each 0–10¹², and a rationale.</summary>
public class RiskAppetiteTailLimitsRequest
{
    public decimal? MaxScenarioExpectedLoss { get; set; }

    public decimal? MaxScenarioP95 { get; set; }

    public decimal? MaxScenarioCvar95 { get; set; }

    public decimal? MaxPortfolioExpectedLoss { get; set; }

    public decimal? MaxPortfolioP95 { get; set; }

    public decimal? MaxPortfolioCvar95 { get; set; }

    /// <summary>Required, 1–2 000: the Phase 0 decision that set these limits.</summary>
    public string? Rationale { get; set; }
}

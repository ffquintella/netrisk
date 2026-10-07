namespace Model.TreatmentEconomics;

/// <summary>The outcome of Gate C on one mitigation (Stage 9.6, S47 §4.6). Computed enums start at 1 (S43 D17).</summary>
public enum GateCOutcome
{
    /// <summary>E[L before] − E[L after] exceeds the annualized total cost.</summary>
    Passes = 1,

    /// <summary>The benefit is at most the annualized total cost.</summary>
    Fails = 2,

    /// <summary>
    /// An ingredient is missing — never read as a pass or a fail, and never as a zero cost or a zero benefit.
    /// The reasons say which.
    /// </summary>
    NotAssessable = 3,

    /// <summary>The treatment option is "accept": there is no control to weigh.</summary>
    NotApplicable = 4
}

/// <summary>Why Gate C could not be computed (S47 §4.6), reported in this order.</summary>
public enum GateCNotAssessableReason
{
    /// <summary>No monetary cost is declared. Not a zero cost — the T181 edge case.</summary>
    NoMonetaryCost = 1,

    /// <summary>The risk has no quantitative (FAIR-lite) analysis, so there is no E[L] before.</summary>
    NoQuantitativeAnalysis = 2,

    /// <summary>The analysis has no residual run: the mitigation's effectiveness is zero or was never declared.</summary>
    NoResidualRun = 3,

    /// <summary>A residual median exists but not the mean — computed before schema 95; recompute. The median is never used.</summary>
    ResidualMeanNotRecorded = 4,

    /// <summary>The residual run was computed for another (more recent) mitigation of the same risk.</summary>
    ResidualForAnotherMitigation = 5
}

/// <summary>Gate D's priority layers (S47 §4.7). Lower is selected first.</summary>
public enum PortfolioTier
{
    /// <summary>The risk carries Gate A: treatment is mandatory whatever the economics.</summary>
    Mandatory = 1,

    /// <summary>Tail (flag 8) or systemic (flag 6): preserved ahead of the economic ranking.</summary>
    Protected = 2,

    /// <summary>Selected by benefit/cost ratio among the treatments that pass Gate C.</summary>
    Economic = 3
}

/// <summary>What Gate D did with one candidate, and why (S47 §4.7).</summary>
public enum PortfolioItemStatus
{
    Selected = 1,

    /// <summary>Its first-year cost, with the prerequisites it pulls in, exceeds what is left of the budget.</summary>
    OverBudget = 2,

    /// <summary>Its effort, with its prerequisites, exceeds what is left of the people capacity.</summary>
    OverPeopleCapacity = 3,

    /// <summary>Its critical path (its duration after its prerequisites') ends after the deadline.</summary>
    MissesDeadline = 4,

    /// <summary>A prerequisite is excluded, or outside the portfolio and not completed.</summary>
    BlockedByDependency = 5,

    /// <summary>It is on a dependency cycle.</summary>
    DependencyCycle = 6,

    /// <summary>Economic tier only: Gate C fails.</summary>
    FailsGateC = 7,

    /// <summary>An ingredient is missing — option, monetary cost, or the effort/duration a constraint needs.</summary>
    NotAssessable = 8,

    /// <summary>The treatment option is "accept".</summary>
    NotApplicable = 9
}

/// <summary>The Phase 5 elements of an action plan line (S47 §4.8).</summary>
public enum ActionPlanElement
{
    Owner = 1,
    DueDate = 2,
    AcceptanceCriterion = 3,
    CompletionEvidence = 4
}

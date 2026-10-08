using System;
using System.Collections.Generic;

namespace DAL.Entities;

/// <summary>
/// The backtest of one incident or near miss against the register (Stage 9.9, S50 §4.4): which registered risks, in
/// the judgement of whoever assessed it, describe what happened. No link means "no scenario corresponds"; no row at all
/// means "not assessed yet", which is never counted as foreseen nor as unforeseen.
///
/// The outcome is not stored: it is computed on read from the dates (S50 D6). A risk foresaw the incident only if it
/// was registered strictly before the incident occurred — a scenario written after the fact is never counted as
/// foreseen, whatever the link says.
/// </summary>
public class IncidentBacktest
{
    public int Id { get; set; }

    public int IncidentId { get; set; }

    public string? Note { get; set; }

    /// <summary>UTC.</summary>
    public DateTime AssessedAt { get; set; }

    public int? AssessedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC.</summary>
    public DateTime? UpdatedAt { get; set; }

    public virtual Incident Incident { get; set; } = null!;

    public virtual User? AssessedBy { get; set; }

    public virtual ICollection<IncidentBacktestRisk> Risks { get; set; } = new List<IncidentBacktestRisk>();
}

/// <summary>A risk an assessor matched to an incident (S50 §4.4). One row per backtest and risk.</summary>
public class IncidentBacktestRisk
{
    public int Id { get; set; }

    public int BacktestId { get; set; }

    public int RiskId { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public virtual IncidentBacktest Backtest { get; set; } = null!;

    public virtual Risk Risk { get; set; } = null!;
}

using System.Globalization;
using DAL.Enums;
using Model.TreatmentEconomics;

namespace Tools.TreatmentEconomics;

/// <summary>One treatment Gate D may fund (S47 §4.7).</summary>
public sealed record PortfolioCandidate
{
    public int MitigationId { get; init; }

    public int RiskId { get; init; }

    public string RiskSubject { get; init; } = string.Empty;

    public TreatmentOption? Option { get; init; }

    /// <summary>Null when no monetary cost is declared.</summary>
    public TreatmentCost? Cost { get; init; }

    public decimal? EffortPersonDays { get; init; }

    public int? DurationDays { get; init; }

    /// <summary>The mitigations this one depends on.</summary>
    public IReadOnlyList<int> Prerequisites { get; init; } = [];

    public GateCResultDto GateC { get; init; } = new();

    /// <summary>Gate A holds on the risk.</summary>
    public bool GateA { get; init; }

    /// <summary>Flag 6 — systemic risk or single point of failure.</summary>
    public bool Systemic { get; init; }

    /// <summary>Flag 8 — low probability, catastrophic impact.</summary>
    public bool Tail { get; init; }

    /// <summary>Residual above the appetite ceiling; null without an appetite.</summary>
    public bool? AboveAppetite { get; init; }
}

/// <summary>Gate D's constraints. <paramref name="DeadlineDays"/> counts from the start date.</summary>
public sealed record PortfolioConstraints(decimal Budget, decimal? PeopleCapacityPersonDays, int? DeadlineDays);

/// <summary>
/// Gate D of MIGR-TI/IA Phase 4 — capacity and portfolio (Stage 9.6, S47 §4.7): "maximize expected reduction and
/// resilience under budget, people, dependencies and deadline; preserve tail and systemic risks".
///
/// A layered greedy selection rather than an exact optimizer (S47 D5): every item says why it was or was not
/// funded, the result is deterministic whatever the input order, and the rule a naive optimizer breaks first is
/// structural — the <see cref="PortfolioTier.Protected"/> tier (flags 6 and 8) is filled before the
/// <see cref="PortfolioTier.Economic"/> one, so a high-E[L] treatment cannot push a tail or systemic one out
/// (PD1, PD2, T181). Gate A precedes everything: its treatments form the first tier whatever Gate C says, and
/// one that does not fit raises <see cref="PortfolioSelectionDto.GateAShortfall"/> instead of disappearing.
///
/// Pure: no clock, no database. The service supplies the candidates, the constraints and the completed mitigations.
/// </summary>
public static class PortfolioSelector
{
    private sealed class Item(PortfolioCandidate candidate)
    {
        public PortfolioCandidate Candidate { get; } = candidate;
        public PortfolioTier OwnTier { get; set; }
        public PortfolioTier Tier { get; set; }
        public int? TierInheritedFrom { get; set; }
        public PortfolioItemStatus? Status { get; set; }
        public List<string> Reasons { get; } = [];
        public int? CompletionDay { get; set; }
        public int? SelectionOrder { get; set; }
        public int? SelectedFor { get; set; }
        public bool OnCycle { get; set; }
    }

    public static PortfolioSelectionDto Select(PortfolioConstraints constraints,
        IReadOnlyCollection<PortfolioCandidate> candidates, IReadOnlySet<int> completedMitigationIds)
    {
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(completedMitigationIds);

        // Sorted by id once, so every loop below — and therefore the result — is independent of input order.
        var items = candidates
            .GroupBy(c => c.MitigationId).Select(g => g.First())
            .OrderBy(c => c.MitigationId)
            .ToDictionary(c => c.MitigationId, c => new Item(c));
        var ids = items.Keys.OrderBy(id => id).ToList();

        List<int> PrerequisitesOf(int id) => items[id].Candidate.Prerequisites
            .Where(p => !completedMitigationIds.Contains(p)).Distinct().OrderBy(p => p).ToList();

        // 1–2. Own tier, then the tier inherited from the treatments that depend on it: the prerequisite of a
        // mandatory treatment is mandatory. Tiers only ever move up, so the relaxation terminates, cycles included.
        foreach (var item in items.Values) item.Tier = item.OwnTier = OwnTier(item.Candidate);

        for (var changed = true; changed;)
        {
            changed = false;
            // An "accept" is never funded, so it lends no tier to what it would depend on.
            foreach (var id in ids.Where(i => items[i].Candidate.Option != TreatmentOption.Accept))
            foreach (var p in PrerequisitesOf(id).Where(items.ContainsKey))
            {
                if (items[p].Tier <= items[id].Tier) continue;
                items[p].Tier = items[id].Tier;
                items[p].TierInheritedFrom = id;
                changed = true;
            }
        }

        // 3. What excludes a candidate on its own.
        foreach (var id in ids) ApplyOwnEligibility(items[id], constraints);

        // 4. Cycles.
        foreach (var id in FindCycleMembers(ids, PrerequisitesOf, items.ContainsKey))
        {
            items[id].OnCycle = true;
            Exclude(items[id], PortfolioItemStatus.DependencyCycle,
                "It is on a dependency cycle — a circular plan cannot be executed; break the cycle.");
        }

        // 5. Critical path against the deadline.
        var memo = new Dictionary<int, int?>();
        foreach (var id in ids)
        {
            var item = items[id];
            item.CompletionDay = Completion(id, items, PrerequisitesOf, memo, []);

            if (constraints.DeadlineDays is { } deadline && item.CompletionDay is { } day && day > deadline)
                Exclude(item, PortfolioItemStatus.MissesDeadline,
                    $"Its critical path ends on day {day}, after the deadline (day {deadline}).");
        }

        // 6. Blocking, to a fixpoint: a prerequisite that is excluded, or outside the portfolio and not completed.
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var id in ids.Where(i => items[i].Status is null))
            {
                foreach (var p in PrerequisitesOf(id))
                {
                    string? why = !items.TryGetValue(p, out var prerequisite)
                        ? $"Prerequisite #{p} is neither in this portfolio nor completed."
                        : prerequisite.Status is { } status
                            ? $"Prerequisite #{p} cannot be selected ({status})."
                            : null;

                    if (why is null) continue;
                    Exclude(items[id], PortfolioItemStatus.BlockedByDependency, why);
                    changed = true;
                    break;
                }
            }
        }

        // 7–8. Order, then select greedily, each candidate with the prerequisites it still needs. A candidate's own
        // turn comes in its own tier: an inherited tier only lets it ride in its dependent's bundle, so the
        // prerequisite of an unfunded tail treatment is not funded on its own ahead of the economic ranking.
        var budgetLeft = constraints.Budget;
        var peopleLeft = constraints.PeopleCapacityPersonDays;
        var order = 0;

        var queue = ids.Where(id => items[id].Status is null)
            .OrderBy(id => items[id].OwnTier)
            .ThenByDescending(id => Priority(items[id].Candidate.GateC))
            .ThenByDescending(id => items[id].Candidate.GateC.ExpectedLossBefore ?? double.NegativeInfinity)
            .ThenBy(id => id)
            .ToList();

        foreach (var id in queue)
        {
            var item = items[id];
            if (item.Status == PortfolioItemStatus.Selected) continue;

            // Kept eligible only for its dependents' sake: on its own it would not pass the economic tier.
            if (item.OwnTier == PortfolioTier.Economic && item.Candidate.GateC.Outcome != GateCOutcome.Passes)
                continue;

            var bundle = Bundle(id, items, PrerequisitesOf);
            var cost = bundle.Sum(b => items[b].Candidate.Cost!.FirstYear);
            var effort = bundle.Sum(b => items[b].Candidate.EffortPersonDays ?? 0m);
            var with = bundle.Count > 1
                ? $" (with prerequisite{(bundle.Count > 2 ? "s" : "")} {string.Join(", ", bundle.Where(b => b != id).Select(b => $"#{b}"))})"
                : string.Empty;

            if (cost > budgetLeft)
            {
                item.Status = PortfolioItemStatus.OverBudget;
                item.Reasons.Add($"Its first-year cost{with} is {Money(cost)}; {Money(budgetLeft)} of the budget was left.");
                continue;
            }

            if (peopleLeft is { } people && effort > people)
            {
                item.Status = PortfolioItemStatus.OverPeopleCapacity;
                item.Reasons.Add($"Its effort{with} is {Number(effort)} person-days; {Number(people)} were left.");
                continue;
            }

            foreach (var member in bundle)
            {
                var selected = items[member];
                selected.Status = PortfolioItemStatus.Selected;
                selected.SelectionOrder = ++order;
                selected.Reasons.Clear();
                if (member != id)
                {
                    selected.SelectedFor = id;
                    selected.Reasons.Add($"Selected as a prerequisite of #{id}.");
                }
                selected.Reasons.Add(SelectedBecause(selected));
            }

            budgetLeft -= cost;
            if (peopleLeft is not null) peopleLeft -= effort;
        }

        // A prerequisite kept only for a dependent that was not funded: it stands on its own Gate C.
        foreach (var item in items.Values.Where(i => i.Status is null))
        {
            var gate = item.Candidate.GateC;
            item.Status = gate.Outcome == GateCOutcome.Fails ? PortfolioItemStatus.FailsGateC : PortfolioItemStatus.NotAssessable;
            item.Reasons.Add($"Considered only as a prerequisite of #{item.TierInheritedFrom}, which was not selected. " +
                             gate.Explanation);
        }

        // 9. The summary.
        var dtos = ids.Select(id => ToDto(items[id])).ToList();

        var selectedItems = dtos.Where(d => d.Status == PortfolioItemStatus.Selected).ToList();

        return new PortfolioSelectionDto
        {
            Budget = constraints.Budget,
            BudgetUsed = constraints.Budget - budgetLeft,
            PeopleCapacityPersonDays = constraints.PeopleCapacityPersonDays,
            PeopleUsed = constraints.PeopleCapacityPersonDays is null
                ? selectedItems.Sum(d => d.EffortPersonDays ?? 0m)
                : constraints.PeopleCapacityPersonDays.Value - peopleLeft!.Value,
            ExpectedReduction = selectedItems.Sum(d => d.GateC.Benefit ?? 0),
            SelectedWithUnknownBenefit = selectedItems.Count(d => d.GateC.Benefit is null),
            Considered = dtos.Count,
            Selected = selectedItems.Count,
            GateAShortfall = dtos.Any(d => d.Tier == PortfolioTier.Mandatory && d.Status != PortfolioItemStatus.Selected),
            ProtectedShortfall = dtos.Any(d => d.Tier == PortfolioTier.Protected
                                               && d.Status is not (PortfolioItemStatus.Selected or PortfolioItemStatus.NotApplicable)),
            EscalationsRequired = dtos.Count(d => d.RequiresEscalation),
            Items = selectedItems.OrderBy(d => d.SelectionOrder)
                .Concat(dtos.Where(d => d.Status != PortfolioItemStatus.Selected)
                    .OrderBy(d => d.Tier).ThenBy(d => d.MitigationId))
                .ToList()
        };
    }

    private static PortfolioTier OwnTier(PortfolioCandidate c) =>
        c.GateA ? PortfolioTier.Mandatory
        : c.Systemic || c.Tail ? PortfolioTier.Protected
        : PortfolioTier.Economic;

    private static void ApplyOwnEligibility(Item item, PortfolioConstraints constraints)
    {
        var c = item.Candidate;

        if (c.Option == TreatmentOption.Accept)
        {
            if (item.Tier == PortfolioTier.Mandatory)
                Exclude(item, PortfolioItemStatus.NotAssessable,
                    "Gate A holds, and 'accept' is not a legitimate treatment of a Gate A risk — declare another option.");
            else
                Exclude(item, PortfolioItemStatus.NotApplicable,
                    "The treatment option is 'accept': there is nothing to fund.");
            return;
        }

        if (c.Option is null)
            Exclude(item, PortfolioItemStatus.NotAssessable, "No treatment option is declared.");

        if (c.Cost is null)
            Exclude(item, PortfolioItemStatus.NotAssessable,
                "No monetary cost is declared, so it cannot be fitted to a budget (a missing cost is not a zero cost).");

        if (constraints.PeopleCapacityPersonDays is not null && c.EffortPersonDays is null)
            Exclude(item, PortfolioItemStatus.NotAssessable,
                "No effort estimate is declared, and the portfolio has a people capacity.");

        if (constraints.DeadlineDays is not null && c.DurationDays is null)
            Exclude(item, PortfolioItemStatus.NotAssessable,
                "No duration estimate is declared, and the portfolio has a deadline.");

        if (item.Status is not null || item.Tier != PortfolioTier.Economic) return;

        // Economic tier only: Gate C decides. In the mandatory and protected tiers it informs but never excludes —
        // E[L] understates exactly the tail and systemic risks the methodology asks to preserve.
        switch (c.GateC.Outcome)
        {
            case GateCOutcome.Fails:
                Exclude(item, PortfolioItemStatus.FailsGateC, c.GateC.Explanation);
                break;
            case GateCOutcome.NotAssessable:
                Exclude(item, PortfolioItemStatus.NotAssessable, c.GateC.Explanation);
                break;
            case GateCOutcome.NotApplicable:
                Exclude(item, PortfolioItemStatus.NotApplicable, c.GateC.Explanation);
                break;
        }
    }

    private static void Exclude(Item item, PortfolioItemStatus status, string reason)
    {
        item.Status ??= status;
        item.Reasons.Add(reason);
    }

    /// <summary>
    /// The sort key of the economic ranking: benefit ÷ annualized cost; a positive benefit at zero cost first; an
    /// unknown benefit last.
    /// </summary>
    private static double Priority(GateCResultDto gate)
    {
        if (gate.Benefit is not { } benefit || gate.AnnualizedCost is not { } cost) return double.NegativeInfinity;
        if (cost > 0) return benefit / cost;
        return benefit > 0 ? double.PositiveInfinity : 0;
    }

    /// <summary>The candidate and the not-yet-selected prerequisites it needs, prerequisites first.</summary>
    private static List<int> Bundle(int root, Dictionary<int, Item> items, Func<int, List<int>> prerequisitesOf)
    {
        var result = new List<int>();
        var seen = new HashSet<int>();

        void Visit(int id)
        {
            if (!seen.Add(id)) return;
            foreach (var p in prerequisitesOf(id))
                if (items.TryGetValue(p, out var prerequisite) && prerequisite.Status != PortfolioItemStatus.Selected)
                    Visit(p);
            result.Add(id);
        }

        Visit(root);
        return result;
    }

    /// <summary>Its duration plus the latest completion among its prerequisites; null when any is unknown.</summary>
    private static int? Completion(int id, Dictionary<int, Item> items, Func<int, List<int>> prerequisitesOf,
        Dictionary<int, int?> memo, HashSet<int> visiting)
    {
        if (memo.TryGetValue(id, out var known)) return known;
        var item = items[id];
        if (item.OnCycle || item.Candidate.DurationDays is not { } duration || !visiting.Add(id))
            return memo[id] = null;

        var latest = 0;
        foreach (var p in prerequisitesOf(id))
        {
            if (!items.ContainsKey(p)) { visiting.Remove(id); return memo[id] = null; }
            var day = Completion(p, items, prerequisitesOf, memo, visiting);
            if (day is null) { visiting.Remove(id); return memo[id] = null; }
            latest = System.Math.Max(latest, day.Value);
        }

        visiting.Remove(id);
        return memo[id] = latest + duration;
    }

    /// <summary>Members of a strongly connected component of size > 1 (iterative Tarjan, no recursion depth).</summary>
    private static HashSet<int> FindCycleMembers(List<int> ids, Func<int, List<int>> prerequisitesOf,
        Func<int, bool> isCandidate)
    {
        var index = 0;
        var indices = new Dictionary<int, int>();
        var low = new Dictionary<int, int>();
        var onStack = new HashSet<int>();
        var stack = new Stack<int>();
        var members = new HashSet<int>();

        List<int> Edges(int v) => prerequisitesOf(v).Where(isCandidate).ToList();

        foreach (var root in ids)
        {
            if (indices.ContainsKey(root)) continue;

            var work = new Stack<(int Node, int Next)>();
            indices[root] = low[root] = index++;
            stack.Push(root);
            onStack.Add(root);
            work.Push((root, 0));

            while (work.Count > 0)
            {
                var (v, next) = work.Pop();
                var edges = Edges(v);

                if (next < edges.Count)
                {
                    work.Push((v, next + 1));
                    var w = edges[next];
                    if (!indices.ContainsKey(w))
                    {
                        indices[w] = low[w] = index++;
                        stack.Push(w);
                        onStack.Add(w);
                        work.Push((w, 0));
                    }
                    else if (onStack.Contains(w))
                    {
                        low[v] = System.Math.Min(low[v], indices[w]);
                    }

                    continue;
                }

                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = System.Math.Min(low[parent], low[v]);
                }

                if (low[v] != indices[v]) continue;

                var component = new List<int>();
                int popped;
                do
                {
                    popped = stack.Pop();
                    onStack.Remove(popped);
                    component.Add(popped);
                } while (popped != v);

                if (component.Count > 1 || Edges(v).Contains(v))
                    members.UnionWith(component);
            }
        }

        return members;
    }

    private static string SelectedBecause(Item item)
    {
        var gate = item.Candidate.GateC;
        var gateNote = gate.Outcome == GateCOutcome.Fails
            ? " Gate C fails, which does not deprioritize it in this tier."
            : gate.Outcome == GateCOutcome.NotAssessable
                ? " Gate C is not assessable, which does not deprioritize it in this tier."
                : string.Empty;

        return item.Tier switch
        {
            PortfolioTier.Mandatory =>
                (item.Candidate.GateA
                    ? "Gate A holds: treatment is mandatory and is selected before any economic criterion."
                    : $"Mandatory as a prerequisite of a Gate A treatment (#{item.TierInheritedFrom}).") + gateNote,
            PortfolioTier.Protected =>
                (item.Candidate.Systemic || item.Candidate.Tail
                    ? $"{ProtectedLabel(item.Candidate)}: preserved ahead of the economic ranking, whatever its expected loss."
                    : $"Protected as a prerequisite of a tail or systemic treatment (#{item.TierInheritedFrom}).") + gateNote,
            _ => gate.BenefitCostRatio is { } ratio
                ? $"Passes Gate C with a benefit/cost ratio of {ratio.ToString("N2", CultureInfo.InvariantCulture)}."
                : "Passes Gate C at no annual cost."
        };
    }

    private static string ProtectedLabel(PortfolioCandidate c) => (c.Systemic, c.Tail) switch
    {
        (true, true) => "Systemic (flag 6) and tail (flag 8) risk",
        (true, false) => "Systemic risk or single point of failure (flag 6)",
        _ => "Tail risk — low probability, catastrophic impact (flag 8)"
    };

    private static PortfolioItemDto ToDto(Item item)
    {
        var c = item.Candidate;
        var reasons = new List<string>();
        if (item.TierInheritedFrom is { } from && item.Status != PortfolioItemStatus.Selected)
            reasons.Add($"Considered in the {item.Tier} tier as a prerequisite of #{from}.");
        reasons.AddRange(item.Reasons);

        return new PortfolioItemDto
        {
            MitigationId = c.MitigationId,
            RiskId = c.RiskId,
            RiskSubject = c.RiskSubject,
            Option = c.Option,
            Tier = item.Tier,
            Status = item.Status ?? PortfolioItemStatus.NotAssessable,
            SelectionOrder = item.SelectionOrder,
            SelectedFor = item.SelectedFor,
            Reasons = reasons,
            GateC = c.GateC,
            FirstYearCost = c.Cost?.FirstYear,
            EffortPersonDays = c.EffortPersonDays,
            DurationDays = c.DurationDays,
            CompletionDay = item.CompletionDay,
            PrerequisiteMitigationIds = c.Prerequisites.Distinct().OrderBy(p => p).ToList(),
            GateA = c.GateA,
            Systemic = c.Systemic,
            Tail = c.Tail,
            AboveAppetite = c.AboveAppetite,
            RequiresEscalation = c.AboveAppetite == true && item.Status != PortfolioItemStatus.Selected
        };
    }

    private static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

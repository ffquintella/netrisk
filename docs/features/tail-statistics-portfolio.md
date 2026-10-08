# Tail Statistics and Portfolio: P95, CVaR, Loss Decomposition, Correlation and Gate B

Track 9, Stage 9.7 (M45). The outputs MIGR-TI/IA Phase 3 asks of a quantitative analysis — **P95**, **CVaR** and
**confidence intervals** —, the loss magnitude **decomposed by form of loss**, the **portfolio aggregation** with a
declared **correlation** between scenarios that Phase 7's "aggregate exposure above appetite" needs, and **Gate B**
comparing the appetite against the tail. Flag 8 ("low probability, catastrophic impact") becomes derivable.

Full specification, with every decision and its rejected alternative:
[S48 — docs/roadmap/track9/9.7-tail-statistics-portfolio.md](../roadmap/track9/9.7-tail-statistics-portfolio.md).

Related: [Risk governance §8 (FAIR-lite scoring)](risk-governance.md) · [Mandatory flags and Gate A](risk-flags-gate-a.md) ·
[Treatment economics: Gates C and D](treatment-economics.md)

---

## 1. Tail statistics of every run

Every FAIR-lite computation (`POST /Risks/{id}/Quantitative`, and the nightly 02:20 recomputation) now stores, for the
**inherent** run and — when the latest mitigation has an effectiveness above zero — the **residual** run, a row in
`risk_tail_statistics`:

| Statistic | Estimator | 95 % confidence interval |
|---|---|---|
| E[L] (mean annual loss) | the mean | mean ± 1.96·s/√n |
| **P95** | the same interpolated percentile as P10/P50/P90 | order statistics (distribution-free, no random numbers) |
| **CVaR95** (expected shortfall) | mean of the worst 5 % of simulated years (Acerbi–Tasche) | percentile bootstrap, 1 000 replicates, seeded from the run's seed |
| Probability of a loss year | fraction of years with any loss | — |
| Mean loss of a loss year | E[L \| L > 0] | — |

The intervals measure **Monte Carlo error** — how precisely the iterations pin the number given the calibrated ranges —
not how good the ranges are. They shrink with more iterations.

**Why CVaR matters:** a scenario that loses 4 M in about 3 % of years has P10 = P50 = P90 = **P95 = 0**, and a CVaR95 of
about 2.7 M: the worst 5 % of years contain the loss years. A tolerance on P95 alone would never see it.

The rows live in their own table, not in `risk_scoring`: `PUT /Risks/{id}/Scoring` copies the whole scoring payload
and blanks any column a client does not send (a pre-existing defect, unchanged). Each row keeps a copy of the inputs it
simulated, so the portfolio reproduces exactly the stored run.

Iterations: at most **100 000** per request (400 above; values below 1 000 are still raised to 1 000). The
`quantitative_iterations` setting is clamped to the same range.

## 2. Loss magnitude by form of loss

`PUT /TailRisk/Risks/{id}/LossComponents` declares the per-event loss by component — **response, recovery, productivity,
revenue, liability, fine, reputation** — each a calibrated min / most likely / max range. A **fine requires its legal
basis** (Phase 3: "fines when legally applicable").

- With components, each event's loss is the sum of one independent PERT draw per component; the single magnitude range
  of the analysis becomes their **envelope** (Σ min, Σ most likely, Σ max).
- Declaring or removing components **recomputes** the analysis in the same request when one exists.
- Each run reports every component's contribution to the **E[L]** and — by Euler allocation — to the **CVaR95**; both
  add up to the run's totals. A component that carries more of the CVaR than of the E[L] is what the tail is paying
  for (typically the fine or the liability).
- With components declared, a quantitative request whose magnitude range differs from the envelope is refused (400):
  a magnitude is never declared two ways that disagree. Re-sending the stored analysis sends the envelope and passes.
- Without components, every number is exactly what it was before this stage (same draws, same results).

## 3. Portfolio aggregation with correlation

`PUT /TailRisk/Correlations` declares the correlation (**0 to 1**, three decimals, with a rationale) between the annual
losses of two scenarios. A declaration that would make the correlation matrix of its connected group **not positive
semidefinite** is refused with `422 correlation_not_positive_semidefinite` — never repaired. The check is
organisation-wide; the message names no risk.

`POST /TailRisk/Portfolio` (computed, never stored) aggregates a set of risks — the ids given (≤ 500), or every visible
open risk, optionally of one entity — on the **residual** basis (residual run where it exists, inherent otherwise) or
the **inherent** one:

- **Method:** Gaussian copula by rank reordering (Iman–Conover). Each risk's stored run is re-simulated exactly and only
  its order across iterations changes, so every marginal is preserved.
- **No correlation declared → independence**, said explicitly (`Dependence = AssumedIndependent`).
- **What adds up and what does not:**

  | | Portfolio vs. sum of the risks |
  |---|---|
  | E[L] | **equal**, always |
  | P95 | **neither** — below the sum when risks diversify, *above* it for rare risks (two 4 %-a-year scenarios have P95 = 0 each, the pair does not) |
  | CVaR95 | **at most** the sum — Σ CVaR95 is an upper bound under any dependence |

- The result carries Σ E[L], Σ P95, Σ CVaR95, the diversification (Σ CVaR95 − portfolio CVaR95), each risk's share of the
  portfolio CVaR95, the risks **not quantified** (listed, never summed as zero), and Gate B on the portfolio.
- The same risks, statistics and **seed** give the same aggregate, in any order.

## 4. Gate B on the tail

Administrators set monetary tolerances on an appetite with `PUT /RiskAppetites/{id}/TailLimits` — per scenario (E[L],
P95, CVaR95) and per portfolio — with a rationale (the Phase 0 decision). They belong to that appetite row: an entity's
appetite without tail tolerances does not inherit the organisation-wide ones.

`GET /Risks/{id}/Appetite` gains a `Tail` block with one of four states:

| State | Meaning |
|---|---|
| `NotConfigured` | no appetite, or no tail tolerance on it |
| `NotAssessable` | a tolerance exists but the risk has no tail statistics (not quantified, or computed before schema 96) — **never read as within** |
| `WithinTolerance` | every tolerance holds |
| `ExceedsTolerance` | a statistic is above its tolerance — treat or escalate |

The residual run is compared where it exists, the inherent otherwise. `Marginal` warns when a tolerance lies inside a
confidence interval. An acceptance or renewal of a risk whose tail exceeds the tolerance is refused with
`422 risk_appetite_tail_tolerance` — **after** Gate A, segregation of duties, the approval band and the ordinal ceiling,
which keep their rules and their order. `NotAssessable` does not refuse.

For a portfolio, an excess on the quantified risks is conclusive; "within" with unquantified risks is `NotAssessable`
(`IncompleteCoverage`, "n of N quantified").

## 5. Flag 8 derived from the tail

Flag 8 is derived on the **inherent** run when the annual probability of a loss year is at most
`tail_flag_max_annual_probability` (default **10 %**) **and** the mean loss of a loss year is at least
`tail_flag_catastrophic_loss` (default: the top `quantitative_band_thresholds` value, 1 000 000). It is reconciled like
flags 3, 4 and 5 — nightly at 05:40, on `POST /RiskFlags/Risks/{id}/Refresh` and before every Gate A check — and reverts
with an audit-trail entry when its basis is lost. Computing an analysis does not reconcile it immediately. Flag 8 is not
Gate A; Gate D already protects it.

## 6. Permissions

| Action | Policy |
|---|---|
| Read a risk's tail, the correlations, the portfolio | `RequireRiskmanagement` |
| Declare or remove loss components and correlations | `RequireSubmitRisk` (the audience that computes the analysis) |
| Set, read or remove an appetite's tail tolerances | `RequireAdminOnly` (the appetite's own policy) |

Everything is entity-scoped: another unit's risks are not found, a correlation is visible only when both its risks are,
and a portfolio holds only the risks the caller can see. Components, correlations and tolerances are in the governance
audit trail with the person who declared them; the computed statistics are not.

## 7. Known limitations

- The desktop screens (components and contributions in the quantitative editor, the tail and Gate B on the risk detail,
  the correlation editor, the tail tolerances in the appetite administration and the portfolio screen) are **T305**; this
  release ships the API and the REST client.
- The confidence intervals are Monte Carlo error only; the uncertainty of the calibrated ranges is not modelled.
- Components of one event are sampled independently; the Gaussian copula has no asymptotic tail dependence. Σ CVaR95 is
  shown as the bound that holds under any dependence.
- Tail statistics appear on the first recomputation after the upgrade (the 02:20 job, or a manual computation); until
  then Gate B on the tail reads `NotAssessable`.
- Gate B by KRI — unavailability, data loss, number of data subjects or another indicator — arrived with Stage 9.8
  ([KRIs, reassessment triggers and metrics](kri-reassessment-metrics.md)); it is checked after the tail.

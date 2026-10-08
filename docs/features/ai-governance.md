# AI Governance: Model Inventory, Flag 11 and Model Metrics

Track 9, Stage 9.12 (M50). It gives MIGR-TI/IA Phase 6 its instrument. Each **AI model** the organization uses — a
chatbot, an admissions triage, a plagiarism detector, a vendor's LLM — gets an **inventory record**: its **purpose**, the
**data** it uses, its **vendor**, its **version**, the risk tier and human oversight the organization declares, and an
accountable **owner**. The register's risks that involve a model are linked to it and get **flag 11**; the model's
**metrics** — accuracy, precision, recall, calibration, drift and the **human override rate** — are recorded as readings
of a version, with an explicit **"not evaluated"**.

**Governance, never use.** Nothing in NetRisk runs a model, takes a model's output as a decision, or lets a model act. A
model has no credential, no user and no route that acts for it. Adding AI to the risk workflow is out of scope: the
methodology wants the governance instrument to exist *before* any use.

Full specification, with every decision and its rejected alternative:
[S53 — docs/roadmap/track9/9.12-ai-governance.md](../roadmap/track9/9.12-ai-governance.md).

Related: [LGPD data catalogue](lgpd-data-catalogue.md) · [Third-party register](third-party-register.md) ·
[Mandatory flags and Gate A](risk-flags-gate-a.md) · [KRIs and the methodology's metrics](kri-reassessment-metrics.md)

---

## 1. The inventory

`POST /AiModels` and `PUT /AiModels/{id}` (permission `ai_governance_manage`, within the model's own unit) register a
model or replace its record: name (unique), **purpose**, kind, source (in-house, vendor, open source), the **vendor** — a
registered third party, only for a vendor model —, the **version** in use, the status (proposed, pilot, production), the
**risk tier** (minimal, limited, high), the **human oversight** (every output reviewed, a sample, none), the **owner** — an
enabled user, never the third line — and how many days an evaluation stays current. `PUT /AiModels/{id}/Data` declares,
whole, the data records the model uses and how (training, fine-tuning, evaluation, input, output); an empty list is a
declaration. The data is the `organizationData` node the LGPD catalogue is keyed by, so the model shows whether each record
is catalogued and its personal-data category. When the version changes, the record notes since when the new one is in
use (or takes the declared date), and a reading or override from before it is refused — it is not of this version.

A model is **retired** with a reason (`POST /AiModels/{id}/Retire`), never deleted; a retired model is frozen. Free text
carrying an e-mail address or a formatted CPF is refused without being echoed.

## 2. Metrics — and "not evaluated"

`POST /AiModels/{id}/Readings` records a reading of the model's **current version**: accuracy, precision, recall and
calibration as fractions, drift as a statistic, with how and on what it was measured and where the report is. A reading
is never edited — a mistaken one is voided with a reason (`…/Readings/{id}/Void`).

What a model must report follows its tier: **drift** for every model, **accuracy** from limited up, **precision, recall
and calibration** for high (or undeclared), and the **human override rate** whenever people review its outputs (or
nobody declared whether they do). For the current version each metric reads **not evaluated** (no reading — no value,
never zero), **evaluated** or **stale** (older than the declared maximum age), and the model reads not evaluated,
incomplete, stale or evaluated. **A model with no recorded evaluation is not evaluated** — never a pass by omission — and a
new version starts not evaluated.

**The human override rate is computed, never typed.** `POST /AiModels/{id}/Overrides` records a person's decision contrary
to the model — what the model proposed, what the person decided, and why — with the caller as its author. A reading of the
override rate gives the period and how many outputs people reviewed in it; the server counts the live overrides of the
current version in the period and divides.

## 3. The register's risks and flag 11

`PUT /AiModels/{id}/Risks/{riskId}` (policy of the linkage chain) links a risk of the register to a model; the link to a
model that is not retired **derives flag 11** at the next reconciliation (nightly, on `POST /RiskFlags/Risks/{id}/Refresh`,
and before every Gate A), and retiring the model or removing the link reverts it with the system's trail. Flag 11 is not
a Gate A condition: it classifies, it decides nothing. Its basis names the model by id and status only, because whoever
reads the risk may not see the model. `GET /AiModels/Risks/{riskId}` shows the models a risk involves.

## 4. Findings

Computed on read: no owner, undeclared tier or oversight, no review of a high-risk model's outputs, a vendor model with no
registered vendor, data never declared or not catalogued, a model in use with no risk in the register, and a model in use
**not evaluated**, incompletely evaluated or with a stale reading. `GET /AiModels?withFindings=true` lists them.

## 5. The methodology panel — M10

`GET /Monitoring/Metrics` now computes M10: the share of the models in use (pilot or production) evaluated on every metric
their tier requires, for their current version — a model with no evaluation counts as not evaluated —, with the coverage
of each metric and the overrides of the last 90 days.

## 6. Security and audit

- Reads: `RequireAiGovernanceRead` (the risk register's audience or the inventory's maintainers); writes: the new
  `ai_governance_manage` permission, granted to nobody by the upgrade; the third line reads and never writes.
- Every write needs the model's own unit in the caller's scope; the organization's models are written with global scope.
- Every change is in the field-level trail, read through `GET /AiModels/{id}/History`; the generic `/AuditTrail/{type}/{id}`
  refuses the five AI governance types.
- The **Phase 6 prohibitions** — AI does not accept residual risk, approve exceptions or close findings — are under test:
  every API action outside the anonymous allowlist and the SCIM provisioning controller refuses a principal that is not a
  user, and every approver a request names must be one
  (`NonUserApprovalInventoryTest`, `AiAuthorityProhibitionsInMemoryTest`). Two older gaps found while checking them are
  recorded in S53 §11: an API token decides in its owner's name — and, because its name claim is the display name matched
  against logins, possibly in another user's —, and `POST /MgmtReviews` does not apply segregation of duties.

## Limitations

- No desktop screen yet (T310).
- NetRisk records evaluations measured elsewhere; it does not run tests, red teams or bias assessments. Bias, robustness and
  explainability are referenced as evidence on a reading, not metrics of their own.
- No tolerance per metric; a KRI can track one.
- Declaring a new model does not raise the "new AI model" reassessment trigger; declare the event.

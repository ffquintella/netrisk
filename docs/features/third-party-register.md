# Third-Party Register: HECVAT, SBOM, Sub-processors, Contract Terms, Concentration and Exit Plan

Track 9, Stage 9.10 (M48). It gives MIGR-TI/IA's discovery front E — third parties — the instrument it never had. A
**supplier** (a SaaS vendor, a cloud, an identity provider) is a first-class record, not a generic `organization` node:
its contract, SLA and contracted RTO/RPO, the right to audit, the exit plan and data portability, the **HECVAT** it
answered, the **SBOM** of what it supplies, its **sub-processors** and **where the data is**. The register measures
**concentration** by supplier, cloud and identity, counting a supplier once per critical process that depends on it.

Full specification, with every decision and its rejected alternative:
[S51 — docs/roadmap/track9/9.10-third-party-register.md](../roadmap/track9/9.10-third-party-register.md).

Related: [Business continuity](business-continuity.md) · [KRIs and the methodology's metrics](kri-reassessment-metrics.md) ·
[Mandatory flags and Gate A](risk-flags-gate-a.md) · [Archive, backtesting, committee and third line](archive-backtesting-committee.md)

---

## 1. The record

`POST /ThirdParties` and `PUT /ThirdParties/{id}` (permission `third_party_manage`) register a supplier:

- identification — name (unique in the organization, case-insensitively), legal name, tax id, country, website (an
  http(s) URL a client may open), description, the **unit** it belongs to (or none: the organization's) and the
  **owner** of the relationship — an enabled first-line user, never the third line;
- the state of the relationship — **prospective**, **active**, **exiting** or **terminated**;
- whether it is a **cloud provider** and/or an **identity provider**, and whether it processes **personal data**;
- the contract — reference, start and end, **SLA availability**, **contracted RTO and RPO**, the contracted deadline to
  fix a medium-or-higher vulnerability, the **right to audit** and its clause;
- the **exit plan** (when it was reviewed and when it was exercised) and **data portability**.

A missing value is never read as compliant: an RTO that is not contracted is not 0, an undeclared right to audit is not a
granted one.

**A supplier in use is never deleted.** `DELETE /ThirdParties/{id}` deletes only a supplier nothing refers to. One that
is linked to something, has a HECVAT or an SBOM, or is named as a sub-processor by another supplier answers
`422 third_party_in_use`; end the relationship by setting it to **terminated** instead, which keeps the record.

## 2. What it supplies or processes

`PUT /ThirdParties/{id}/Links/{entityId}` links the supplier to:

- an **IT service** (Stage 9.1) it supplies;
- a **business process** it operates (an outsourced process);
- a **data record** (`organizationData` or `organizationDataGroup`) it processes — the record the Stage 9.11 LGPD
  catalogue extends.

The kind is derived from the entity. `GET /ThirdParties/ByEntity/{entityId}` reads the link from the other side: who
supplies this service, who processes this data.

## 3. HECVAT, SBOM, sub-processors and data location

**HECVAT.** `POST /ThirdParties/{id}/Assessments` records a questionnaire the vendor answered — variant, version and
**how many questions the vendor was asked** — and `PUT …/Assessments/{assessmentId}/Answers` its answers (yes, no, N/A or
left blank, each with the preferred answer, a weight and whether it is critical). NetRisk ships none of HECVAT's questions.
The result is computed on read:

| State | When |
|---|---|
| **Incomplete** | fewer questions answered than asked, or any left blank — **no score is shown, and it never passes** |
| Conforming | complete, at least 80 % of the scored weight as preferred, no critical question against the preference |
| Non-conforming | complete, below 80 % or a critical question against the preference |
| Expired | complete but past its validity date — never a pass |
| Not scorable | complete, but nothing in it has a preferred answer |
| Voided | voided with a reason (`…/Void`) — kept as evidence |

**SBOM.** `POST /ThirdParties/{id}/Sboms` imports a **CycloneDX or SPDX JSON** document sent as text. NetRisk never
fetches an SBOM from a URL. The document is handled as hostile input: at most 5 MiB, 64 levels of nesting and 10 000
components — a larger one is refused, never truncated — and XML is refused unparsed. Components are stored without control
characters; the document itself is not kept, only its SHA-256, its size and the file name as metadata.

**Sub-processors and data location.** `PUT /ThirdParties/{id}/Subprocessors` declares the whole sub-processor list —
an empty list is a declaration ("none"). A sub-processor may itself be a registered third party.
`PUT /ThirdParties/{id}/DataLocations` declares where the data is stored, processed, backed up and reached for support,
by country.

## 4. Findings

`GET /ThirdParties/{id}` lists what the register does not yet know or what does not hold, computed on read: the HECVAT
missing, incomplete, non-conforming or expired; personal data, sub-processors or data location not declared; no right to
audit; no exit plan, an exit plan never exercised by a supplier of a critical process, no data portability; **an RTO or
RPO not contracted, or looser than what the BIA requires** of the services and processes it supplies; a
vulnerability-fix deadline over 30 days (FGV NRM §5.2) or undeclared; a contract that ended while the supplier is still
active; no SLA. A finding refuses nothing.

## 5. Concentration

`GET /ThirdParties/Concentration` measures, for each supplier in use, **how many active critical processes depend on
it — each counted once**, however many services, links or paths lead to it. Depending means: the supplier supplies a
service or process (while the relationship is active or exiting) and the process depends on it in the **declared BIA
cascade** (Stage 9.3) — or, through a supplier that names it as a sub-processor, the fourth-party path. The report has
three dimensions — **supplier**, **cloud** and **identity** — with each supplier's share of the critical processes and the
most concentrated one. A supplied service with no dependent declared in the BIA counts nothing, and the report says how
many there are.

A reader sees the suppliers in their scope; each count is computed over the whole organization, so it never depends on
who asks. The methodology metrics panel now computes **M8** from this report.

Flag 7 ("concentration in a third party, cloud or identity") stays **declared**: the report is the evidence for whoever
declares it.

## 6. Who reads and writes

Reads (`RequireThirdPartyRead`): whoever reads the risk register (`riskmanagement`, the `Administrator` role, the third
line) and whoever manages the register. Every write needs the new permission `third_party_manage` (administrators hold it
by construction). The third line reads and is refused every write. A supplier of a unit needs that unit in scope; the
organization's suppliers need global scope.

History is read through `GET /ThirdParties/{id}/History`, which checks the supplier is visible first; the generic
`/AuditTrail/{type}/{id}` refuses the third-party types, because it cannot apply the caller's scope.

The desktop screens are planned as T308.

# LGPD Data Catalogue: Legal Basis, Purpose, Retention, Location, Transfer, RIPD and Legal Requirements

Track 9, Stage 9.11 (M49). It makes LGPD compliance demonstrable for MIGR-TI/IA's discovery front C — data. Each
**data record** of the entity map (`organizationData`) gets a **catalogue**: whether it holds personal or sensitive
personal data, **why** (each purpose with its **legal basis** under LGPD art. 7 or art. 11), **for how long** (retention),
**where** (countries, and the international transfer with its art. 33 safeguard) and **which RIPD** (DPIA) covers it.
The register's legal and contractual **requirements** become a catalogue of their own, linked to risks instead of
written as text.

**A catalogue of kinds of data, never of data.** It says "student health records are sensitive personal data, processed
for student care under art. 11, II, f, kept for 60 months after the end of enrolment, in Brazil". It stores no name, CPF
or e-mail of anyone, and nothing in NetRisk holds the data it describes. A free text that carries an e-mail address or a
formatted CPF is refused, and no log line echoes the catalogue's text.

Full specification, with every decision and its rejected alternative:
[S52 — docs/roadmap/track9/9.11-lgpd-data-catalogue.md](../roadmap/track9/9.11-lgpd-data-catalogue.md).

Related: [Third-party register](third-party-register.md) · [Mandatory flags and Gate A](risk-flags-gate-a.md) ·
[Archive, backtesting, committee and third line](archive-backtesting-committee.md)

---

## 1. The catalogue of a data record

`PUT /DataCatalogue/Records/{entityId}` (permission `data_catalogue_manage`, **global scope**) catalogues an
`organizationData` node, or replaces its catalogue whole:

- the **personal-data category** — not personal, personal, **sensitive personal** (art. 5, II), anonymised —, and whether
  it involves **children or adolescents**, a **large volume** or **strategic research**;
- the categories of data subjects and of data held — kinds, never values;
- the **purposes**, each with its **legal basis** (one of the ten incisos of art. 7 or the eight of art. 11), the
  catalogued requirement it rests on and a reference to the consent model, the legitimate-interest assessment or the
  contract;
- **retention** — the period, the event it runs from, its basis and requirement, and the date the retention must be
  reviewed;
- **locations** by country and use (storage, processing, backup, support access), and the **international transfer** with
  its safeguard.

Both lists are required: an empty list declares "none", and a client that does not send them cannot clear them by
omission.

## 2. Findings — what the catalogue is missing

`GET /DataCatalogue/Records?withFindings=true` lists every data record with its findings, computed on read:

| Finding | When |
|---|---|
| Not catalogued | the record has no catalogue — which is not "no personal data" |
| Personal data undeclared | the category is not declared |
| Purpose / legal basis missing | personal or sensitive data with no purpose, or a purpose with no legal basis |
| Basis not valid for sensitive data | sensitive data under a basis of art. 7 (legitimate interest, credit protection…) |
| Legal obligation unnamed / legitimate interest unassessed | the obligation cites no requirement; the balancing test is not referenced |
| Retention undeclared / **expired** | no period nor review date; the review date has passed |
| Location undeclared, transfer undeclared, safeguard missing | including the countries of the record's live processors and their sub-processors |
| RIPD missing, overdue, high residual risk | for sensitive data, data of minors or a large volume |

**An expired retention signals; nothing is ever deleted.** Eliminating the data subject's data is the controller's
decision, in the system that holds it. No route deletes a catalogue, reading writes nothing, and no background job writes
or deletes it — the nightly flag reconciliation only reads it, for flag 5.

## 3. Processors and transfer

The processors of a record are the third parties linked to it — or to its data group — in the
[third-party register](third-party-register.md). A processor the reader cannot see is counted, never named. The countries
the data reaches through processors are computed over the whole organization, so a transfer finding never depends on who
reads it.

## 4. The RIPD (DPIA)

`/DataCatalogue/Dpias` records a RIPD as an artifact: title, summary, the reference to the document, the residual risk,
when it was performed and when it is due for review. It is linked to the **data records** and the **business processes**
it covers. It is approved only when complete — performed, with a residual risk, a summary or a document, and at least one
data record — by the person who approves, **never the third line**. Approved, it is frozen: a review is a new RIPD, and the
old one is retired with a reason. A RIPD is never deleted.

## 5. Legal requirements and the risk register

`/DataCatalogue/Requirements` catalogues laws, regulations, contracts (optionally naming the third party) and internal
norms. Purposes and retention cite them, and `PUT /DataCatalogue/Risks/{riskId}/Requirements/{requirementId}` links them
to a risk (policy `RequireRiskmanagement`, like the linkage chain). `GET /DataCatalogue/Risks/{riskId}` shows the risk's
requirements, the data records it reaches through its chain with their findings, and the requirements those records cite.

A requirement in use — by a purpose, a retention or any risk, visible or not — answers `422 legal_requirement_in_use`;
a third party named as a contract's counterparty is in use and is not deleted.

## 6. Flags 2 and 5

- **Flag 5** is derived from the catalogue too: a risk linked to data catalogued as sensitive, as a large volume of
  personal data or as strategic research carries it; removing the mark reverts it on the next reconciliation, with the
  system's entry in the audit trail.
- **Flag 2** stays **declared**. It is a Gate A condition with no break-glass; deriving it from the catalogue would let
  whoever edits a legal basis switch Gate A on or off without the segregation of duties that withdrawing a Gate A
  declaration requires. The risk's requirements and its data findings are the evidence for whoever declares it.

## 7. Access

| Action | Who |
|---|---|
| Read the catalogue, the requirements and the RIPDs | `Admin` or `Administrator`, `riskmanagement` (the third line included), or `data_catalogue_manage` |
| Write the catalogue, the requirements and the RIPDs | `data_catalogue_manage` (or `Admin`) **with global scope** |
| Read and link a risk's requirements | `riskmanagement`, within the risk's entity scope |

The third line reads and writes nothing. The audit trail of the catalogue is read through
`/DataCatalogue/Records/{id}/History`, `/DataCatalogue/Dpias/{id}/History` and `/DataCatalogue/Requirements/{id}/History`;
the generic `/AuditTrail/{type}/{id}` refuses these types.

The desktop screens are task T309; this stage delivers the API and the REST client.

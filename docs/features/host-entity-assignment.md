# Host attribution: assessment date, entity assignment and code-scanner targets

M58, T281–T282 — corrects at the source the asset data the risk index ([S39](../methodology/icr-indice-consolidado-de-risco.md))
read with a workaround: defects D-14, D-18, D-20 and D-21 of its §14 register.

## `hosts.last_assessed_at` (T281, D-18)

`hosts.last_verification_date` says a host *exists*: the Jira Assets import, the Vision One inventory and CVE feed,
the SecurityScorecard pseudo-host and every finding import stamp it. `last_assessed_at` says a *scanner looked at it*.

- Written **only** by `FindingIngestionService`, for importers `HostAssessmentPolicy.StampsLastAssessed` accepts: every
  file importer (Nessus, OpenVAS, ZAP, SARIF, …) and the Tenable VM feed. The two feeds the index reads on their own
  clocks are excluded — `trendmicro-visionone` (the device's detection feed) and `securityscorecard`.
- UTC, the import's time, never moved backwards by a late re-import of an older report.
- The hosts API ignores it on the way in (`HostsService.StampServerOwnedFields`), so neither a forged body nor the
  stale copy a dialog loaded can set or roll it back. It is not in the field-level audit (a churn column, like the
  verification date).
- Not backfilled (version 115): it stays NULL until the next scan; the index keeps its §8.2 interim rule for a NULL.
- Known limit: the importer contract carries findings, not the hosts a scan covered, so a host whose only results were
  filtered as negligible (a clean scan) is not stamped. Closing that needs an "assessed hosts" list in the plugin SDK's
  `ImportResult`.

## Finding hosts matched by external id first (T281, D-14)

A connector that knows the asset's own id passes it with the finding's host (`IdentifiedHost`): Vision One CVEs carry the
device id, Tenable findings the asset uuid. The pipeline looks the host up by that id — `host_external_ids`, then
`hosts.external_provider`/`external_id` — before any address, and only then by IP, then by name or FQDN. File importers
carry no id and match as before. The pipeline reads identities; the inventory syncs own them.

## Host → entity assignment and the re-stamp policy (T282, D-20)

**Who wrote the entity** is recorded in `hosts.entity_source`, ranked like `criticality_source` (T275):

| Source | Rank | Written by |
|---|---|---|
| `manual` | 4 | A person: `PUT`/`POST /Hosts` with `entityId`, or the entity picker of the desktop host dialog |
| `cmdb` | 3 | A Jira Assets connection's target entity |
| `rule` | 2 | A vendor tag → entity rule (T263) |
| `visionone` | 1 | A Vision One connection's target entity |
| `import` / NULL | 0 | The importer's scope when a scan created the host / unknown (written before version 115) |

**The rule.** Each Vision One and Jira Assets connection has `entity_restamp_policy`:

- `OnlyUnassigned` (1, default) — a matched host with **no** entity takes the connection's target; any other host keeps
  its entity.
- `FollowMapping` (2) — a matched host in another entity is also moved to the target, **but only when the connection
  ranks at least as high as the current writer**. A manual assignment is never moved by any sync; a CMDB assignment is
  not moved by Vision One. Every move is an ordinary write of `hosts.entity_id`, recorded in the Host audit under the
  sync's actor.
- A connection without a target entity attributes nothing. On Vision One a winning tag rule is applied instead of the
  connection's target (rule, then target, then unassigned — S40 §6.6), and a `FollowMapping` rule now obeys the same
  precedence: it no longer moves a manual or CMDB assignment.

Equal ranks may overwrite each other, so two connections of the same kind with different targets and `FollowMapping`
would alternate; give overlapping connections `OnlyUnassigned`.

**Through the API.** `PUT /Hosts/{id}` with an `entityId`: a new or changed entity is recorded as `manual`, an unchanged
one keeps its writer, and clearing it clears the writer (the host goes back to the connections). The body's own
`entitySource` is never trusted. A nonexistent entity is a 400; an entity outside the caller's scope is refused by the
DAL write guard and answered 403 by `EntityScopeViolationMiddleware`. `GET /Hosts/Filtered?filters=entityId==null`
lists the unassigned (`entityId`, `entitySource` and `lastAssessedAt` are filterable).

**Backfill report.** `GET /Hosts/Unassigned?limit=1000` (permission `hosts`, at most 5000 rows) lists the hosts with no
entity the caller can see — a scoped caller sees none — with the target entity of the Jira Assets (preferred) or Vision
One connection that reports each one (`suggestedEntityId`, `suggestedBy`). Assign them through `PUT /Hosts/{id}`.

Existing `entity_source` values are not backfilled: `hosts.source = 'manual'` does not prove a person chose the entity
(the desktop dialog wrote it on every save and had no entity field). Until re-assigned, those hosts rank 0.

## Code-scanner imports: target application and repository (T282, D-21)

`POST /Vulnerabilities/import/{importer}` and `POST /Vulnerabilities/import/{importer}/{fileId}` accept:

- `entityId` — the target application (any entity; typically an `application`). Refused with 403 when the caller's
  scope does not cover it (checked before existence, so a scoped caller learns nothing about other entities) and 400
  when it does not exist. Without it, the caller's own scope applies as before. Stored on `scan_imports.entity_id` and
  on the findings.
- `repository` — the repository or project, at most 255 printable characters. Stored on `scan_imports.repository` and on
  the import's **host-less** findings (`vulnerabilities.repository`).

The repository is folded into every dedup key of a host-less finding, whatever the strategy (built-in or plugin), so
the same rule on the same path in two repositories stays two findings. An import naming no repository keeps every key
exactly as it was. A finding imported before repositories were named is **adopted** by the first repository that
reports it again (it takes that repository's key and, if it had none, the import's target application); a second
repository gets its own. A full scan auto-closes only the findings of its own repository.

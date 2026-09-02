# Jira Service Management & Assets

> Track 4 milestone 4.6. Builds on [issue-tracker synchronization](issue-tracker-sync.md), which
> already creates and syncs Jira *Software* issues from findings.

Three things this adds to the Jira integration: the **Service Management** read surface, an **Assets**
import for the CMDB registers that describe applications, servers and machines, and a
**configuration screen where the mappings can actually be edited**.

```
                        ┌── service desks / request types / queues ──► read live
Jira site ──────────────┤
 /rest/servicedeskapi   └── customer requests + SLA cycles ──────────► mirrored

api.atlassian.com ────────► Assets objects (AQL) ──► attribute mapping ──┬──► hosts
 /jsm/assets/workspace/…                                                 └──► application entities
```

## One connection, three facets

Service Management and Assets are **not a separate connection**. A JSM service desk is a Jira project
on the same site, reached with the same credential, so `IssueTrackerProviderKind.Jira` gains a 1:1
extension row (`jira_connection_settings`) instead of the integration gaining a fifth provider. A
separate provider would have meant entering the same API token twice, and splitting one ticket's links
across two connections — which would give the sync engine two tables to reconcile.

The extension is a table rather than more columns on `issue_tracker_connections` because GitHub, GitLab
and Azure DevOps have no service desk and no CMDB, and fifteen always-null columns on a shared table is
how a generic table stops being generic.

| Setting | Notes |
|---|---|
| Deployment | **Cloud** or Data Center. Only Cloud is implemented; a Data Center connection is refused at save |
| Service desk | Picked from the site's own list |
| Request-type filter | Comma-separated ids. Empty means every type |
| Queue imports | Which queues feed the mirror, with a per-queue ceiling (clamped to 1–5000) |
| Assets workspace | **Discovered**, never typed |
| Assets schema | Picked from the workspace's own list |

**Why Data Center is refused rather than half-supported.** Assets on Cloud is served from
`api.atlassian.com/jsm/assets/workspace/{id}/v1`; Data Center's equivalent is Insight at
`/rest/insight/1.0/` on the site, with a different object model. Pointing the Cloud client at a Data
Center site produces 404s that read as "your credentials are wrong", and an operator would rotate a
token that was never the problem.

## Service Management, read-only

NetRisk writes to Jira through the existing issue-tracker provider, which already carries the
operator's status mapping and its loop protection. There is no method in the JSM client that writes to
a service desk — a second write path would be a second place for that policy to live.

**Read live:** service desks, request types, and queues (with the issue count Jira advertises, so a
queue can be sized before it is imported).

**Mirrored:** the requests NetRisk cares about — those in a configured queue, plus every request
already linked to a NetRisk record regardless of queue configuration, because a link is a stronger
statement of interest than a queue selection.

Queues themselves are deliberately **not** mirrored. A queue is a saved JQL filter whose membership
changes on every triage action, so a stored copy of it is wrong the moment it is written.

### SLA

Stored as **one row per cycle per metric**, in columns.

* **Columns, not a JSON blob**, because "what is breaching this week" and "which metric breaches most
  often" are the only questions this data is ever asked, and a blob answers neither without a full
  scan and a parse per row.
* **Per cycle, not per metric**, because a reopened request starts a second cycle of the same metric.
  Keying on `(request, metric)` alone would overwrite the first cycle's breach with the second cycle's
  clean state, and the breach would vanish from the record.

A **new** breach raises `jsm.sla_breached` through the Track 4.1 dispatcher — once per
`(request, metric, cycle)`. Re-notifying every sync for a cycle that breached last week is how a
channel gets muted, and a muted channel is worse than none. The notification links to the Jira portal
rather than to a NetRisk route: whoever has to act on a service-desk breach acts on it in the service
desk.

The mirror runs on the connection's existing `poll_interval_minutes`, in the same recurring job as the
issue-link poll, so an operator has one schedule per connection rather than two that could disagree
about a queue feeding a linked finding. The two passes are caught separately: a broken Jira facet does
not cost the other three providers their poll.

## Assets import

Assets objects of a mapped type are read by AQL and projected through the attribute mapping onto
**name, responsible, environment and active state**, plus the target-specific fields.

### Servers and machines → `hosts`

Matched through the **same asset-identity chain milestone 4.4.2 uses**: external id (`external_id` +
`external_provider = 'JiraAssets'`) → MAC → FQDN → hostname → IP. The order is the point: an external
id is exact, a MAC survives a rename, an FQDN survives re-addressing, and an IP is weakest because DHCP
reassigns it — matching on IP first is how two unrelated machines get merged.

Reusing the chain is what keeps an Assets server that a scanner and Vision One already found from
becoming a third row for the same box.

Two new columns, `hosts.environment` and `hosts.owner`, hold what a CMDB knows that no scanner does.
The **active state needs no column**: `hosts.status` already holds a `Model.IntStatus`, so an active
object maps onto `Active` and an inactive one onto `Retired` — values the hosts screen already renders.
A parallel boolean would give one fact two homes that can disagree.

A field the mapping did not produce is **left alone, not cleared**. A CMDB that does not record MAC
addresses must not erase the ones a scanner found: "the register says nothing" is not the same
statement as "the register says empty".

### Applications → `entities`

An `application` entity, written through `IEntitiesService` so the definition's own validation applies.
The definition gains `environment` and `active` properties
([EntitiesConfiguration.yaml](../../src/API/EntitiesConfiguration.yaml), version 2.4); `name` and
`technology` and `responsible` were already there.

`responsible` is typed `Definition(person)`, so a name only lands if a `person` entity with that name
or email already exists. **An unmatched owner is reported, not invented** — creating a person row from a
CMDB string is how a directory fills up with near-duplicates of real people. The value is still
recorded on the import row, so nothing the register carried is lost.

### The audit row

Every object read produces a `jira_asset_objects` row, **including the ones that resolved to nothing**,
with the rule that matched (`external-id`, `mac`, `fqdn`, `host-name`, `ip`, `created`) or the reason it
failed. Without it, "why is that server not in NetRisk" has no answer except re-running the import and
watching.

Deleting a host does not delete its audit row (`ON DELETE SET NULL`): "this Assets object mapped to a
host that has since been removed" is exactly the row somebody needs when the machine reappears on the
next import.

### Retiring

`deactivate_missing` retires a previously imported object the AQL no longer returns. **Off by
default**, and only ever applied to objects this connection imported before — never to a host a scanner
found. A typo in an AQL filter returns nothing, and an import that decommissions production on a typo
is worse than one that leaves a stale row.

### Dry run

`Preview import` runs the **same code path** with the writes skipped: the AQL, the projection, the
matching and the decision about what would change are identical. It writes nothing at all — not the
host, not even the audit row — and returns the counts plus the first twenty rows as they would be
written. A preview that runs different code is a preview of nothing.

## The configurable mappings

Four mappings, all per connection, all editable:

| Mapping | Table | Where the vocabulary comes from |
|---|---|---|
| Severity → Jira priority | `issue_tracker_connections.priority_mapping_json` | `/rest/api/3/priority` |
| NetRisk field → Jira field | `jira_field_mappings` | `/rest/api/3/field` — **including custom fields** |
| Jira status → NetRisk action | `issue_status_mappings` | `/rest/api/3/project/{key}/statuses` |
| Assets attribute → NetRisk field | `jira_object_attribute_mappings` | `/objecttype/{id}/attributes` |

Three of those four existed before 4.6 and **could not be edited**: the status-mapping grid shipped as
`IsReadOnly` with no way to add a row, and the title/description templates and the priority mapping had
no editor at all — the server has had a wholesale `PUT` for the status mapping since 4.2.1 and nothing
called it.

The NetRisk target list is **served by the API** (`GET /Jira/mappable-fields`) rather than duplicated in
the client, so the picker cannot offer a target the mapping engine does not implement.

### Transforms

A small closed enum — `None`, `Trim`, `Upper`, `Lower`, `TruthyBoolean`, `FirstOfList`, `DateTime`,
`Integer` — for the same reason the templates are `{{Placeholder}}` substitution and not a template
language: the values are third-party text crossing between two systems, and an expression evaluator in
that position is a server-side injection surface bought for no benefit.

`TruthyBoolean` accepts what a CMDB actually holds: `true`, `yes`, `y`, `1`, `on`, `active`, `enabled`,
`in use`, `in service`, `in production`, `live`, `operational`, `ativo`, `sim`. A strict boolean parse
would read every `In Production` as inactive and retire the estate on first import.

`Integer` takes the first run of digits, so `3 - High` and `Tier 2` import as 3 and 2 — a criticality
written as text is the normal case, and refusing it would mean the field imports for nobody.

### Guards

A configuration that would be saved and then silently do nothing is **refused with a sentence**:

* two rows writing the same Jira field, or the same NetRisk target field;
* a mapping row with neither a source attribute nor a constant;
* an object mapping with no `Name` target — without one nothing can be matched or created;
* a target field that does not exist for the target kind (`MacAddress` on an application);
* one object type mapped twice;
* a NetRisk source the mapper does not understand, with the available list in the message.

A constant is a **fallback**, not an override: a present attribute wins and the constant fills the gap.
The other way round would make a constant a way to silently ignore the register.

## Links beyond findings

A Jira ticket can hang off a **finding, an incident or a risk**. `finding_issue_links` was widened
rather than duplicated — the poll loop, the webhook lookup, the loop protection and the conflict queue
all key off that one table, and a second table would have meant a second copy of each.

Three real foreign keys plus a `target_kind` discriminator, and **not** a polymorphic `(kind, id)` pair:
a polymorphic id cannot carry a foreign key, so deleting a risk would leave a link pointing at nothing
and the existing `ON DELETE CASCADE` would stop working. Exactly one of the three is set, enforced by
`FindingIssueLink.Validate()`, by a service guard, and by a `CHECK` constraint.

> **Deliberate limitation.** Inbound `IssueSyncAction`s (`MarkMitigated`, `ScheduleReverify`,
> `MarkFalsePositive`, `Reactivate`) apply to **findings only**. For an incident or a risk the external
> status is mirrored and displayed, and nothing is transitioned. Closing an incident is a human process
> with its own record-keeping, and mapping "Done" onto it has never been specified. The configuration
> screen says so where the mapping is edited, rather than offering an action that does nothing.

Findings keep their own endpoint (`FindingIssuesController`), which carries the auto-create policy, the
preview and the conflict queue; creating a finding's issue through the record route is refused.

## Permissions

`RecordIssues` decides its permission from the record kind **in the route**, checked inside the action
rather than as an attribute: an attribute would have to name the union of all three, and holding any one
of them would then be enough for all three.

| Route | Read | Write |
|---|---|---|
| `/RecordIssues/finding/{id}` | `vulnerabilities` | `vulnerabilities_update` |
| `/RecordIssues/incident/{id}` | `incident_management` | `incident_management` |
| `/RecordIssues/risk/{id}` | `riskmanagement` | `modify_risks` |

Everything on `/Jira` needs `configuration`, including the live metadata reads — they spend the
connection's credential against a third party. Two exceptions: the request mirror needs
`vulnerabilities` (the same permission that already gets somebody the connection list), and the
imported register needs `hosts`, because that is what it describes.

## Security

* Assets calls leave for **`api.atlassian.com`**, a different host from the operator-typed base URL.
  They go through `IOutboundHttpClient`, so `OutboundUrlPolicy` evaluates them like any other; the
  policy is a deny-list (cloud metadata always, private ranges optionally) rather than an allow-list,
  so the new host needs no configuration.
* **No new credential.** JSM and Assets reuse the connection's encrypted API token.
* The mirror holds third-party personal data (reporter and owner display names) and is removed with its
  connection (`ON DELETE CASCADE`).
* Assets attribute values are stored as text and rendered in a grid — never interpolated into SQL or
  into anything evaluated.
* AQL is the customer's query language over their own schema. It is serialised through a JSON writer
  and sent to Jira; the object type's name is quoted with its own quotes doubled, so a type called
  `Server "Legacy"` does not produce a syntax error nobody can attribute.

## Setup

1. On the Jira connection, set **Deployment** to Cloud and test the connection.
2. **Service Management:** enable it, load the service desks, pick one, load the queues, tick the ones
   to import and set each ceiling. Save, then *Sync now*.
3. **Assets:** enable it and save — the workspace id is discovered at that point. Load the schemas,
   pick one, load the object types.
4. Add an object mapping per type: pick the type, the target (Host for servers and machines,
   Application for applications), and optionally an AQL filter.
5. Map its attributes. **At minimum a `Name` target**; then responsible, environment, and the active
   state with the `TruthyBoolean` transform.
6. **Preview import** and read the sample. Only then *Import now*.

## Known limitations

* **Data Center is not supported.** Insight has a different root and object model; a Data Center
  connection is refused where it is configured.
* **Assets needs Jira Service Management Premium or Enterprise.** The connection test distinguishes
  "not entitled" (403/404 on the workspace endpoint) from "misconfigured", so a Standard-plan customer
  does not read a bug.
* The Assets object browse URL is not surfaced as a link — the object key is shown instead. The URL
  form was not verified against a live site.
* An unmatched `responsible` is reported and not created, so an application's owner stays unlinked until
  the matching `person` entity exists.
* Inbound actions are finding-only, as above.

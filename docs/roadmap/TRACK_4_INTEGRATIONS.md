# Track 4 — Integrations & Notification Channels: Detailed Specifications

> Status: **Planned** · Roadmap: [ROADMAP.md → Track 4](../../ROADMAP.md)
> Research basis: web survey of integration/notification/SSO best practices (June 2026) — sources at the end of each milestone.

This track connects NetRisk with external messaging platforms, issue trackers, and enterprise identity systems.

---

## Milestone 4.1: Unified Notification Channels

**Research highlights.** Per-platform native formatting matters (Slack Block Kit vs. Teams Adaptive Cards — Teams' Office 365 connectors are **deprecated**; use Workflows/Power Automate webhooks with Adaptive Cards). Rate-limit awareness (Slack ~1 msg/s/channel, Teams ~4 req/s), retry with exponential backoff, and **fallback channels** (if Slack fails after retries, fall back to email) are the reliability patterns. Webhook receivers must be able to verify authenticity.

### 4.1.1 Extensible `INotificationChannel` interface

**Contract**
- `Name`, `Task<DeliveryResult> SendAsync(NotificationMessage msg, ChannelConfig cfg, CancellationToken ct)`, `Task<TestResult> TestAsync(ChannelConfig cfg)`.
- `NotificationMessage` is channel-agnostic: event type, severity, title, body, structured fields, deep link back to the NetRisk record. Each provider renders it natively.

**Dispatcher**
- Queue-backed (Hangfire) in `BackgroundJobs`: per-channel retry with exponential backoff (3 attempts), failure logging, and an ordered **fallback chain** per subscription (e.g., Slack → email).
- Channel configs (URLs, tokens) stored **encrypted** via the settings infrastructure — never plaintext in the `settings` table.

### 4.1.2 Native providers: Email, Slack, Microsoft Teams, generic Webhook

| Provider | Spec |
|---|---|
| **Email** | Adapt the existing SMTP path onto the interface; HTML template + plaintext alternative. |
| **Slack** | Incoming-webhook URL config; Block Kit payloads (header, severity-colored section, field grid, "Open in NetRisk" button); respect HTTP 429 + `Retry-After`. |
| **Teams** | Workflows webhook URL with **Adaptive Card** JSON (FactSet for fields, OpenUrl action) — explicitly *not* the retired O365 connector format. |
| **Generic webhook** | POST JSON with a documented stable schema, custom-header support, and an **HMAC-SHA256 signature header** (`X-NetRisk-Signature`) over the body with a per-config secret so receivers can verify authenticity. |

- Every provider implements `TestAsync` → "send test message" button in the admin UI.

### 4.1.3 Event-triggered notification configuration

**Event catalog** (raised as domain events from `ServerServices`):
`risk.created`, `risk.severity_changed`, `vulnerability.imported`, `finding.status_changed`, `sla.approaching`, `sla.breached`, `incident.created`, `irp.task_assigned`, `riskacceptance.expiring`.

**Configuration**
- `notification_subscriptions` table: event type + filter (min severity, entity scope) + channel config + enabled flag.
- Admin UI: matrix of events × channels with per-row filters, and a **delivery log** view (status, attempts, last error) — delivery observability is a recurring demand.
- Digest/throttle option per subscription (e.g., batch `vulnerability.imported` into one summary message per import) to respect platform rate limits.

**Acceptance criteria**
- "New Critical risk → Slack" fires within seconds with a correctly rendered Block Kit card.
- An SLA breach reaches both Teams and the generic webhook with a verifiable HMAC signature.

**Sources (4.1):** [Courier — Slack/Teams notification implementation](https://www.courier.com/guides/how-to-build-slack-and-microsoft-teams-notifications/technical-implementation) · [Courier — Notification design best practices](https://www.courier.com/guides/how-to-build-slack-and-microsoft-teams-notifications/best-practices-and-optimization) · [Microsoft Learn — Teams incoming webhooks](https://learn.microsoft.com/en-us/microsoftteams/platform/webhooks-and-connectors/how-to/add-incoming-webhook)

---

## Milestone 4.2: Bi-directional Issue Sync

**Research highlights.** The demanded feature set: **policy-based ticket creation** (not ticket-per-finding noise), flexible field mapping, status mapping both ways, and close-the-loop automation — a developer closing the ticket transitions the finding and/or schedules re-verification.

### 4.2.1 Modular issue-tracker integration core

**Contract**
- `IIssueTrackerProvider`: `CreateIssueAsync`, `UpdateIssueAsync`, `GetIssueStatusAsync`, `TestConnectionAsync`, plus capability flags (supports webhooks? custom fields?).

**Data model**
- `finding_issue_links` table: finding ↔ external issue (provider, project key, issue key/id, URL, last-synced status, `last_sync_at`, sync-error state). One finding may link to multiple trackers.

**Configuration**
- Field-mapping config per connection: NetRisk severity → tracker priority; title/description templates (finding fields interpolated); project/issue-type selection; default labels.
- Connection management UI with `TestConnection` and a per-connection sync log.

### 4.2.2 Create/link developer tasks: Jira, GitHub Issues, GitLab Issues, Azure DevOps

- Four providers on the core: Jira Cloud REST v3 (API token), GitHub Issues (PAT or GitHub App), GitLab Issues (project token), Azure DevOps Work Items (PAT).
- From a finding (or a multi-selection) in the GUI: "Create issue…" → pick connection/project → preview rendered title/description → create + link; or "Link existing issue" by key/URL.
- Issue description includes severity, asset, CVE links, evidence excerpt, and the deep link back to NetRisk.
- **Policy mode** (noise reduction): optional rule such as "auto-create a Jira ticket for every new Critical in entity X."

### 4.2.3 Bi-directional synchronization

**Inbound**
- Webhook receivers per provider — validated (Jira/GitHub/GitLab signature or secret token; ADO basic-auth subscription) — plus a polling fallback job (15 min, configurable) for instances that can't reach NetRisk.
- Status mapping table per connection (e.g., Jira `Done` → NetRisk action `MarkMitigated` or `ScheduleReverify`); applying a transition records the audit event with source=`IssueSync` and the external actor.

**Outbound**
- NetRisk finding transitions (`Mitigated`, `FalsePositive`, reactivated-by-reimport) post a comment and/or transition on the linked issue per mapping.

**Safety**
- Loop protection: sync-origin marking so an inbound change doesn't echo back out.
- Conflict policy: last-writer-wins + a "sync conflicts" review queue.

**Acceptance criteria**
- Closing a linked Jira ticket flips the NetRisk finding to `Mitigated` (or creates a re-verify task, per config) within one webhook delivery or one polling cycle; the finding timeline shows it.

**Sources (4.2):** [Bugcrowd — Bi-directional Jira integration](https://www.bugcrowd.com/blog/vulnerability-management-made-easy-with-the-most-intuitive-and-efficient-bi-directional-jira-integration/) · [Invicti — 2-way Jira integration](https://www.invicti.com/blog/docs-and-faqs/fix-vulnerabilities-faster-bidirectional-invicti-jira-integration) · [Atlassian — Security vulnerabilities in Jira](https://support.atlassian.com/jira-software-cloud/docs/manage-security-vulnerabilities-in-jira/)

---

## Milestone 4.3: Hardened Enterprise Authentication

**Research highlights.** Enterprise buyers expect SAML *and* OIDC — SAML still dominates IdP installs, and .NET ships **no first-party SAML library** (plan on Sustainsys.Saml2 or a commercial component). **SSO without SCIM is considered a compliance gap**: automated deprovisioning is the killer requirement. WebAuthn/FIDO2 is the phishing-resistant standard for privileged accounts.

### 4.3.1 SAML 2.0 and OIDC authentication

**OIDC**
- ASP.NET Core first-party `AddOpenIdConnect` (authorization code + PKCE), discovery-document configuration.
- Validated against Entra ID, Okta, and Keycloak.

**SAML 2.0 (SP role)**
- Via Sustainsys.Saml2 (or an equivalent maintained library): SP-initiated login, IdP metadata import (URL or XML), signed assertions required, clock-skew tolerance, single logout where the IdP supports it.

**Common**
- Multiple IdP configurations storable; claim/attribute mapping (email, name, groups → NetRisk roles/entities) configurable per IdP; JIT user creation optional, **off by default**.
- **Desktop flow:** GUIClient opens the system browser; the API completes the IdP dance and returns a token via a localhost loopback redirect (the standard native-app pattern).
- **Break-glass:** local-admin login remains, so an IdP outage can't lock out administration.

### 4.3.2 Automated user provisioning via SCIM

- SCIM 2.0 server endpoints: `/scim/v2/Users`, `/scim/v2/Groups` — create, RFC 7644-correct PATCH semantics, filtering (`userName eq …`), pagination.
- **Deactivation is non-negotiable:** `active:false` must disable login *and revoke sessions immediately*.
- Auth: long-lived bearer token per provisioning connection (managed/revocable in admin UI), with full request audit logging.
- Group mapping: SCIM groups → NetRisk roles/entity assignments via the same mapping config as 4.3.1.
- Validate against Entra ID and Okta provisioning; document setup in `docs/`.

### 4.3.3 Hardware tokens (YubiKey / WebAuthn) for administrative accounts

- FIDO2/WebAuthn via a maintained .NET library (e.g., fido2-net-lib): registration ceremony (multiple named authenticators per user, created/last-used tracked), authentication ceremony on login, configurable attestation policy.
- Policy switch: "require hardware-backed second factor for users in admin roles" — enforced at login and at role elevation.
- Recovery: admin-issued one-time recovery codes; generation audited.
- **Desktop caveat to resolve in design:** WebAuthn is a browser API — enrollment/login ceremonies run through the system-browser flow established in 4.3.1.
- Integrates with the existing FaceID/biometric feature rather than replacing it (both register as second-factor methods).

**Sources (4.3):** [Security Boulevard — Enterprise SAML SSO in ASP.NET Core](https://securityboulevard.com/2026/05/enterprise-saml-sso-in-asp-net-core-the-complete-integration-guide-for-2026/) · [Deepak Gupta — SAML/OAuth/SCIM in enterprise identity](https://guptadeepak.com/sso-deep-dive-saml-oauth-and-scim-in-enterprise-identity-management/) · [SSOJet — SCIM vs SSO](https://ssojet.com/blog/scim-vs-sso-understanding-identity-provisioning-vs-authentication)

---

## Milestone 4.4: Trend Micro Vision One Integration

**Research highlights.** Trend Micro Vision One exposes Attack Surface Risk Management (ASRM) and Cyber Risk Exposure Management (CREM) through its public REST v3.0 APIs. These APIs enable programmatic access to device metadata, vulnerability profiles (with real-time exploitability indicators), risk scores, and security postures. Authentication is key-based, requiring a Bearer token generated via the Vision One console under Administration > API Keys.

### 4.4.1 Authentication & Connection Management

**Data model & Configuration**
- `trendmicro_connections` table: connection settings including API Base URL (region-dependent, e.g., `https://api.xdr.trendmicro.com`), encrypted API Key (Bearer Token), synchronization schedule, and active status flag.
- Admin UI: Connection screen with "Test Connection" functionality executing a lightweight ping/GET against `/v3.0/asrm/attackSurfaceDevices` (with a `limit=1` parameter) to verify token validity.

### 4.4.2 Computer Inventory Synchronization

- **Integration logic:** Daily background job (via Hangfire in `BackgroundJobs`) query `GET /v3.0/asrm/attackSurfaceDevices`.
- **Schema Mapping:**
  - Map endpoints to NetRisk `Host` or `Asset` models.
  - Fields mapped:
    - Hostname / FQDN -> `Host.Name`
    - IP/MAC Addresses -> `Host.IpAddress`, `Host.MacAddress`
    - Operating System -> `Host.OsPlatform`, `Host.OsVersion`
    - Vision One Asset ID -> `Host.ExternalId` (where provider is 'TrendMicroVisionOne')
    - Criticality / Business Impact -> `Host.Criticality` (syncing TM asset classification)
- **Deduplication:** Leverage the Track 3.3 deduplication engine using MAC/IP or Hostname matching to merge TM assets with existing inventory instead of creating duplicates.

### 4.4.3 Vulnerability Ingestion & Mapping

- **Integration logic:** Fetch device-specific vulnerabilities via `GET /v3.0/asrm/vulnerableDevices`.
- **Ingestion pipeline:**
  - For each vulnerable device, extract the listing of active CVEs.
  - Query CVE details (CVSS, EPSS, exploitability) from the TM response.
  - Map CVE findings onto NetRisk `Vulnerability` and `Finding` entities.
  - Populate virtual patching status where applicable: if the vulnerability is protected by a TM Virtual Patch (e.g., Cloud One Workload Security, Apex One), flag the NetRisk finding as `Mitigated` (or with a custom tag `VirtualPatched`), documenting the virtual patch ID/rule in the finding audit log.

### 4.4.4 Risk & Posture Assessment Synchronization

- **Cyber Risk Scoring:** Query `GET /v3.0/asrm/highRiskDevices` to fetch granular risk scores (0–100 range) and factors (e.g., detection history, security configuration posture, identity risk).
- **Security Posture integration:**
  - Store the TM Cyber Risk Score directly on the NetRisk Host model (`Host.RiskScore`).
  - Periodically aggregate device risk scores to calculate the overall Cyber Risk Index for the entire Business Entity.
  - Support bi-directional status updates using `POST /v3.0/asrm/attackSurfaceDevices/update` to update asset criticality or assign exemptions inside Trend Micro Vision One when a finding is accepted as `RiskAccepted` in NetRisk.

**Sources (4.4):** [Trend Micro Developer Portal — v3.0 Public APIs](https://v1-api-docs.trendmicro.com) · [Trend Micro Online Help — Attack Surface Risk Management](https://docs.trendmicro.com/en-us/enterprise/trend-micro-vision-one-help/attack-surface-risk-management.aspx)

---

## Milestone 4.5: SecurityScorecard Integration

**Research highlights.** SecurityScorecard exposes public REST APIs (v1.0) under `https://api.securityscorecard.io`. It provides endpoints to retrieve general corporate ratings and grades, granular scores across 10 risk factors, active security findings/issues (grouped by factors), and potential/confirmed CVE vulnerabilities (Patching Cadence). Authentication is token-based, using the header `Authorization: Token <API_KEY>`.

### 4.5.1 Authentication & Configuration Management

**Data model & Configuration**
- `securityscorecard_connections` table: connection settings including Target Domain (e.g., `yourcompany.com`), encrypted API Token, synchronization schedule, and active status flag.
- Admin UI: Connection screen with "Test Connection" executing a lightweight ping/GET against `GET /companies/{domain}` to verify token and domain validity.

### 4.5.2 Posture & Factor Score Synchronization

- **Integration logic:** Daily background job (via Hangfire in `BackgroundJobs`) query `GET /companies/{domain}` to retrieve:
  - Overall Score (0-100) -> mapped to `Entity.CyberRiskIndex`.
  - Grade (A-F) -> mapped as custom posture KPI.
- Query `GET /companies/{domain}/factors` to fetch the 10 core factors (Network Security, Patching Cadence, DNS Health, Endpoint Security, etc.). Store these factor scores in a new `security_scorecard_factors` table linked to the Business Entity for historical trend charting.

### 4.5.3 Vulnerability & Finding Ingestion

- **Vulnerabilities Ingestion:** Query `GET /companies/{domain}/issues/potentially_vulnerable` to fetch the list of CVEs detected on the domain's assets.
  - Map CVE findings to NetRisk `Vulnerability` and `Finding` entities, linking them to a virtual "Domain Asset" represented in the Host table.
- **Security Issues Ingestion:** Query `GET /companies/{domain}/issues` to get active findings (e.g., missing SPF, SSL certificate expiration, open ports).
  - Map issues to NetRisk findings under a custom category `'SecurityScorecard_Issue'`, including fields like `first_seen`, `last_seen`, and impacted IP addresses/URLs.

**Sources (4.5):** [SecurityScorecard Developer Portal — Public APIs](https://api.securityscorecard.io) · [SecurityScorecard Online Help — Risk Factors and Issue Types](https://support.securityscorecard.com)

---

## Milestone 4.6: Jira Service Management & Assets

**Status: Implemented** in `db_version` 83 / upgrade phase 14; see
[docs/features/jira-service-management.md](../features/jira-service-management.md) for what shipped and
the ROADMAP item for the three places it departs from this specification. Milestone 4.2 already ships
Jira *Software* — create, update, transition and
bi-directionally sync an issue against a vulnerability finding
([docs/features/issue-tracker-sync.md](../features/issue-tracker-sync.md)). This milestone adds the
three things 4.2 does not have: the **Service Management** read surface (customer requests, their SLA
cycles, and service-desk queues), **Assets** import for the CMDB registers that describe
applications, servers and machines, and a **configuration screen that is actually configurable** —
today the status-mapping grid is read-only and the title/description templates and priority mapping
have no editor at all (`IntegrationsView.axaml`, `IsReadOnly="True"` at the status-mapping `DataGrid`).

**Verified API surface.** Every path below was read from Atlassian's current reference, not from
memory; the ones that turned out not to exist where expected are called out.

| Need | Endpoint | Notes |
|---|---|---|
| Service desks | `GET /rest/servicedeskapi/servicedesk` | No `X-ExperimentalApi` needed |
| Request types | `GET /rest/servicedeskapi/servicedesk/{sdId}/requesttype` | |
| Queues | `GET /rest/servicedeskapi/servicedesk/{sdId}/queue` | |
| Issues in a queue | `GET /rest/servicedeskapi/servicedesk/{sdId}/queue/{qId}/issue` | |
| Customer requests | `GET /rest/servicedeskapi/request` | `expand=serviceDesk,requestType,participant,status,sla,attachment,action,comment` |
| One request | `GET /rest/servicedeskapi/request/{issueIdOrKey}` | |
| SLA | `GET /rest/servicedeskapi/request/{issueIdOrKey}/sla` | Zero-or-more `completedCycles`, zero-or-one `ongoingCycle` |
| Assets workspace id | `GET /rest/servicedeskapi/assets/workspace` | Paginated; the id is **not** the Jira cloud id |
| Assets root | `https://api.atlassian.com/jsm/assets/workspace/{workspaceId}/v1/…` | A **different host** from the site URL |
| Object schemas | `GET /objectschema/list` | |
| Object types of a schema | `GET /objectschema/{id}/objecttypes/flat` | In the *objectschema* group, not *objecttype* |
| Object-type attributes | `GET /objecttype/{id}/attributes` | Drives the attribute picker |
| Object search | `POST /object/aql` (+ `POST /object/aql/totalcount`) | AQL body `{"qlQuery":"objectType = Server"}` |
| One object | `GET /object/{id}` | `id`, `label`, `objectKey` (`ITSM-88`), `objectType`, `attributes[]`, `created`/`updated` |

### 4.6.0 Design decisions

**JSM extends the existing Jira connection; it is not a fifth provider.** A JSM service desk *is* a
Jira project on the same site, reached with the same credential. A `JiraServiceManagement = 5`
provider kind would make an operator type the same API token twice, and would split the links for one
ticket across two connections — so the sync engine, the conflict queue and the poll loop would each
have two tables to reconcile. Instead `IssueTrackerProviderKind.Jira` gains capability flags
(`SupportsServiceDesk`, `SupportsAssets`) and a **1:1 extension table**, `jira_connection_settings`,
keyed on `connection_id`. An extension table rather than more columns on
`issue_tracker_connections`, because GitHub, GitLab and Azure DevOps have no service desk and no
CMDB, and fifteen always-null columns on a shared table is how a generic table stops being generic.

**A new `deployment` discriminator, because the Assets API is not the same product on Data Center.**
Cloud Assets lives at `api.atlassian.com/jsm/assets/workspace/{id}/v1`; Data Center's equivalent is
Insight at `/rest/insight/1.0/` on the site itself. `JiraDeployment { Cloud = 1, DataCenter = 2 }`
selects the client. Only Cloud is implemented in this milestone; Data Center is refused at save with
a message that says so, rather than silently producing 404s.

**Requests and SLA are mirrored; queues are not.** A queue is a saved JQL filter whose membership
changes on every triage action — a mirror of it is wrong the moment it is written, so the queue
browser reads live. Requests and their SLA cycles *are* mirrored (`jira_service_requests`,
`jira_request_slas`), because those are the rows that have to survive a NetRisk restart, be joined
against findings for reporting, and be swept by a job for breach notification.

**SLA goes into columns, not a JSON blob.** `breached`, `remaining_ms` and `goal_duration_ms` are the
fields every question is asked about ("what is breaching this week"), and a blob cannot answer that
without a full table scan and a parse.

### 4.6.1 Schema

`db_version` **83**, upgrade **phase 14** (`startVersion: 82`, `targetVersion: 83`), additive and
non-destructive throughout — so the phase carries no `--yes` gate. Nine new tables, one generalised
table, two new `hosts` columns. Named per the Track 6 convention (snake_case, `fk_`/`idx_`/`uq_`
prefixes, int-backed enums with explicit `HasConversion`, `tinyint(1)` booleans, UTC `datetime`,
`varchar(n)`/`text` — never `char(n)` for a string, per the `ElementMappingConvention` trap in
[CLAUDE.md](../../CLAUDE.md)).

```
jira_connection_settings          1:1 with issue_tracker_connections
  connection_id PK/FK, deployment, service_desk_id, service_desk_name,
  jsm_enabled, request_type_filter, import_slas, sla_breach_notifications,
  assets_enabled, assets_workspace_id, assets_schema_id, assets_schema_name,
  default_link_target_kind, last_jsm_sync_at, last_assets_sync_at

jira_queue_imports                which queues feed the mirror
  id, connection_id FK, queue_id, queue_name, service_desk_id, enabled,
  link_target_kind, max_requests, created_at

jira_service_requests             the mirror
  id, connection_id FK, issue_key (uq with connection), issue_id,
  service_desk_id, request_type_id, request_type_name, summary,
  status_name, status_category, reporter_account_id, reporter_display_name,
  organization_name, priority_name, assignee_display_name,
  created_at_remote, updated_at_remote, is_closed, request_url,
  first_seen_at, last_synced_at, sync_error

jira_request_slas                 one row per metric per request
  id, request_id FK CASCADE, metric_id, metric_name, is_ongoing, breached,
  paused, goal_duration_ms, elapsed_ms, remaining_ms,
  cycle_start_at, cycle_stop_at, captured_at
  uq_jira_request_slas_request_metric_cycle (request_id, metric_id, cycle_start_at)

jira_field_mappings               outbound custom-field mapping ("object mapping" for tickets)
  id, connection_id FK, direction, netrisk_field, jira_field_id,
  jira_field_name, jira_field_type, transform, constant_value, enabled

jira_object_mappings              Assets object type -> NetRisk record
  id, connection_id FK, object_type_id, object_type_name, target_kind,
  aql_filter, match_strategy, enabled, create_missing, update_existing,
  deactivate_missing, last_imported_at, created_at, created_by_id

jira_object_attribute_mappings    attribute -> field, the per-type detail
  id, mapping_id FK CASCADE, source_attribute_id, source_attribute_name,
  target_field, transform, is_identity, constant_value, sort_order

jira_asset_objects                the imported register, and the audit trail of what it produced
  id, connection_id FK, object_id (uq with connection), object_key,
  object_type_id, object_type_name, label,
  mapped_name, mapped_owner, mapped_environment, mapped_active,
  attributes_json, target_kind, target_host_id FK SET NULL,
  target_entity_id FK SET NULL, match_reason,
  created_at_remote, updated_at_remote, first_seen_at, last_synced_at, import_error

hosts.environment    varchar(64) NULL
hosts.owner          varchar(255) NULL
```

`hosts` needs no `active` column: `hosts.status` already holds a `Model.IntStatus`, so an Assets
object's active state maps onto `IntStatus.Active` (42) and its opposite onto `IntStatus.Retired`
(27) — both values that already exist and that the hosts screen already renders. Adding a parallel
boolean would give the same fact two homes that can disagree.

**`finding_issue_links` is generalised rather than duplicated.** The user-visible requirement is that
a Jira ticket can hang off a finding, an **incident** or a **risk**. A second link table would mean a
second sync engine, a second conflict queue and a second loop-protection rule, so the existing table
gains:

```
finding_issue_links
  + target_kind        int NOT NULL DEFAULT 1   -- Finding = 1, Incident = 2, Risk = 3
  + incident_id        int NULL  FK incidents(id)  ON DELETE CASCADE
  + risk_id            int NULL  FK risks(id)      ON DELETE CASCADE
  ~ vulnerability_id   becomes NULL-able
  + CHECK (exactly one of vulnerability_id / incident_id / risk_id is non-null)
```

Three real FK columns, not a polymorphic `(kind, id)` pair: a polymorphic id cannot carry a foreign
key, so `ON DELETE CASCADE` would stop working and deleting a risk would leave a link pointing at
nothing. The `CHECK` constraint is available on MariaDB 10.2+ and is backed by a code guard, because
a constraint the application can trip is a bug report and not a defence. The `DEFAULT 1` is what makes
the migration additive — every existing row is a finding link and stays one.

> **Deliberate limitation.** Inbound `IssueSyncAction`s (`MarkMitigated`, `ScheduleReverify`,
> `MarkFalsePositive`, `Reactivate`) apply to **Finding** targets only. For an incident or a risk the
> external status is mirrored and displayed, and nothing is transitioned automatically. Closing an
> incident is a human process with its own record-keeping, and wiring "Done" onto it without a
> specification would be exactly the kind of control this repository has three times documented as
> working and shipped broken (see the Security Conventions note in [CLAUDE.md](../../CLAUDE.md)). The
> config screen says so where the mapping is edited, rather than offering an action that does nothing.

### 4.6.2 Service Management read

`JiraServiceManagementClient` in `ServerServices/Integrations/IssueTrackers/Jsm/`, on
`IOutboundHttpClient` and the same basic-auth credential as `JiraIssueTrackerProvider`:

* `GetServiceDesksAsync`, `GetRequestTypesAsync`, `GetQueuesAsync`, `GetQueueIssuesAsync` — live
  reads, paginated (`start`/`limit`, `isLastPage`), used by the config screen's pickers and the queue
  browser.
* `GetRequestAsync(key, expand: requestType,status,sla,serviceDesk)` and `GetSlaAsync(key)` — the
  mirror's inputs. SLA is fetched separately rather than only through `expand=sla` so a request whose
  expand is truncated still gets its cycles.
* `JsmSyncService.SyncConnectionAsync` — for each enabled `jira_queue_imports` row, page the queue's
  issues up to `max_requests`, upsert `jira_service_requests` on `(connection_id, issue_key)`, then
  upsert each metric's cycles. Runs on the existing per-connection `poll_interval_minutes` schedule
  in `IssueSyncPollingJob`, extended rather than a second job, and records an
  `integration_sync_logs` row under a new `IntegrationKind.JiraServiceManagement = 5`.
* SLA breach raises the existing Track 4.1 notification path — a new `jsm.sla_breached` event in the
  catalog, de-duplicated per `(request, metric, cycle_start)` so one breach notifies once.

A request already linked to a NetRisk record is mirrored regardless of queue configuration; queues
are how you import requests that are *not* yet linked.

### 4.6.3 Assets import

`JiraAssetsClient` (Cloud): discover the workspace id once via
`GET /rest/servicedeskapi/assets/workspace` on the site, cache it in
`jira_connection_settings.assets_workspace_id`, then talk to
`https://api.atlassian.com/jsm/assets/workspace/{id}/v1`. Schemas, flat object types and object-type
attributes drive the pickers; `POST /object/aql` with
`objectType = "<name>"` (AND the mapping's `aql_filter`, when set) reads the register, paged by
`startAt`/`maxResults` against `POST /object/aql/totalcount`.

`AssetsImportService.ImportAsync(connectionId, dryRun)`:

1. For each enabled `jira_object_mappings` row, page the objects of that type.
2. Project each object through `jira_object_attribute_mappings` into a `MappedAssetObject`
   (`Name`, `Owner`, `Environment`, `Active`, plus the target-specific fields).
3. Resolve the target:
   * `target_kind = Host` (servers, machines) — matched by the **asset-identity chain already used by
     4.4.2**: `external_id` + `external_provider = 'JiraAssets'` → MAC → FQDN → hostname → IP. Writes
     `host_name`, `fqdn`, `ip`, `mac_address`, `os`, `os_version`, `criticality`, the new
     `environment` and `owner`, and `status` from the active state. Reusing the chain is what keeps a
     server that a scanner and Vision One already know about from becoming a third row.
   * `target_kind = ApplicationEntity` — an `entities` row with `definition_name = 'application'`,
     its properties written through `IEntitiesService` so the definition's validation applies.
     `name` → `name`, the owner → `responsible`, and **two new properties on the `application`
     definition**, `environment` and `active`, added to
     [src/API/EntitiesConfiguration.yaml](../../src/API/EntitiesConfiguration.yaml) with its version
     moved 2.3 → 2.4. `responsible` is typed `Definition(person)`, so a matched `person` entity is
     referenced and an unmatched owner is kept as text in `jira_asset_objects.mapped_owner` and
     reported in the import summary — inventing a person row from a CMDB string is how a directory
     gets polluted.
4. `deactivate_missing` (off by default) retires a previously imported object that the AQL no longer
   returns. Off by default because an AQL typo would otherwise retire the estate.
5. Every object gets a `jira_asset_objects` row whether or not it resolved, with `match_reason` and
   `import_error` — a register import you cannot audit is a register import you cannot trust.

`dryRun` writes nothing and returns the counts plus the first 20 mapped rows. That is the preview the
config screen shows before the first real import.

### 4.6.4 Configurable mapping — what "object mapping" means here

Three distinct mappings, all per connection, all editable:

| Mapping | Table | Direction | Editor |
|---|---|---|---|
| Severity → Jira priority | `priority_mapping_json` (exists, no editor) | out | Grid, one row per NetRisk severity, priority values loaded from `/rest/api/3/priority` |
| NetRisk field → Jira field | `jira_field_mappings` (new) | out | Grid; Jira fields (including `customfield_10012`) loaded from `/rest/api/3/field`, so custom fields are selectable rather than typed |
| Jira status → NetRisk action | `issue_status_mappings` (exists, read-only grid) | both | Editable grid; statuses loaded from `/rest/api/3/project/{key}/statuses` |
| Assets attribute → NetRisk field | `jira_object_attribute_mappings` (new) | in | Grid per object type; attributes loaded from `/objecttype/{id}/attributes`, targets from a fixed list per `target_kind` |

Transforms stay a small closed enum — `None`, `Trim`, `Upper`, `Lower`, `TruthyBoolean`,
`FirstOfList`, `DateTime`, `IntegerScale` — for the same reason 4.2's templates are `{{Placeholder}}`
substitution and not a template language: the values are attacker-influenced text crossing into
someone else's system, and an expression evaluator in that position is a server-side injection
surface for no benefit.

### 4.6.5 Configuration screen

The Integrations admin screen's **Issue Trackers** tab becomes a master list plus five sub-tabs,
shown only when the selected connection's provider is Jira:

1. **Connection** — the current fields, plus deployment (Cloud/Data Center) and *Test connection*.
   The test is extended to probe, in order: `/rest/api/3/myself`, the project, and — when enabled —
   `/rest/servicedeskapi/servicedesk/{id}` and the Assets workspace. Each probe reports separately,
   so "credentials fine, Assets not entitled" reads as that instead of as a flat failure.
2. **Field mapping** — priority grid, the Jira-field grid, title/description template editors, and a
   **live preview** rendered from a picked finding through the existing
   `IIssueTrackerService.PreviewAsync`. The preview is what makes a template editable with
   confidence, and it already exists server-side with no UI on it.
3. **Status mapping** — the existing grid, made editable, with add/remove and *Load statuses from
   Jira*. Saved wholesale through the existing `PUT /IssueTrackers/{id}/status-mappings`.
4. **Service Management** — enable, service-desk picker, request-type filter, the queue list with a
   per-queue *import* checkbox and link-target column, SLA import and breach-notification toggles,
   and a read-only mirror browser (request, type, status, SLA remaining, breached).
5. **Assets** — enable, workspace id (discovered, read-only), schema picker, the object-type mapping
   grid (type → target kind, AQL filter, match strategy, create/update/deactivate flags), the
   attribute mapping grid for the selected type, *Preview import* and *Import now*.

New view models split out of `IntegrationsViewModel` — at 1,482 lines it is already the largest view
model in the client, and five more tabs inside it would be unreviewable: `JiraFieldMappingViewModel`,
`JiraServiceManagementViewModel`, `JiraAssetsViewModel`, each owning its own load/save.

### 4.6.6 API endpoints

All under `IssueTrackersController` (or a sibling `JiraController` sharing
`IntegrationsControllerBase`), every action annotated — `API.Tests/Security/ControllerAuthorizationInventoryTest`
fails the build on an unannotated one.

```
GET  /IssueTrackers/{id}/jira/fields                              configuration
GET  /IssueTrackers/{id}/jira/priorities                          configuration
GET  /IssueTrackers/{id}/jira/statuses                            configuration
GET  /IssueTrackers/{id}/jira/field-mappings                      configuration
PUT  /IssueTrackers/{id}/jira/field-mappings                      configuration
GET  /IssueTrackers/{id}/jsm/settings                             configuration
PUT  /IssueTrackers/{id}/jsm/settings                             configuration
GET  /IssueTrackers/{id}/jsm/servicedesks                         configuration
GET  /IssueTrackers/{id}/jsm/servicedesks/{sdId}/requesttypes     configuration
GET  /IssueTrackers/{id}/jsm/servicedesks/{sdId}/queues           configuration
GET  /IssueTrackers/{id}/jsm/queues/{qId}/requests                vulnerabilities
GET  /IssueTrackers/{id}/jsm/requests                             vulnerabilities
GET  /IssueTrackers/{id}/jsm/requests/{key}                       vulnerabilities
POST /IssueTrackers/{id}/jsm/sync                                 configuration
GET  /IssueTrackers/{id}/assets/schemas                           configuration
GET  /IssueTrackers/{id}/assets/schemas/{sid}/objecttypes         configuration
GET  /IssueTrackers/{id}/assets/objecttypes/{otid}/attributes     configuration
GET  /IssueTrackers/{id}/assets/mappings                          configuration
PUT  /IssueTrackers/{id}/assets/mappings                          configuration
POST /IssueTrackers/{id}/assets/preview                           configuration
POST /IssueTrackers/{id}/assets/import                            configuration
GET  /IssueTrackers/{id}/assets/objects                           hosts
GET  /RecordIssues/{targetKind}/{targetId}                        per module
POST /RecordIssues/{targetKind}/{targetId}/create                 per module _update
POST /RecordIssues/{targetKind}/{targetId}/link                   per module _update
```

`per module` is `vulnerabilities`, `incidents` or `risks` according to `targetKind`, resolved in the
action rather than by attribute — one route with a checked discriminator, because three near-identical
controllers is how the permission on one of them ends up missing.

Outbound creation from an incident or a risk needs the template engine to stop being
finding-shaped: `IssueTemplate.ValuesFor(Vulnerability, …)` becomes an `IIssueTemplateSource` with one
implementation per target kind, each publishing its own placeholder set (a risk has no CVE, an
incident has no CVSS). The config screen's placeholder help is generated from those sets, so it cannot
drift from what actually renders.

### 4.6.7 Security

* Assets calls leave for **`api.atlassian.com`**, a different host from the operator-typed base URL.
  They go through `IOutboundHttpClient`, so `OutboundUrlPolicy` still evaluates them; the policy is a
  deny-list (metadata endpoints always, private ranges optionally) rather than an allow-list, so no
  configuration change is needed for the new host — confirmed in
  [src/ServerServices/Http/OutboundUrlPolicy.cs](../../src/ServerServices/Http/OutboundUrlPolicy.cs).
* No new credential. JSM and Assets reuse the connection's encrypted API token, so nothing new is
  stored and the write-only credential handling in `IssueTrackerService` is unchanged.
* `jira_service_requests` and `jira_asset_objects` carry third-party personal data (reporter and owner
  display names). Both get an `entity_id`-derived scope through the connection, and the mirror is
  purged with the connection on delete (`ON DELETE CASCADE`).
* Assets attribute values land in `attributes_json` as text and are rendered in a grid, never
  interpolated into SQL or into a template that is evaluated.
* The deep link for an Assets object (`/jira/servicedesk/assets/object/{id}`) is **unverified** — see
  the risk list.

### 4.6.8 Tests

Per [src/AI_TESTING_INSTRUCTIONS.md](../../src/AI_TESTING_INSTRUCTIONS.md), these land with the code,
not after it.

| Project | Coverage |
|---|---|
| `ServerServices.Tests` | Attribute-mapping projection per transform; the identity/match chain including the "MAC matches, hostname moved" case; dry-run writes nothing; `deactivate_missing` off means nothing is retired; SLA cycle upsert is idempotent across two syncs; the target-kind guard refuses two non-null FKs; JSM and Assets clients against recorded fixture payloads |
| `API.Tests` | Every new action's happy path and each guard branch; `ControllerAuthorizationInventoryTest` passes with no new `[AllowAnonymous]`; `RecordIssues` refuses a target kind the caller lacks permission for |
| `ClientServices.Tests` | Each new REST method through `MockSetup.GetRestClient()` |
| `GUIClient.Tests` | Mapping-row validation (duplicate target field, missing identity attribute) — as pure classes under `Validation/`, since that project cannot reference Avalonia |
| `ConsoleClient.Tests` | `SchemaUpgradeIdempotenceTest` and `SchemaUpgradeTableReferencesTest` accept `83.sql` unchanged |
| `DAL.IntegrationTests` | `Phase14JiraSchemaTests` mirroring `Phase10IntegrationsSchemaTests`; `SchemaUpgradeRetryTests` extended to 83 with the Structure script applied twice |

**Acceptance criteria.** (1) A JSM queue configured for import populates the mirror with each
request's status and SLA, and a breaching SLA raises `jsm.sla_breached` exactly once per cycle.
(2) An Assets object type mapped to `Host` imports servers with name, responsible, environment and
active state, and re-importing updates the same rows rather than creating duplicates. (3) An object
type mapped to `ApplicationEntity` produces `application` entities whose `responsible` points at a
matched `person`, with unmatched owners reported and not invented. (4) A Jira ticket can be created
from, and linked to, a finding, an incident and a risk. (5) Every mapping above is editable in the
GUI, and the description template's rendered preview updates before anything is saved.

### 4.6.9 Risks and open questions

1. **Does the Assets Cloud API accept basic auth with email + API token?** Atlassian documents OAuth
   2.0 scopes (`read:cmdb-object:jira`, `read:cmdb-schema:jira`) for the Assets REST API, while
   basic-auth-with-API-token is documented for Jira Cloud REST APIs generally. Community usage
   suggests basic auth works against `api.atlassian.com/jsm/assets/…`, but this was **not confirmed
   from Atlassian's own reference**. This is the single largest risk in the milestone: if basic auth is
   refused, Assets needs a 3LO OAuth flow, which is a milestone of its own. **Mitigation:** implement
   the workspace-id + `objectschema/list` probe *first*, behind the connection test, against a real
   site. Do not build the mapping UI until that probe returns 200.
2. **The Assets object browse URL** is written from memory. Resolve it from the object payload's own
   links, or verify against a live site, before it ships in a grid as a clickable link.
3. **Assets is not on every JSM plan.** Assets requires Premium or Enterprise. The connection test
   must distinguish "not entitled" (403/404 on the workspace endpoint) from "misconfigured", or every
   Standard-plan customer reads a bug.
4. **Jira Data Center** uses `/rest/insight/1.0/` and a different object model. Out of scope here and
   refused at save rather than half-supported.
5. **`request_type_filter` granularity.** Filtering the mirror by request type is specified as a
   comma-separated id list. If customers need per-request-type link targets instead, that becomes a
   row per type — deferred until someone asks, since the table can grow without a schema change to
   the settings row.

**Sources (4.6):** [Assets REST API — objectschema](https://developer.atlassian.com/cloud/assets/rest/api-group-objectschema/) · [Assets REST API — object (AQL)](https://developer.atlassian.com/cloud/assets/rest/api-group-object/) · [Assets REST API — objecttype](https://developer.atlassian.com/cloud/assets/rest/api-group-objecttype/) · [Assets REST API guide — workflow](https://developer.atlassian.com/cloud/assets/assets-rest-api-guide/workflow/) · [JSM Cloud REST API — request](https://developer.atlassian.com/cloud/jira/service-desk/rest/api-group-request/) · [JSM Cloud REST API — servicedesk & queues](https://developer.atlassian.com/cloud/jira/service-desk/rest/api-group-servicedesk/) · [JSM Cloud REST API — assets workspace](https://developer.atlassian.com/cloud/jira/service-desk/rest/api-group-assets/) · [Basic auth for REST APIs](https://developer.atlassian.com/cloud/jira/software/basic-auth-for-rest-apis/)

---

## Dependencies & sequencing

- **4.1** is a prerequisite for the channel-based notifications in Track 3.4.3 (SLA breaches) and Track 2.4.2 (IRP task assignment).
- **4.3.1** group/entity mapping composes with Track 2.3's `user_entity_roles` — co-design the mapping model.
- **4.2** consumes the finding lifecycle from Track 3.2 (status mapping targets `Mitigated`, etc.).
- **4.4** requires the deduplication engine from Track 3.3 to properly reconcile computer inventory, and maps findings to the lifecycle state-machine from Track 3.2.
- **4.5** maps its external vulnerabilities and domain findings to the finding lifecycle from Track 3.2.
- **4.6** builds on 4.2's connection, provider and link model rather than beside it, reuses 4.1's channel dispatcher for `jsm.sla_breached`, and reuses 4.4.2's host-identity chain so Assets servers reconcile with the inventory Vision One and the scanners already populate. Its Assets import writes `entities` rows through Track 2.3's entity service, so the `application` definition gains two properties there.

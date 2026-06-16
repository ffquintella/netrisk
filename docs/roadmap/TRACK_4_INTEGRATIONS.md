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

## Dependencies & sequencing

- **4.1** is a prerequisite for the channel-based notifications in Track 3.4.3 (SLA breaches) and Track 2.4.2 (IRP task assignment).
- **4.3.1** group/entity mapping composes with Track 2.3's `user_entity_roles` — co-design the mapping model.
- **4.2** consumes the finding lifecycle from Track 3.2 (status mapping targets `Mitigated`, etc.).
- **4.4** requires the deduplication engine from Track 3.3 to properly reconcile computer inventory, and maps findings to the lifecycle state-machine from Track 3.2.
- **4.5** maps its external vulnerabilities and domain findings to the finding lifecycle from Track 3.2.

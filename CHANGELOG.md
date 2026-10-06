---
ptf: 1
project: netrisk
---

# Changelog

All notable changes to NetRisk are documented here. Format: [Keep a Changelog](http://keepachangelog.com/)
1.1.0 with the Postponed/Abandoned extensions described in
[TRACKING-FORMAT.md](https://github.com/felipe/project-tracker/blob/main/TRACKING-FORMAT.md). Versions
follow [SemVer](http://semver.org/).

## [Unreleased]

### Added
- Add the ICR methodology under docs/methodology/: a configurable-weight, review-aware 0–100 consolidated cyber-risk index over the register, findings, incidents, assessments, CMDB, entity map, Vision One, SecurityScorecard and Tenable (S39)
- Add the specification of the Cyber Risk Overview dashboard, its drill-down and the ICR configuration screens (S40)
- Add Track 10 — Consolidated Cyber Risk Index & Overview Dashboard to the roadmap as a planned track (M52, M53, M54, M55, M56, M57, M58, M59)
- Add the Stage 9.1 linkage-chain specification: strategic objective and IT service entity types, per-level risk chain links that coexist with the legacy entity link, and a critical-process coverage metric (T143, S41)
- Add the strategic objective entity type and let business processes point at the objectives they serve (T144, S41)
- Add the IT service entity type, with a mandatory technical owner and the processes, applications and data it serves, and a declared 1–5 criticality on business processes (T145, S41)
- Link a risk to any level of the objective → process → IT service → data → asset chain through `/RiskChain`, with direct and inferred node queries and host links that require the hosts permission (T146, S41)
- Add `GET /RiskChain/Coverage/CriticalProcesses`, the share of active processes with criticality 4 or 5 covered by an open risk, directly or by inference (T147, S41)
- Mirror the legacy risk entity link into the chain as Legacy links, copied on upgrade and kept in step by `PUT/DELETE /Risks/{id}/Entity`, and test traversal with a missing middle link (T148, S41)
- Show a risk's linkage chain under its Entity row, five levels with "not informed" for the empty ones, and edit it in a dialog with a host search that requires the hosts permission (T146, S41)
- Add report 7, Critical process coverage, with the covered share, the processes without a declared criticality, and the direct and inferred risks of each process (T147, S41)

### Changed
- Count a risk linked to an entity through the risk chain in the top-entities and entities-risks statistics, once even when the legacy entity link also points there (T146, S41)

### Fixed
- Stop counting an entity's own risks twice in the Entities Risks report (S41)
- Translate the `Application` property label and the `team` entity type in the entity form in English and Portuguese (T145, S41)

## [2.24.0] - 2026-10-02

### Added
- Allow filtering hosts by criticality, environment, owner, source, risk score and last verification date on `GET /Hosts/Filtered` (T233, S38)
- Add `GET /Hosts/Environments`, listing the distinct environments of the hosts the caller can see, for the Hosts view facet (T233, S38)
- Add `GET /Hosts/{id}/VulnerabilitySummary` and `GET /Hosts/VulnerabilitySummary?ids=`, counting a host's open vulnerabilities by severity with the same closed-status set as the Master Dashboard (T235, S38)
- Record a field-level change history for hosts and their services, ignoring the last-verification and risk-score timestamps every import stamps, and expose it via `GET /Hosts/{id}/History` (T234, S38)
- Attribute host changes made by a finding import, a Jira Assets import or a Trend Micro sync to that import in the change history (T234, S38)
- Rebuild the desktop Hosts view as a header card over resizable Vulnerabilities, Overview, Services, History and Comments tabs, with a permanent search box, status/team/criticality/environment facets, paging, and a remembered list width and tab (T230, S38)
- Show a host's services as icon chips on the Overview tab and as a Services table with a vulnerability count per service, and list its change history grouped by save with field, actor and date filters (T231, S38)
- Show and edit a host's criticality, environment and owner, painting criticality and vulnerability severity from one tokenized five-step colour ramp (T232, S38)

### Changed
- Return the total host count with each filtered page on the desktop client and sort hosts by name on the server instead of within each page (T233, S38)
- Order a host's vulnerabilities by severity and then most recent detection, and resolve their fix team and analyst names once per host instead of with a blocking request per cell (T230, S38)

### Fixed
- Load a selected host's details on the UI thread and discard responses for a host the user has already left, so a slow request no longer overwrites the panes of the host now selected (T230, S38)

### Security
- Serve host change history only through `GET /Hosts/{id}/History`, which checks the caller can see the host and omits the user record, and refuse host types on the generic audit-trail reader (T234, S38)

## [2.23.0] - 2026-10-01

### Added
- Show and add comments on a host in the Hosts view, via a new `GET /Comments/host/{id}` endpoint (T228)

### Fixed
- Scroll the Jira Assets admin tab so the imported-objects grid at the bottom is reachable (T229)

## [2.22.12] - 2026-10-01

### Fixed
- Allow a Jira Assets object search 120 s per page instead of 30 s, so a slow instance no longer stops an import part-way with "timed out after 30s" (T227)

## [2.22.11] - 2026-10-01

### Fixed
- Say why a Jira Assets search or metadata read failed in transit (timeout, dropped connection, oversized response) instead of reporting "HTTP 0: (no response body)" (T226)

## [2.22.10] - 2026-09-30

### Added
- Show a toast confirming how many Assets object types were loaded from Jira (T225)

### Fixed
- Release and recover the shared LiteDB configuration-store mutex when a caller's read/write throws, instead of every config access (including the auth token) failing the same way forever after just one bad LiteDB open (T224)

## [2.22.9] - 2026-09-30

### Fixed
- Recover the desktop client's session in place when the server rejects its token mid-use — e.g. after it expires across a machine sleep/hibernate — instead of leaving the app stuck resending the same rejected token with no way back but quitting (T223)

## [2.22.8] - 2026-09-30

### Fixed
- Show the server's actual refusal reason on a failed integration GET (which connection, which credential, which upstream error) instead of a generic "Error calling {route}" for every status code (T221)
- Stop the Jira Assets schema and object-type refresh buttons from calling Jira while "Enable Assets" is unchecked (T221)
- Tell the operator to select an Assets schema when the object-type button is clicked before one is chosen, instead of the button silently doing nothing (T222)

## [2.22.7] - 2026-09-29

### Added
- Import Jira Assets schemas, object types, attributes and objects from Jira Data Center connections, while Service Management stays Cloud-only (T220, S34)

### Fixed
- Keep the selected provider shown in the issue-tracker connection form after saving instead of blanking the field when the provider list reloads (T79)
- Keep a Jira connection's stored service-desk queue imports when its Service Management settings are saved without loading the queues first, instead of deleting them (T80)

## [2.22.6] - 2026-09-28

### Added
- Add Jira Server/Data Center as a separate issue-tracker provider authenticating via Personal Access Token (T79)

## [2.22.5] - 2026-09-28

### Added
- Sweep and settle integration sync runs abandoned mid-process on an hourly clock instead of only on the next manual sync (T71, T75)
- Report vulnerability-import and Vision One synchronization progress incrementally instead of jumping from 50% to 100%

### Changed
- Encrypt stored credentials under a dedicated master key held in the host's most protected store (TPM/keychain/DPAPI/owner-only file) instead of one derived from the JWT signing token (T113, S22)

### Fixed
- Persist large vulnerability imports in batches of 500 on a context per batch instead of one long-lived change-tracked context (T45, T46)
- Batch the Vision One virtual-patch and importer auto-close passes instead of one query per record (T72)

## [2.22.4] - 2026-09-25

### Fixed
- Match and batch-write the Vision One inventory sync against a single table read instead of up to five queries per device (T72)

## [2.22.3] - 2026-09-25

### Fixed
- Retry a failed Vision One page up to three times with backoff honouring `Retry-After` instead of failing the whole sync (T71, T72)
- Report the specific class of Vision One connection failure instead of a generic "could not be reached" message (T71)

## [2.22.2] - 2026-09-25

### Changed
- Stretch the Administration window's integration detail form to the column's width instead of a fixed 460px

### Fixed
- Move the register's workflow-status endpoint to `WorkflowStatus` to resolve a route collision with the Track 3 finding-lifecycle status endpoint (T48)

## [2.22.1] - 2026-09-24

### Changed
- Band the vulnerability register grid's rows with a 4% wash under the selection highlight

### Fixed
- Realign the Incident Response Plan window's label/value grid and hide empty life-cycle fields (T41, T42)
- Restore the vulnerability register's toolbar and collapsible details pane under the Semi theme
- Align the four dashboard panels on one title-band/graph-surface shell
- Fix a crash from a missing `SystemListLowColor` static resource after the Semi.Avalonia switch

## [2.22.0] - 2026-09-24

### Changed
- Rebase the desktop client's theme on Semi.Avalonia instead of Avalonia's stock FluentTheme, via a single token file
- Drop the Aura.UI submodule; move Badge and GroupBox into AvaloniaExtraControls

### Fixed
- Size tab headers to their content instead of a hard-coded 15px height that clipped labels under Semi
- Fix the multi-select control's cramped panes and clipped transfer arrows
- Page the Vision One CVE endpoint at 50 devices instead of 200 to stay under the outbound 16 MiB response cap (T72, T73)

## [2.21.18] - 2026-09-24

### Fixed
- Show a posture sync as Running in the Synchronization log as soon as it starts, refreshing every 5 seconds (T71, T75)
- Stretch the Secret Vaults admin tab to the window's width

## [2.21.17] - 2026-09-24

### Fixed
- Give every window its own toast stack so notifications from dialogs are no longer drawn behind the main window (T24)

## [2.21.16] - 2026-09-24

### Added
- Support BastionVault AppRole (app-id) login as an alternative to a pre-minted vault token (S36)

### Changed
- Rework every settings screen's field help, labels and card grouping for readability (S37)

### Fixed
- Stop dropping the App ID field at credential-resolution time for BastionVault connections
- Stop echoing a reflected API key back into a BastionVault connection's stored failure message

## [2.21.15] - 2026-09-23

### Changed
- Install plugin packages into a timestamp-stamped directory, removing the previous installation

### Fixed
- Load a newly uploaded plugin version instead of continuing to serve the previously loaded assembly

## [2.21.14] - 2026-09-23

### Fixed
- Stage plugin upgrade installs beside the target directory instead of under system temp, fixing a cross-filesystem rename failure on Linux

## [2.21.13] - 2026-09-23

### Changed
- Replace Sieve with Gridify for filtering, sorting and paging, translating existing Sieve syntax for compatibility
- Restore the direct `RazorLight` reference that pins FluentEmail's rendering engine to a stable release

### Fixed
- Register filterable host columns under both their localized and invariant names so search works in every locale

## [2.21.12] - 2026-09-23

### Added
- Add the MIGR-TI/IA reference methodology and a phase-by-phase coverage analysis under docs/methodology/ (S27, S28)
- Add Track 9 — MIGR-TI/IA Methodology Alignment to the roadmap as a planned track (M39, M40, M41, M42, M43, M44, M45, M46, M47, M48, M49, M50)

### Changed
- Replace Moq with a real BackgroundServiceHttpContextAccessor in ConsoleClient and BackgroundJobs
- Remove two unused Hangfire storage providers and the NU1608 suppression they required
- Centralize package versions in Directory.Packages.props, resolving a duplicate LiveChartsCore version
- Bump Fido2 to 4.1.0 for the RPID/RPName rename (T70)
- Replace Pomelo.EntityFrameworkCore.MySql with its Microting fork

### Fixed
- Pin SkiaSharp.HarfBuzz and HarfBuzzSharp.NativeAssets.Linux to the resolved SkiaSharp version, fixing a text-shaping TypeLoadException
- Remove the WebSite's dead jQuery UI datepicker script and unserved theme assets
- Bound every outbound HTTP response at 16 MiB to prevent unbounded allocation from a hostile or oversized remote reply

## [2.21.11] - 2026-09-14

### Changed
- Remove reliable-rest-client-wrapper's inner 11-attempt retry loop so only the configured Polly policy governs retries
- Bump Avalonia/ReactiveUI, Microsoft.Extensions.*, EF Core and other dependencies to their latest patch

## [2.21.10] - 2026-09-14

### Fixed
- Widen the gitleaks allowlist path pattern so per-plugin test projects under src/Plugins/ are covered

## [2.21.9] - 2026-09-12

### Fixed
- Refuse to save a vault connection whose own API key is itself stored as a vault reference (S36)

## [2.21.8] - 2026-09-12

### Fixed
- Serve plugin requests from the newest installed copy instead of whichever directory the filesystem enumerates first

## [2.21.7] - 2026-09-11

### Added
- Add plugin deletion from Administration → Plugins, disabling and removing its directory
- Add `NETRISK_PLUGINS_PATH` to relocate the plugins root off the application directory

### Fixed
- Name a plugin's install directory after its assembly so a new version replaces rather than duplicates it

## [2.21.6] - 2026-09-11

### Fixed
- Probe `/v1/sys/health` instead of `/sys/health` so vault-cluster node health scoring works as documented (S36)

## [2.21.5] - 2026-09-11

### Fixed
- Report the actual TLS certificate failure reason instead of a generic inner-exception message
- Stop logging a permission denial at Information level for a request an admin session actually allowed

## [2.21.4] - 2026-09-10

### Added
- Add an App ID field to secret-vault connections for application-identity authorization (S36)
- Add a per-connection "ignore SSL errors" option for secret-vault connections (S36)

## [2.21.3] - 2026-09-10

### Added
- Support a BastionVault cluster DNS name or SRV record on the connection address instead of one node only (S36)

### Changed
- Explain the vault address field's accepted formats on the Secret Vaults form

### Fixed
- Enforce a plugin's required machine identity on save instead of only at resolution time

## [2.21.2] - 2026-09-10

### Fixed
- Read Vision One's real CVE-pass field names, restoring CVE ingestion after a 2.19.6 endpoint move (T73)

## [2.21.1] - 2026-09-10

### Added
- Track integration sync runs with start/finish notifications and a step-by-step progress trail (T71, T75, T79)

### Changed
- Claim a Running sync-log row up front for the issue-tracker poll, JSM mirror and Jira Assets import instead of after they finish (T79)

### Fixed
- Settle an interrupted sync as Failed on disposal instead of leaving it Running until the two-hour reaper horizon

## [2.21.0] - 2026-09-10

### Added
- Add plugin package upload from Administration → Plugins, validating the archive before extraction

## [2.20.3] - 2026-09-10

### Fixed
- Apply the documented `header`/`header3` style classes to four windows that referenced non-existent style classes

## [2.20.2] - 2026-09-10

### Added
- Add a UI-standard CI gate (`LintUi`) and a UI compliance section to the PR template (T5)

### Changed
- Move the BastionVault plugin to its own external repository, no longer built by this solution
- Localize twenty hard-coded window titles that had shown their view's class name (T2)

## [2.20.1] - 2026-09-09

### Changed
- Make the BastionVault secret picker's field a free-text suggestion box instead of a listing call (S36)

### Fixed
- Rewrite the BastionVault plugin protocol to match the real HashiCorp-Vault-compatible API (S36)
- Verify rather than send the machine ID, checked against the token's own `spiffe_id` (S36)

## [2.20.0] - 2026-09-09

### Added
- Add external secret vaults and the BastionVault plugin implementing them, resolved server-side and never returned to a client (S36)

### Changed
- Route every credential read through `ISecretResolver` instead of `ISecretProtector.Unprotect` (S36)
- Split CI API tokens into their own administration section instead of a tab of the findings-admin screen (T58)

### Fixed
- Back off and suppress repeated token-refresh failures instead of retrying every 10 seconds
- Fix "Could not issue an API token" and several REST error-reporting regressions across the governance/IRP/API-token clients (T58, T121)
- Stop following redirects on REST calls so an expired session reports 401 instead of a JSON parse error
- Resolve findings-window host/team/entity columns in one bulk request instead of one blocking call per grid cell

## [2.19.7] - 2026-09-09

### Fixed
- Reconcile the entity property bag by type and value instead of by client-supplied row ids, fixing three save failures (T35)
- Fill the Mitigation dialog's height instead of leaving 40% dead space
- Show entity display names instead of the raw DAL class name on the governance admin pickers (T129, T137)
- Show reviewer and appetite-scope names instead of raw ids on the governance admin grids (T129, T137)

## [2.19.6] - 2026-09-09

### Fixed
- Point the Vision One risk-score and CVE passes at the real `attackSurfaceDevices` endpoint (T72, T73)
- Read Vision One's `latestRiskScore` field, restoring risk-score ingestion (T74)
- Request a valid page size in the Vision One test-connection probe (T71)

## [2.19.5] - 2026-09-08

### Fixed
- Split REST writes onto a non-retrying client so a 5xx is reported instead of retried up to eleven times (T71, T75)
- Fix the upstream retry-amplification bug in reliable-rest-client-wrapper
- Model the retrying client's real policy in the client test stub instead of a no-op

## [2.19.4] - 2026-09-08

### Fixed
- Refuse a duplicate integration sync with 409 instead of allowing eleven concurrent runs (T71, T75)
- Settle a sync stuck Running for over two hours as Failed
- Show the server's structured refusal message instead of raw JSON in the integration admin views
- Fix two ComboBox bindings that erased the Vision One region and issue-tracker provider on save (T71, T65)

## [2.19.3] - 2026-09-08

### Fixed
- Complete a sync as Failed when its credential cannot be decrypted, instead of leaving it Running forever
- Log the refusals the integration endpoints had been answering silently
- Read error responses through a non-throwing REST client so refusal reasons reach the operator

## [2.19.2] - 2026-09-08

### Fixed
- Share one error message across Vision One's connection test and sync passes that names the endpoint and quotes Vision One's own reason (T71)

## [2.19.1] - 2026-09-03

### Changed
- Declare the tracked branch on every vendored submodule so Dependabot cannot propose reverting a fork's port
- Verify a combined state across twelve Dependabot updates before merging them together

### Fixed
- Reject a submodule bump that moves the pinned commit backwards, regardless of its PR description (T110)

## [2.19.0] - 2026-09-03

### Added
- Read Jira Service Management service desks, request types, queues and SLA cycles live (T79, T80)
- Import Jira Assets registers for applications, servers and machines (T82)
- Widen `finding_issue_links` so a ticket can hang off an incident or a risk, not only a finding (T81)
- Map NetRisk values onto any Jira field, including custom fields picked from the site's own field list (T79)

### Changed
- Add a live preview for issue templates before they are saved (T84)
- Link an imported Jira Assets object back to its Jira page by object key (T85)
- Make the issue-tracker status mapping grid editable with a duplicate guard and a "load from Jira" action (T83)
- Add editors for the Jira title/description templates and severity→priority mapping (T83)
- Add a Jira Assets tab with field-mapping, Service Management and Assets sub-tabs (T79)
- Add schema version 83 (upgrade phase 14): eight additive tables, two host columns, and the finding_issue_links widening

### Fixed
- Fix 96 build warnings across the solution (nullable annotations, a duplicate resx key, blocking calls in tests)
- Fix a missing `IssueTemplatePlaceholdersMSG` resource key and add a localization-coverage test

## [2.18.0] - 2026-08-31

### Changed
- Stretch Administration section headers and the risk screen's Vulnerabilities header to the panel's full width
- Fill the risk screen's vulnerabilities grid to the remaining viewport height

### Fixed
- Derive the token-renewal window from the token's own lifetime instead of a fixed 300-minute floor, fixing an infinite renewal loop (T112)
- Fix four build warnings on the Track 8 governance screens (T130)

## [2.17.4] - 2026-08-28

### Added
- Add `make docker-release` to build and push all four container images with one command

### Fixed
- Read the database credential from the deployment host's env file as a configuration source

## [2.17.3] - 2026-08-28

### Fixed
- Resolve the database connection string through one guard that names the missing setting instead of silently defaulting to localhost
- Load `/netrisk/netrisk.env` from a `netrisk-console` wrapper on PATH instead of relying on `docker exec` inheriting entrypoint exports
- Reissue the local development TLS certificate for ten years with correct SANs, and add an expiry-warning test
- Restore any project the solution declines to build before scanning it for vulnerable dependencies

## [2.17.2] - 2026-08-28

### Fixed
- Pin the .NET SDK to 10.0.302 instead of the unpublished 10.0.0, and fix the gitleaks config so the security workflow runs at all
- Narrow the gitleaks `nrk_`/`scim_` and connection-string rules to match real credential shapes instead of schema identifiers
- Record and flag for rotation a credential found by the first completed full-history gitleaks scan (S19)

## [2.17.1] - 2026-08-28

### Changed
- Assert in each Dockerfile that the base image runs `openvox-agent`, not `puppet-agent`

### Fixed
- Exec the given command in the console container's entrypoint instead of leaving it unreachable after the keepalive
- Stop shell-parsing the netrisk.env connection string, fixing a container restart loop introduced by the NR-2026-025 credential move
- Restage the Puppet modules tree on every Docker packaging build instead of only when absent

## [2.17.0] - 2026-08-26

### Added
- Add Track 8 — formal expiring risk acceptance, inherent/residual scoring, a server-side approval state machine, segregation of duties, risk appetite, and a field-level audit trail (T120, T121, T122, T123, T124, T127, T128, T129, T131)
- Stand up the Business Risk Acceptance Portal (src/RiskPortal), consumed by entity-appointed reviewers (T136, T137, T138, T139)
- Push review cadence notifications and repair the pending-risk intake pipeline; add mitigation_tasks line items (T125, T133, T134, T135)
- Add an auditor evidence export (CSV and PDF) per entity and period (T132, T140)
- Add quantitative likelihood/impact definitions and a FAIR-lite Monte Carlo scoring method (T141, T142)
- Add schema versions 80, 81 and 82 (upgrade phases 11, 12 and 13) for the governance core, review portal and deferred security findings
- Add a 34-finding Track 7 security register naming how each finding was established (T104, T105, T106, T107, S19)
- Add continuous security gates in CI: CodeQL, gitleaks over the full history, a dependency scan and a submodule-provenance check (T108, T110, T117)
- Publish SECURITY.md's disclosure policy and an internal triage SLA (T118)
- Schedule periodic re-audits and track remediation burn-down against the findings register (T119)
- Add a CycloneDX SBOM generated at build time from the resolved dependency graph (T109)
- Add progressive login throttling and rate limiting on the credential endpoints (T112)
- Add security response headers (HSTS, CSP, X-Frame-Options, and others) on the API and the WebSite (T116)
- Read configuration from environment variables with file → user-secrets → environment precedence (T113)
- Wire automated Windows Authenticode and macOS Developer ID/notarization signing into the Nuke build (T86, T87)
- Add the Windows MSI/MSIX, drag-and-drop macOS DMG, and Linux Flatpak/Snap packaging targets (T88, T89, T90)
- Add a `netrisk.ini` overlay so administrators can pre-seed the desktop client's server URL
- Add a release-engineering guide for cutting a signed build (S10)
- Add Track 4.1 unified notification channels: Email, Slack, Teams and generic webhooks behind one dispatcher (T62, T63, T64)
- Add Track 4.2 bi-directional issue sync with Jira, GitHub, GitLab and Azure DevOps (T65, T66, T67)
- Add Track 4.3 hardened enterprise authentication: OIDC/SAML SSO, SCIM provisioning, and WebAuthn/FIDO2 (T68, T69, T70)
- Add Track 4.4 Trend Micro Vision One integration: inventory sync, CVE ingestion, and Cyber Risk Index scoring (T71, T72, T73, T74)
- Add Track 4.5 SecurityScorecard integration: posture sync, CVE and issue ingestion (T75, T76, T77, T78)
- Add schema version 79 (upgrade phase 10): fifteen new integration tables plus posture columns on hosts and entities
- Encrypt integration credentials at rest with no endpoint ever returning them

### Changed
- Turn off SAML by default and clamp the JWT lifetime to a 1440-minute ceiling, logging any configured excess (T112)
- Allow TLS 1.2 alongside 1.3 rather than 1.3 alone, since .NET on macOS does not offer 1.3 in the server role (T114)
- Re-encrypt integration credentials with AES-256-GCM on save, upgrading the old unauthenticated format after a round-trip check (T115)
- Publish Windows and Linux packaging from one shared compiled output (T88, T90)
- Package the macOS DMG around the signed app instead of the installer package (T89)
- Add a real macOS bundle icon and camera-usage purpose string (T87)
- Localize the thirteen remaining hard-coded GUI labels and the multi-select picker headers (T2)
- Show the score delta on the risk register list and add a pre/post-treatment table to the Detailed Entities Risks report (T126)
- Raise finding-lifecycle, risk, incident, IRP-task and scan-import events as notifications (T64)

### Fixed
- Report a rejected write as the server's own refusal instead of a generic network failure
- Fix the Impact-vs-Probability report sending its minimum score twice and its maximum never
- Stop `JobManager` preventing the API from starting in Development
- Stop `EmailService` accumulating recipients across sends inside one injected instance

### Security
- Close the five findings Track 7 left open: persisted lockout counters, per-file attachment authorization, a 0600 Puppet credential file, per-session logout revocation, and column-encrypted FaceID templates (NR-2026-008b, 017, 025, 028, 032) (T112, T115)
- Verify a plugin's publisher before loading it, report-only by default (NR-2026-027, risk-accepted) (T111)
- Fix a critical SSO flow that let an attacker who controlled the request id collect any user's session token (NR-2026-001) (T111)
- Draw the JWT signing key, reset links, file keys and the FaceID liveness challenge from a CSPRNG instead of a shared non-cryptographic generator (NR-2026-002) (T112)
- Stop shipping a committed, expired TLS private key in the default configuration (NR-2026-003) (T113)
- Validate the server certificate on every desktop-client connection instead of accepting any certificate (NR-2026-004, NR-2026-005) (T114)
- Validate chunked-upload file ids against an allowlist and a resolved-path check (NR-2026-006)
- Check the `enabled` flag on the Basic authentication path, not only on the JWT path (NR-2026-007)
- Persist failed-login counters and enforce SAML assertion signature validation by default (NR-2026-008, NR-2026-010) (T112)
- Encrypt stored integration credentials with AES-256-GCM instead of a constant-key/constant-IV AES-CBC scheme (NR-2026-011) (T115)
- Add an `[Authorize]` attribute to the WebAuthn enrolment endpoints and a reflective authorization-inventory test (NR-2026-009) (T111)
- Pin JWT issuer/audience/algorithm, shorten the default lifetime, and revoke sessions on password change (NR-2026-012) (T112)
- Refuse outbound integration requests to link-local and cloud-metadata addresses (NR-2026-013) (T114)
- Guard the base64 credential decode and stop truncating passwords containing a colon (NR-2026-018)
- Compare unsigned webhook secrets in constant time (NR-2026-019)
- Move uploaded scan-report staging off a world-writable `/tmp` directory (NR-2026-020) (T115)
- Parameterise three `information_schema` queries and confirm DTD processing is refused by the three live scanner importers (NR-2026-021, NR-2026-022)
- Hash password-reset links with SHA-256 instead of MD5, on the API and the WebSite (NR-2026-014)
- Force the SAML session cookie to `Secure` always (NR-2026-016)
- Validate scan-report links as absolute http/https with no whitespace, passing arguments as a list (NR-2026-023) (S17)
- Log when the website sync is running with certificate validation disabled (NR-2026-026)

## [2.16.3] - 2026-08-25

### Changed
- Reorganize the Vulnerability editor dialog into a responsive two-pane layout per the UI standard (S1, S2)
- Format the score spinner to two decimals and add a step of 0.1
- Remove the vulnerability dialog's duplicate risk-filter search box
- Show the Save-gating validation rules in the window instead of only in a disabled-button tooltip

## [2.16.2] - 2026-08-25

### Fixed
- Build the Vulnerabilities view's own view model in its constructor instead of a shell field initializer, fixing a completely blank window
- Stop the finding-lifecycle grid column from boxing an enum through an unsupported TreeDataGrid expression-tree path, fixing a startup SIGABRT (T48)

## [2.16.1] - 2026-08-25

### Added
- Add a root Makefile as the discoverable entry point for `make gui`, `build`, `test` and the other everyday developer commands

### Fixed
- Guard every numbered Structure script statement by statement instead of wrapping it in a transaction MariaDB implicitly commits anyway (T91)
- Wrap every numbered Data script in a real transaction with the db_version bump as the genuine commit point (T91)
- Rename the risk-acceptance evidence FK's target from `files` to `nr_files` in Structure/77.sql, fixing a schema upgrade that aborted partway through 78 (T50)
- Try the bare, Window- and Dialog-suffixed view-model naming conventions in DialogService, fixing a SIGABRT on Add/Edit Risk and six other dialogs
- Publish `PackageMacGUI`'s osx-x64 build natively on Apple Silicon instead of under QEMU, fixing an indefinite hang
- Enforce a 30-minute timeout on external build commands so a wedged process fails the build instead of blocking it forever

## [2.16.0] - 2026-08-24

### Added
- Add Track 3 extensible scanner importers: a versioned plugin contract plus ten built-in importers including SARIF 2.1 (T43, T44, T45, T46, T47)
- Add Track 3 finding lifecycle and audit trail: a seven-state triage machine with sticky triage, regression detection and an append-only history (T48, T49)
- Add Track 3 formal, expiring risk acceptance for findings with a daily T-30/T-7 expiry job (T50, T51)
- Add Track 3 the deduplication engine: layered per-scanner strategy chains with a merge preview (T52, T53, T54)
- Add Track 3 SLA tracking and aging: effective-dated policies, computed due dates, and a deduplicated breach digest (T55, T56, T57)
- Add Track 3 CI/CD-first integration: scoped API tokens, idempotent bulk upload, and `netrisk-console ci gate` (T58, T59, T60, T61)
- Build the Master Dashboard end to end: neither the endpoint nor the service existed despite being documented as complete (T37, T38)
- Add IRP template editing, automation-rule authoring and a critical-path Gantt view (T39, T40, T41, T42)
- Enforce multi-entity scoping via EF global query filters and a SaveChanges guard, correcting a previously undocumented gap (T35, T36)
- Add API controller test coverage from 8.7% to 77.1% across 31 controllers
- Add ClientServices test coverage from 5.2% to 70.6% across 26 services
- Add test projects for SharedServices, BackgroundJobs, ConsoleClient and WebSite

### Changed
- Require new features and bug fixes to ship with tests in the same change (recorded in CLAUDE.md and src/AI_TESTING_INSTRUCTIONS.md)

### Fixed
- Change the `char(36)` store type on `ClientActionId` to `varchar(36)`, fixing an EF Core 10 model-build NullReferenceException
- Strip leading zeros so `CWE-089` and `CWE-89` match as the same weakness in importer dedup keys (T52)
- Evaluate the failed-import check before a gate policy of `none`, so a failed scan cannot report success (T61)
- Declare the schema-upgrade 75/76 SQL scripts as project content so a packaged console client actually ships them
- Collapse duplicate-language locales instead of throwing from the language-list getter
- Make the desktop client's memory cache check its own expiry synchronously instead of serving stale entries from a racing background sweep
- Fix six client-side writes that reported a rejected server response as a successful save

## [2.15.0] - 2026-08-21

### Added
- Add Track 1 Milestone 1.5 — Interaction & Workflow Standardization, completing Track 1 (T22, T23, T24, T25, T26)
- Add GUIClient.Tests, the desktop client's first test project
- Close the full docs/ui-standard.md compliance sweep across all 80 views, completing Track 1 (T1, T6, T7, T8, T9, T10, T11, T12)
  - note: no dedicated per-item changelog entry exists for Milestones 1.2–1.4's individual tasks in the pre-migration file; version attributed from the roadmap's own "Milestones 1.1–1.5 shipped in 2.15.0" note

### Changed
- Upgrade the five `libs/` submodules and reattach three detached HEADs to their tracking branches
- Migrate the test suite from xUnit v2 to xUnit v3 on Microsoft.Testing.Platform across all five test projects
- Refresh dependencies across the solution at patch/minor level (EF Core, Microsoft.Extensions.*, Serilog, and others)
- Move `SkiaSharp` off a preview build onto the stable 3.119.4 release
- Upgrade Avalonia 12.0.2 → 12.1.1 and ReactiveUI 23.2.19 → 24.1.0, porting 359 generic-argument sites to `RxVoid`
- Replace the deprecated `Serilog.Sinks.RollingFile` with `Serilog.Sinks.File`, giving the desktop client real daily log rolling

### Fixed
- Replace the desktop client's stubbed validation layer, dead since a February 2026 regression, with an in-tree ValidationContext (T22)
- Change `ProcessedSyncAction.ClientActionId`'s mapping to avoid EF Core 10's char-collection NullReferenceException
- Register `IIrpAutomationService` in the ServerServices test container so `IncidentsService` can be constructed
- Bump the transitive `System.Security.Cryptography.Xml`, `SQLitePCLRaw.bundle_e_sqlite3` and `SSH.NET` dependencies for CVE-2026-50525, CVE-2026-47302, CVE-2025-6965 and CVE-2026-48798

## [2.14.2] - 2026-06-25

### Fixed
- Let labelled operation buttons grow to fit their content instead of clipping to a fixed 25×25 size

## [2.14.1] - 2026-06-25

### Fixed
- Zero the padding and size the icon on the admin navigation buttons, fixing a clipped-sliver rendering

## [2.14.0] - 2026-06-18

### Added
- Decouple the public WebSite from the main database via a local SQLite store and a signed, ECDSA-authenticated periodic `/sync` (S37)
- Add `netrisk-console keys` and `website enroll` commands to manage the sync signing keypair and TOFU-enroll a site
- Add configurable website sync intervals, including a fast lane for password-reset-sensitive actions
- Add the `processed_sync_actions` idempotency ledger (db_version 75)

## [2.13.4] - 2026-06-18

### Added
- Support per-question answer options in JSON and Excel assessment template imports (T33)

### Fixed
- Persist the bundled NIST CSF 2.0 and ISO 27001:2022 starter templates' answer options, which had been imported as empty dropdowns (T33)

## [2.13.3] - 2026-06-18

### Changed
- Route editing an assessment execution through the paged run viewer too, retiring the last flat-grid answer editor (T31)

## [2.13.2] - 2026-06-18

### Added
- Add a read-only preview mode to the assessment builder so authors can see the run viewer's rendering before publishing (T31)

### Changed
- Route new assessment executions through the paged run viewer with auto-saved drafts and a Submit action (T31, T32)
- Redesign the assessment questionnaire builder as an inline card canvas with structured show/hide rules (T31)

### Fixed
- Auto-size the User Info dialog's logout button instead of clipping its label
- Make the product version a single source of truth in Directory.Build.props instead of sixteen hard-coded project files
- Widen the assessment builder's answer-option Risk field and auto-size its Add button
- Make assessment page, order and rich-text explanation editable when authoring questions manually (T31)

## [2.13.1] - 2026-06-17

### Fixed
- Resolve `IQuestPdfRenderingService` from the test DI container so `ServerServices.Tests` builds again

## [2.13.0] - 2026-06-17

### Added
- Add an interactive paged assessment-run viewer with server-enforced conditional show/hide and auto-saved drafts, completing Milestone 2.2 (T31, T32, T34)
- Add an assessment template import dialog with dry-run preview validation for JSON and Excel (T33, T34)
- Bundle NIST CSF 2.0 and ISO/IEC 27001:2022 Annex A starter assessment templates (T33)
- Generate file reports directly from report templates, not only as scheduled email exports (T27)

### Changed
- Restyle the Create Report dialog to the standard dialog visual identity

## [2.12.8] - 2026-06-17

### Changed
- Rebuild the Report Template and Schedule Manager windows onto the standard master/detail layout (T30)

### Fixed
- Make the report manager selections nullable so Update/Delete/Test no-op instead of null-dereferencing
- Replace the deprecated `Watermark` property with `PlaceholderText` on two remaining TextBoxes

## [2.12.7] - 2026-06-17

### Fixed
- Eager-load report template versions on `GET /ReportTemplates` so the schedule dialog's version dropdown is no longer always empty

## [2.12.6] - 2026-06-17

### Fixed
- Validate a required-parent entity definition client-side instead of surfacing the server's rejection as a generic 500
- Bind the assessment-run dialog's entity AutoCompleteBox's SelectedItem, fixing "Could not parse entity id from selection"
- Register every DialogViewModelBase-derived view-model by reflection, fixing dialogs that crashed with a missing DI registration
- Restyle the Report Template and Schedule Manager windows onto the app's visual identity instead of raw unstyled controls

## [2.12.5] - 2026-06-17

### Added
- Add a structured report-template designer with branding, presets and a live rendered PDF preview (T27, T30)
- Add a scheduled-export frequency builder and last-run status to the schedule manager (T28, T30)
- Add client-side CSV and Excel export actions to the Reports views (T29)

## [2.12.4] - 2026-06-17

### Changed
- Replace the separate PDF/CSV/Excel toolbar buttons with a single Export dialog on the Risks, Vulnerabilities, Hosts and Incidents views (T29)

### Fixed
- Render wide PDF reports in landscape with humanized headers, switching to a card layout past nine columns
- Parse "Name (id)" selections through a shared, exception-free helper instead of crashing on a malformed selection

## [2.12.3] - 2026-06-17

### Added
- Add the report-template designer, scheduled-export screen and PDF/CSV/Excel export actions to the GUI (T30)

## [2.12.2] - 2026-06-16

### Fixed
- Fix the assessment question editor's layout and make Save commit an in-progress answer edit instead of discarding it
- Top-align the assessment questions grid's ID and Actions columns
- Size the IRP window's attachment Download/Delete icons instead of rendering blank squares
- Fix the mitigation and management-review editor window layouts

## [2.12.1] - 2026-06-16

### Fixed
- Add the missing `AddUserEntityRoles` migration and numbered SQL (db_version 74) that 2.11.0's scoped roles shipped without

## [2.12.0] - 2026-06-15

### Added
- Add customizable Incident Response Plan templates with automated task generation and assignee notifications (T39, T40)

## [2.11.0] - 2026-06-15

### Added
- Add multi-entity and multi-tenant scoping: business-entity segregation and role-based scoped access (T35, T36)
  - note: this initial pass was corrected in 2.16.0 after `ApplyEntityScope` was found to filter nothing (T35)

## [2.10.0] - 2026-06-15

### Added
- Add the enhanced assessments workflow: a paged viewer, progress tracking, draft auto-save and template import (T31, T32, T33)

## [2.9.0] - 2026-06-15

### Added
- Add the advanced reporting engine's core export service, customizable report templates and scheduled GRC report exports (T27, T28)

## [2.8.0] - 2026-06-12

### Added
- Add Track 6 upgrade tooling: `schema_upgrade_log`, `netrisk-console database upgrade-schema`, `database baseline`, and `DAL.IntegrationTests` (T91, T92)
- Document the Track 6 naming convention in CLAUDE.md (T93)

### Changed
- Fix invalid `0000-00-00` defaults and index-name typos (Milestone 6.2 phase 1, db_version 64) (T94)
- Rename 8 PascalCase tables and hybrid camelCase columns to snake_case (Milestone 6.2 phase 2, db_version 65) (T98)
- Normalize boolean columns from tinyint(4) to tinyint(1) (Milestone 6.2 phase 1b, db_version 66) (T95)
- Snake-case the last stray column, `comments.IsAnonymous` (Milestone 6.2 phase 2b, db_version 67) (T96)
- Convert all 99 base tables to utf8mb4/utf8mb4_unicode_ci (Milestone 6.2 phase 1c, db_version 68) (T97)
- Add FK constraints and EF navigations for orphan id columns (Milestone 6.3 phase 3, db_version 69) (T99)
- Add query-justified hot-path indexes and convert BLOB-for-text columns (Milestone 6.3 phase 4, db_version 70) (T100)
- Migrate `risks.status` to an int-backed enum via create-copy-coexist (Milestone 6.4 phase 5, db_version 71) (T101)
- Deprecate 23 unreferenced tables and orphan columns, reversibly (Milestone 6.4 phase 6a, db_version 72) (T102)
- Drop the deprecated tables and columns after the observation window (Milestone 6.4 phase 6b, db_version 73, destructive) (T103)

## [2.7.7] - 2026-06-11

### Changed
- Raise DAL test coverage to ~99% and ServerServices coverage from ~11% to ~90%, with an EF Core in-memory test harness

## [2.7.6] - 2026-06-10

### Changed
- Stream file uploads in 5 MB chunks instead of one base64-encoded JSON body, fixing uploads over ~22 MB

### Fixed
- Persist a chunked upload's `NrFile` record and entity association at finalize time, fixing orphaned attachments
- Raise and make configurable the API's request-body size limit

## [2.7.5] - 2026-06-10

### Fixed
- Remove a stale CommandParameter on the Edit Incident file-add button, fixing a ReactiveCommand type-mismatch crash
- Wrap file-upload calls in a try/catch across five upload sites instead of crashing on a failed upload

## [2.7.4] - 2026-06-09

### Fixed
- Set `NUGET_CERT_REVOCATION_MODE=offline` for the Docker cross-publish restore, fixing an NU3012 signature-revocation failure on Apple Silicon

## [2.7.3] - 2026-06-09

### Fixed
- Add a .gitattributes pinning shell-script line endings to LF, fixing a baked-in `\r` shebang that broke container entrypoints

## [2.7.2] - 2026-06-09

### Fixed
- Set container payload ownership at copy time (`COPY --chown`) instead of a recursive Puppet chown, fixing an image-export failure

## [2.7.1] - 2026-06-08

### Fixed
- Skip absent per-platform installer artifacts when building the website Docker image instead of aborting the whole run

## [2.7.0] - 2026-06-08

### Changed
- Upgrade `Pomelo.EntityFrameworkCore.MySql` to a 10.0.0 release-candidate build sourced from a private feed

## [2.6.2] - 2026-06-03

### Fixed
- Zero the padding and size the icon on `Button.subButton` toolbars, fixing clipped glyphs

## [2.6.1] - 2026-06-03

### Fixed
- Fix the search-toggle button's invalid Material icon name, which rendered as fallback glyph text

## [2.6.0] - 2026-06-03

### Added
- Add macOS global menu redirection and platform-native window-control alignment (T18, T19)
- Sweep keyboard accessibility: global Ctrl+P/S/F, Esc, and TabIndex/IsDefault/IsCancel ordering (T20)
- Add system tray / menu-bar-extra integration with a quick status preview (T21)

### Fixed
- Bind the macOS navigation-bar inset explicitly against MainWindow's DataContext, fixing the notification bell overlapping the traffic lights (T19)

## [2.5.1] - 2026-06-03

### Fixed
- Promote 194 bound view-model members from private to public across 26 view-models, fixing widespread broken bindings under compiled bindings (T17)

## [2.5.0] - 2026-06-03

### Added
- Enable compiled bindings globally with explicit `x:DataType` on every view (T13, T15)
- Add a high-performance virtualizing TreeDataGrid for the dense vulnerability grid (T16)
- Add explicit VirtualizingStackPanel to the primary dense data lists (T16)
- Add the UI standard compliance audit and remediation plan (S3, S4)

### Changed
- Upgrade Avalonia 11.3.11 → 12.0.1 and its dependent packages across GUIClient, AvaloniaExtraControls and Aura.UI
- Complete the GUIClient UI compliance pass: canonical button classes, resx-based strings, MinWidth-based responsive inputs (T3, T4)

### Fixed
- Restore macOS window dragging by constraining the custom title-bar menu's width
- Accept both `--environment=dev` and `--environment dev` argument forms
- Fix compile-time binding errors surfaced by enabling compiled bindings across several view-models (T14)
- Fix two high-severity transitive dependency vulnerabilities in `Tmds.DBus.Protocol` and `System.Security.Cryptography.Xml`

## [2.2.0] - 2026-02-06

### Added
- Add responsive layouts and DataGrid columns to EditRiskWindow and RisksPanelView

### Changed
- Upgrade to .NET 10.0 with C# 13 across all projects

### Fixed
- Fix EditRiskWindow's button placement and right-panel overlap on resize
- Fix risk deletion and closure bugs

## [2.1.4] - 2025-09-27

### Fixed
- Fix a risk closure bug

## [2.1.0] - 2025-08-27

### Added
- Add a search on the incident response plan list
- Add a risk calculation command-line command
- Add the plugin system, FaceID plugin verification, registration and risk-closure verification
- Add the security classification, organization data and organization data group entities

### Changed
- Upgrade to Avalonia 11.3 and .NET 9

### Fixed
- Return to the first pagination page on the risk vulnerability list after selecting a new risk
- Fix the incident response plan list search
- Fix a bug in risk association
- Stop the contributing score considering closed vulnerabilities
- Fix a bug in closing the incident response plan window

## [2.0.7] - 2025-08-01

### Changed
- Filter the incident window to only show approved incident response plans

### Fixed
- Fix risk vulnerability pagination and risk loading time
- Add a missing scroll view on the incidents window
- Remove a leftover foreign key on the incident response plan

## [2.0.6] - 2025-07-01

### Added
- Add risk vulnerability pagination

### Fixed
- Fix risk loading time

## [2.0.0] - 2025-06-01

### Added
- Add Incident Management and Incident Response Plans
- Add new dashboard graphics with improved performance
- Add last-import date on vulnerability data and filters on the entity list

### Fixed
- Fix several bugs; see [GitHub issues](https://github.com/ffquintella/netrisk/issues)

## [1.7.1] - 2024-11-06

### Added
- Add vulnerability chat tracking and improved e-mail communication
- Start using .NET migrations to manage the database schema

### Fixed
- Fix several bugs; see [GitHub issues](https://github.com/ffquintella/netrisk/issues)

## [1.6.1] - 2024-10-15

<!-- source content elided in the pre-migration file; nothing to migrate -->

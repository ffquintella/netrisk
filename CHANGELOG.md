# Change Log
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

## [NEXT] - Unreleased

This release includes new features and improvements.

### Added

### Changed

### Fixed

- **A missing `Database:ConnectionString` now says so, instead of meaning `localhost`.** An empty or
  absent connection string reached `new MySqlConnection("")` at six call sites in `DatabaseService`
  and `SchemaUpgradeService`, and MySqlConnector reads that as `server=localhost;port=3306`. So the
  setting being unset surfaced either as `Unable to connect to any of the specified MySQL hosts` —
  naming neither the setting nor the fallback — or, on a host that happens to run a local MariaDB, as
  a *successful* connection to the wrong database, reported by `netrisk-console database status` as
  `Schema does not exist`. Both diagnoses point at the database server, and the fault is in the
  configuration. All six sites now resolve through one guard,
  `DatabaseConnectionStringResolver`, which fails with a message naming `Database:ConnectionString`,
  its `Database__ConnectionString` environment form, and the `docker exec` export-inheritance trap
  that produced the original incident. `database status` reports the new status `Misconfigured`
  rather than `Offline`, `init`/`update`/`upgrade-schema`/`baseline` repeat the message instead of
  claiming the database is offline, and `Backup`/`Restore` resolve outside their catch-alls so a
  missing setting can no longer be logged and swallowed. `upgrade-schema --dry-run`, which needs no
  database, still runs with none configured.

- **`netrisk-console` can reach the database on a deployed host.** Every `netrisk-console database …`
  command failed with `Unable to connect to any of the specified MySQL hosts`, on every deployed
  environment, since the credential moved out of `appsettings.json` (security finding NR-2026-025).
  The credential now lives only in `/netrisk/netrisk.env`, which the container entrypoint loads into
  *its own* environment — PID 1's. But the console container is a keepalive, so every operator
  command arrives as `docker exec … netrisk-console <command>`, and a `docker exec` builds a fresh
  environment from the image configuration and inherits none of those exports. With the
  Puppet-rendered `/netrisk/appsettings.json` deliberately carrying a comment where the connection
  string used to be, `Database:ConnectionString` resolved to null, MySqlConnector fell back to its
  default `localhost:3306`, and the database is a separate container — so the connect was refused
  instantly and the error named a database server that had never been configured instead of the
  setting that was missing. `netrisk-console` is now a wrapper installed on `PATH` in the image that
  loads `/netrisk/netrisk.env` itself, warns by name when the variable is still absent, and runs the
  binary from `/netrisk` (where `appsettings.json` must be resolved from, since the console registers
  it with `optional: false`). Its copy of the loader is byte-identical to the four entrypoints' and a
  test holds all five that way, because reading that file with `.` is what caused the 2.17.0 restart
  loop.

- **Local development TLS material is no longer three years expired.** The self-signed certificate
  `src/API` and `src/WebSite` serve with (`https:certificate:file`) was issued in September 2022 with
  a one-year lifetime and expired on 2023-09-14, so every local client failed its handshake — the
  desktop client reporting only `The SSL connection could not be established`, which names neither
  the certificate nor its expiry. Reissued for ten years with `localhost` and `127.0.0.1`
  subjectAltNames, which the old certificate lacked entirely (hostname validation matches a literal
  IP against an iPAddress SAN, never against the common name, so the configured
  `https://127.0.0.1:5443/` could not have validated even once trusted). Reissuing is now one
  command, `./scripts/security/generate-dev-certificates.sh`, and a test fails 30 days *before*
  expiry rather than after. The file names and the placeholder password are unchanged, so
  `Tools.Security.CommittedCertificates` still refuses to boot a host configured with this material.

- **The vulnerable-dependency gate can reach every project in the solution.** With the workflow
  finally running past `setup-dotnet`, the scan failed on `build/build.csproj` with "No assets file
  was found". `dotnet list package` enumerates every project in the solution and needs an assets
  file for each, but `dotnet restore <solution>` does not produce one for every project a solution
  contains: a project mapped with `ActiveCfg` and no `Build.0` is skipped, which is precisely how
  Nuke registers the build project so that building the solution does not build the build script.
  It reproduced locally the moment `build/obj` was removed — the gate had only ever passed on a
  machine where `./build.sh` had restored that project as a side effect. `scan-dependencies.sh` now
  restores by name any project the solution declines to build, rather than skipping it: the Nuke
  project pulls its own transitive graph into the release process, which is exactly the supply chain
  this gate exists to watch. A test derives the list of such projects from `netrisk.sln` itself, so
  adding another one without restoring it fails in `dotnet test` instead of in CI.



## [2.17.2] - 2026-08-28

This release includes new features and improvements.

### Added

### Changed

### Fixed

- **The `security` workflow now runs.** Every one of its four gates had failed on every run since the
  workflow was added, for two unrelated reasons, and the two masked each other — the secret scan's
  red X read as more of the same `setup-dotnet` breakage sitting next to it.
  - `global.json` pinned SDK `10.0.0`. That is a *runtime* version; .NET SDK feature bands start at
    `.100`, and no SDK archive has ever been published under `10.0.0`. Both `actions/setup-dotnet`
    (via `global-json-file`) and `build.sh` (via `dotnet-install --version`) install that exact
    string, so both 404 — the CodeQL and dependency-scan jobs never reached their first real step,
    and neither would a bootstrap build on a machine without an SDK. `rollForward` hid it from
    anyone who already had one. Now pinned to `10.0.302`.
  - `.gitleaks.toml` used a negative lookahead. gitleaks compiles with RE2, which has no lookaround,
    so it panicked while translating the config and scanned nothing at all. Both lookaheads were
    removable without loss: one was already implied by the value's character class, the other is now
    a leading non-whitespace character.
- **The gitleaks rules match credentials rather than the schema.** With the gate finally running, a
  full-history scan returned 89 findings, 85 of them noise. The `nrk_`/`scim_` rules matched "the
  prefix plus 20 word characters", which is every table, index and foreign key the SCIM feature owns
  (`idx_scim_request_logs_occurred_at` and 71 more); they now match the shape the services actually
  issue — prefix, 16 hex characters of key id, `_`, then the base64url secret. The
  connection-string rule matched `password = expr` with spaces, which is an assignment in C#, Puppet
  or shell, not a connection string; it now requires `pwd=value`. The remaining 11 findings are
  genuine and pre-existing, and are baselined by fingerprint in a new `.gitleaksignore`, each with
  its reason — so a *new* occurrence of the same credential still breaks the build.
- **A credential in the repository's history is recorded and flagged for rotation.** The first
  history scan the gate ever completed found a real connection string — user, private-network host
  and a chosen password — in an EF-scaffolded `SRDbContext.cs` committed in 2023 and deleted since.
  The audit's manual sweep had missed it because it searched tracked files only, and the file was no
  longer one. Written up in [docs/security/FINDINGS.md](docs/security/FINDINGS.md), which previously
  claimed the history was clean.



## [2.17.1] - 2026-08-28

This release includes new features and improvements.

### Added

### Changed

- **The container images assert that their configuration management agent is OpenVox.** The base
  image (`ffquintella/docker-puppet`, Rocky Linux 9) has installed `openvox-agent` from
  `yum.voxpupuli.org` rather than Perforce's `puppet-agent` since its 8.24 bump, but nothing in this
  repository said so or checked it — and because OpenVox keeps the `puppet` command and the
  `/opt/puppetlabs` layout, a base image that changed back would look identical from here. Each of
  the four Dockerfiles now fails the build if `openvox-agent` is absent or if `puppet-agent` is
  installed at all. The entrypoints and every manifest under `build/puppet` are unchanged: the
  package ships no `openvox` binary, and `/opt/puppetlabs/bin/puppet apply` is the supported path.

### Fixed

- **The console container's entrypoint no longer swallows a command passed to it.** `_main` ran the
  keepalive (`tail -f /dev/null`, which never returns) and *then* `exec "$@"`, so the `exec` was
  unreachable and `docker run <image> netrisk-console database init` printed nothing and hung
  forever. The keepalive is the deployed behaviour and stays that way — the image declares no `CMD`
  and the generated host launcher passes no command, so operators drive the container with
  `docker exec` — but the entrypoint now execs the command when one is given instead of leaving dead
  code that reads as though it does. Both paths are pinned by
  `Packaging.Tests/ConsoleEntrypointCommandModeTest`, which runs the shipped `_main`.
- **The API, WebSite and Console containers no longer restart in a loop on 2.17.0.** Two independent
  regressions came in with the NR-2026-025 credential move. The Docker entrypoints read
  `/netrisk/netrisk.env` with `.`, but that file is a literal `KEY=VALUE` environment file and the
  value is a connection string full of `;` — a shell command separator — so
  `Database__ConnectionString` was set to `server=<host>` and `port=`, `uid=`, `pwd=`, `database=`
  became five unrelated variables. With no port left in the connection string MySqlConnector used its
  default 3306 instead of the configured port, the API's database self test timed out after 15 s and
  the host exited on every start. The entrypoints now export each line's raw value without letting
  the shell parse it, so a password containing `;`, `$`, quotes, backticks or spaces survives intact.
  Separately, `console.pp` and `website.pp` still passed `db_port` to `appsettings.json` templates
  that no longer declare it, which failed catalog compilation and killed both containers before their
  application started.
- **A Puppet module edit now actually reaches the container images.** The four Docker packaging
  targets staged `workdir/puppet-modules` only when that directory did not already exist, so a
  workdir left over from an earlier build kept its old copy and any manifest or template change
  silently never shipped. The tree is now restaged on every build.

## [2.17.0] - 2026-08-26

This release includes new features and improvements.

### Added

- **Track 8 — risk governance, approval workflows and a business review portal.** This track closes the gap between NetRisk's risk lifecycle and what an ISO 27001 / SOC 2 / DORA auditor actually samples. **Accepting a risk is now an artifact rather than a status**: a `risk_acceptances` record naming the authorizing manager, the business justification, the compensating controls, a snapshot of the residual score at the moment of the decision, and a mandatory expiry — with renew and revoke, severity-band authority checks, and a daily job that warns at T-30 and T-7 and reopens the risk when it lapses. **Risks carry an inherent and a residual score**, the residual derived from mitigation percentage and validated controls through a swappable strategy that composes multiple controls as `1 − Π(1 − pᵢ)` rather than summing them, so two 60% controls buy 84% and not 120%. **Approvals are enforced server-side rather than by convention**: a status state machine that refuses `Closed` without a review and `Mitigation Planned` without a mitigation; segregation of duties so a reviewer or acceptor cannot be the submitter, owner or manager — administrators included, with a break-glass path that demands a written reason and exports it in the evidence pack; and a `risk_appetites` model, global or per entity, with a dual-approval threshold and a hard acceptance ceiling. **"Who changed what, when" is a query**: an EF `SaveChanges` interceptor writes one `audit_logs` row per changed field across the governance aggregate, attributable end to end including a system actor for background jobs, with a retention policy that is applied rather than merely documented.
- **A business risk acceptance portal (`src/RiskPortal`).** A new mobile-friendly ASP.NET Core application where the people who own a business entity — not the security team — periodically review, rank and decide their own risks. Reviewers are appointed per entity, hold a dedicated `business_risk_review` permission and see only their entity's risks. Campaigns are generated automatically each quarter (per-entity override available) on calendar-aligned periods with a unique `(entity, period)` index, so the job is idempotent by construction. A reviewer drags their risks into business-priority order — or types the numbers, if JavaScript is off — and for each one **accepts** it (creating a formal, expiring acceptance, refused if it breaches the entity's appetite), **requests mitigation** (creating treatment tasks with an owner and a due date), or **escalates** it to a named senior approver. Every decision writes a `MgmtReview`, so the desktop and the portal share one approval timeline rather than keeping two. It consumes the REST API only; the DB-decoupled `WebSite` is untouched, and `CompileRiskPortal`/`PackageRiskPortal` build it.
- **Review cadence is pushed, not pulled.** A daily job walks the register against the existing review-level cadence and notifies through the Track 4.1 channels; a risk that has never been reviewed becomes overdue one cadence interval after submission rather than immediately, so the first notification is not the entire register. The `next_review_date_uses` setting now genuinely selects whether that cadence keys off the inherent or the residual score. Treatment work is tracked as `mitigation_tasks` line items with owners and due dates, feeding the same notifications. And the assessment intake pipeline works: a `PendingRisk` can be promoted into a real risk or dismissed with a reason — previously nothing promoted them, so they accumulated forever.
- **An auditor evidence pack, per entity and period.** One export carrying the acceptances in force, the management reviews and their counter-signatures, the business review decisions, and the field-level change trail underneath — as CSV for a spreadsheet or as a PDF through the 2.1 reporting engine, which stores it as a report so the export is itself a record. An acceptance granted last year and still in force is in it; a campaign nobody decided is in it as undecided items rather than being quietly dropped; a change list cut short by the row limit says so. The CSV neutralises leading `=`, `+`, `-` and `@`, because an evidence pack is precisely a file that gets emailed and opened in a spreadsheet.
- **Quantitative scoring, as an option rather than a replacement.** Every likelihood and impact level now carries a written definition and a numeric range — "1% – 5% a year", "R$100.000 – R$1.000.000" — shown at rating time under the choice, because a five-point scale labelled only Low/Medium/High is read differently by different raters and cannot be aggregated. Alongside it, a FAIR-lite scoring method: calibrated frequency and magnitude ranges, a Monte Carlo engine in `Tools` (PERT magnitude, Poisson event counts, seeded and reproducible), annualized-loss percentiles, a loss-exceedance curve, before/after-mitigation comparison, and a mapping into the existing risk bands by monetary threshold.
- **Schema versions 80, 81 and 82 (upgrade phases 11, 12 and 13).** Governance core, review portal, and the tables the deferred security findings needed. Every statement is guarded and every Data script is a real transaction, so each is safe to apply twice; all three are applied against a real MariaDB in `DAL.IntegrationTests`, from version 79, in order.

- **Track 7 — a security audit of every tier, and the machinery to keep it honest.** The full review of the request flow (GUIClient → ClientServices → API → ServerServices → DAL, plus BackgroundJobs, WebSite, plugin loading and file imports) produced a **34-finding register** under [docs/security/](docs/security/), triaged into milestones and cross-referenced from the code that fixes each one. Twenty-five are fixed with regression tests that fail on the pre-fix code; five stay open with a named owner, a proposed fix and a stated reason; four are risk-accepted with an expiry, dogfooding the product's own risk-acceptance discipline. The register records **how each finding was established**, because this repository has twice shipped a control that was documented as working and was not — and the audit found the same pattern a third time, in a class whose own doc comment claimed its endpoints were authenticated while it carried no `[Authorize]` attribute at all. Alongside it: a STRIDE [threat model](docs/security/THREAT_MODEL.md) over six named trust boundaries, an [ASVS Level 2 checklist](docs/security/ASVS_L2_CHECKLIST.md) where every ✅ names a file or a test, a [supply-chain policy](docs/security/SUPPLY_CHAIN.md), a [secrets inventory with rotation procedures](docs/security/SECRETS.md), a [data-protection posture](docs/security/DATA_PROTECTION.md), an [internal triage SLA](docs/security/TRIAGE_SLA.md) using the same numbers NetRisk ships as its product's remediation defaults, a [burn-down](docs/security/BURN_DOWN.md), and a [baseline report](docs/security/baseline-2026-08-26.md) that separates what was measured from what was asserted.
- **Continuous security gates in CI.** [`.github/workflows/security.yml`](.github/workflows/security.yml) runs CodeQL (C#), gitleaks over the **full** history, a known-vulnerable-dependency scan and a submodule-provenance check, on push, on pull request and weekly — the weekly run matters because an advisory published against an already-pinned version appears in no diff. Every gate fails on something *new* rather than on the backlog, which is the only way a gate survives contact with a real backlog. Dependabot watches NuGet (solution and build), GitHub Actions and all five git submodules. The dependency gate is a committed script, so a developer can run exactly what CI runs; accepted findings live in [`security/dependency-suppressions.yml`](security/dependency-suppressions.yml), where every entry needs an advisory id, an owner, a real reason and an **expiry at most 180 days out** — an expired suppression fails the build, which is what stops the file becoming a list nobody revisits. The baseline scan found no vulnerable package across all 33 projects, so the file ships empty.
- **A CycloneDX SBOM beside every artifact.** `GenerateSbom` emits `netrisk-<component>-<version>.cdx.json` plus a `.sha256` into each packaged component's directory, generated at build time from the *resolved* dependency graph rather than hand-maintained — a hand-maintained list records what somebody believed was shipping. It is `TriggeredBy` the `Package*` targets, so adding a component means one entry in `Sbom.Components`. Like the signing targets, a missing tool warns and still produces the artifact; only `--require-sbom` turns the gap into a failure, and the build reports the install command rather than installing anything itself.
- **A submodule-provenance gate.** A submodule bump is a one-line diff that can pull in any amount of code, which makes it the highest-leverage, lowest-visibility change anyone can make to this repository. A pull request that moves a `libs/` pointer must now name the submodule and the commit range in its description; [docs/security/SUPPLY_CHAIN.md](docs/security/SUPPLY_CHAIN.md) sets out the review procedure, which submodules sit on a security surface (`NessusParser` parses untrusted scan files, `netrisk-plugin-sdk` defines what a plugin may do, `reliable-rest-client-wrapper` carries every outbound client call) and when to vendor rather than track.
- **Progressive login throttling and rate limiting on the credential endpoints.** Four free failures per identity, then a lockout doubling from five seconds to a fifteen-minute cap, decaying after thirty minutes of quiet, keyed on **both** the account and the source address — account-only lets an attacker lock a colleague out on purpose, address-only lets a distributed attempt straight through. A per-source request budget sits in front of it for a different reason: bcrypt at work factor 15 is deliberately expensive, so a few hundred concurrent *refused* attempts are a problem on their own.
- **Security response headers on the API and the WebSite.** HSTS, `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `X-Permitted-Cross-Domain-Policies`, `Cross-Origin-Resource-Policy` and a Content-Security-Policy — `default-src 'none'` on the API, whose responses are data, and a page policy on the WebSite that keeps `script-src 'self'` with no inline allowance. `Security:Headers:HstsMaxAgeSeconds` is configurable and `0` genuinely disables HSTS, which is the right setting while an installation is still on a self-signed certificate: pinning a browser to HTTPS-only for a host whose certificate does not validate cannot be undone from the server side.
- **Hosts read configuration from environment variables.** `Database__ConnectionString`, `https__certificate__password` and every other key can now be supplied from the environment or a secret store, which is what makes the "no credentials in `appsettings.json`" rule achievable rather than aspirational. Precedence is now file → user-secrets (Debug) → environment; it was previously inverted, so the committed `appsettings.json` silently overrode anything a developer set in user-secrets.

- **Track 5.1 — automated code-signing pipelines.** The Nuke build now signs what it ships. On Windows, Azure Trusted Signing (through the `sign` CLI) is the first-class provider with `signtool` as the generic fallback — an installed certificate by thumbprint, a vendor CSP/key-container pair for a cloud HSM, or a PFX for internal builds — always SHA-256, always RFC 3161 timestamped, and always gated by `signtool verify /pa /all` before an artifact may be published. Timestamping walks an ordered fallback list (`timestamp.acs.microsoft.com`, DigiCert, Sectigo) because a single timestamp authority outage is the classic flaky release build. On macOS, `PackageMacGUI`/`PackageMacA64GUI` run the whole ordered pipeline — hardened-runtime `codesign` of every nested Mach-O and then the bundle with an entitlements file, `notarytool submit --wait`, `stapler staple`, `spctl --assess` — over the `.app`, the `.pkg` (via `productsign`) and the `.dmg`; a notarization rejection fails the build and prints Apple's own log, which is the only place that names the offending binary. **No credential is ever read from the repository and no ordinary build needs one:** with signing material absent the targets emit one warning line and produce the unsigned artifact, which is the normal outcome for a developer and for a CI fork, while `--require-signing`/`--require-notarization` turn a missing credential into a failure. Secrets arrive as `[Secret]` parameters or `NETRISK_*` environment variables and every command line carrying one is redacted before it reaches a log. Only three macOS entitlements are granted; a test fails if `disable-library-validation` or any other hardened-runtime weakening is added.
- **Track 5.2 — modern native installers.** Windows gains `PackageWindowsMSI` (WiX v5, per-machine, no-UI so `msiexec /i … /qn` is the supported path, upgrade table wired so version N+1 replaces N, publish directory harvested rather than file-listed, and public `INSTALLFOLDER`, `SERVERURL` and `INSTALLDESKTOPSHORTCUT` properties for GPO/Intune rollouts) and `PackageWindowsMSIX` (rendered `AppxManifest.xml`, `makeappx pack`, signed, plus a published `.appinstaller` giving installed clients built-in auto-update with no updater inside the app). macOS `.dmg` assembly became genuinely drag-and-drop — the app bundle, an `/Applications` symlink, a branded background and a volume icon — on a pure `hdiutil` path that needs no Finder or AppleScript and so works on a headless runner, with `--branded-dmg` opting into `create-dmg` for window geometry. Linux gains `PackageLinuxFlatpak` (org.freedesktop.Platform 24.08) and `PackageLinuxSnap` (core24, strict confinement), both fed by the same self-contained publish and both with their sandbox permissions enumerated deliberately: rendering, GPU, network, the XDG download directory, and the tray and Secret Service bus names — and explicitly *not* `--device=all`, `--filesystem=home` or a raw D-Bus socket. Shared AppStream metadata and a freedesktop desktop entry are validated with `appstreamcli` when it is installed, and a validation failure fails the build because Flathub rejects invalid metadata. Every installer identifier — the MSI upgrade code, the MSIX identity, the macOS bundle id, the Flatpak app-id, the Snap name — is declared once in `PackageIdentity` instead of being restated per target.
- **Administrators can pre-seed client settings with a `netrisk.ini` overlay.** The desktop client now layers an optional INI file, read last, on top of `appsettings.json`, so `[Server] Url=https://netrisk.example.com:5443/` next to the executable overrides the shipped default. It is what the MSI writes from its `SERVERURL` property — through the MSI `IniFile` table, so no custom action is involved and the file is removed again on uninstall — and it works identically for the macOS, Flatpak and Snap deployments. This is what makes enterprise server pre-configuration real rather than a property the app never reads.
- **A release-engineering guide for whoever cuts a build.** [docs/packaging/release-engineering.md](docs/packaging/release-engineering.md) covers every artifact and what signs it, the full parameter/environment table with the secrets marked, Azure Trusted Signing and signtool setup, certificate rotation (including the MSIX subject-DN trap that turns a rotation into a migration), Apple Developer ID and App Store Connect API key setup, CI keychain import, the enumerated Flatpak/Snap sandbox grants with the two features a strict sandbox limits, what each platform's runner must have installed, and a troubleshooting table.

- **Track 4.1 — unified notification channels.** NetRisk now broadcasts domain events to Email, Slack, Microsoft Teams and generic webhooks through one extensible `INotificationChannel` contract. Each provider renders natively: Slack gets Block Kit inside a severity-coloured attachment, Teams gets an Adaptive Card 1.4 posted to a Workflows webhook (explicitly *not* the retired O365 `MessageCard`, which Microsoft has withdrawn), email gets inline-styled HTML with a plaintext alternative, and the generic webhook gets a documented, versioned JSON body signed with HMAC-SHA256 in `X-NetRisk-Signature` over `"{timestamp}.{body}"` — the timestamp is inside the signed string, so a captured request cannot be replayed indefinitely. Administrators configure an events × channels matrix (ten events, per-row minimum severity and entity filters, optional digest window) under Administration → Integrations, with a "send test message" button per channel that performs a real send rather than a reachability probe. The dispatcher owns retry (three attempts, 1/4/9-minute backoff, and a 400 or 403 is *not* retried because it is a configuration error), an ordered fallback chain that engages only once the primary is out of attempts, and digest windows that collapse an import of three thousand findings into one message. Every attempt is a row in a delivery log with its status, attempt count and last error — credentials redacted — because "the SLA breach fired, did the team hear about it?" cannot be answered from the absence of a Slack message.
- **Track 4.2 — bi-directional issue sync.** Findings can be filed as developer tasks in **Jira Cloud**, **GitHub Issues**, **GitLab Issues** and **Azure DevOps Work Items**, singly or from a multi-selection, with a preview of the rendered title and description before anything is created — or linked to an issue that already exists, by key or URL. Each connection carries its own field mapping: severity → tracker priority, `{{Placeholder}}` templates, issue type and default labels. Closing a linked ticket transitions the NetRisk finding per a per-connection status-mapping table whose actions are `MarkMitigated`, `ScheduleReverify`, `MarkFalsePositive`, `Reactivate` or `None` — the mapping names an *action* rather than a destination status, because closing a ticket does not always mean the finding is fixed. Inbound changes arrive by validated webhook (`X-Hub-Signature-256` for GitHub, `X-Gitlab-Token` for GitLab, a URL secret for Jira and Azure DevOps, which cannot sign a body) with a per-connection polling fallback for instances that cannot reach NetRisk. Loop protection stops an inbound change echoing back out as a comment the tracker then reports as a change; a conflict between a NetRisk decision and a tracker one applies last-writer-wins *and* flags the link for a review queue.
- **Track 4.3 — hardened enterprise authentication.** OIDC (authorization code with PKCE, discovery-document configuration) and SAML 2.0 in the service-provider role, several identity providers storable at once, per-IdP claim and group mapping, and JIT provisioning off by default. The desktop flow is the standard native-app pattern: the client opens the system browser and the API completes the dance through a loopback redirect, with the PKCE verifier held server-side and the state single-use. SAML validation enforces metadata-sourced signing certificates, signature-wrapping rejection, audience and `InResponseTo` checks, bounded clock skew and prohibited DTD processing. SCIM 2.0 provisioning at `/scim/v2/Users` and `/scim/v2/Groups` implements RFC 7644 PATCH semantics — including the path-less `replace` Entra ID sends — and `active:false` disables login and revokes live sessions on the next request rather than at the next token expiry. Provisioning tokens are per-connection, hashed, shown once and revocable, and every SCIM request is audited. WebAuthn/FIDO2 registration and authentication ceremonies (fido2-net-lib) support several named authenticators per user, signature-counter clone detection, a configurable attestation policy, a "require a hardware factor for administrative accounts" switch, and admin-issued single-use recovery codes.
- **Track 4.4 — Trend Micro Vision One integration.** Region-aware connection management for all seven Vision One regions (a key issued in one region is rejected by the others, so the API root is derived from a picker rather than typed), with a test-connection utility that reads the ASRM endpoint the sync actually uses — proving the key carries the ASRM permission, which a `/whoami` probe would not. A daily job syncs the attack-surface device inventory onto NetRisk hosts, matching existing assets by external id, MAC, FQDN, hostname and finally IP, and filling only empty fields so a hostname a person typed is not overwritten nightly by one an agent guessed. Per-device CVEs are ingested through the shared finding pipeline, so they get the same deduplication, sticky triage and SLA due dates as a scanner import; a Trend Micro virtual patch records its IPS rule in the finding's evidence and optionally closes the finding, off by default because a virtual patch is a compensating control and not a fix. Device risk scores land on the host and roll into a criticality-weighted entity-wide Cyber Risk Index, with optional write-back of criticality and acceptance-derived exemptions.
- **Track 4.5 — SecurityScorecard integration.** Domain-targeted connections authenticating with `Authorization: Token` (not `Bearer`, which SecurityScorecard rejects outright) and a test-connection utility that proves both the token and the entitlement to that domain. A daily job records the overall score and grade plus the ten risk factors as an append-only history for trend charting, and inverts the 0–100 "higher is better" score into the Cyber Risk Index, where higher is worse. Domain CVEs and active issues — missing SPF, expiring certificates, exposed ports — are ingested as findings under the `SecurityScorecard_Vulnerability` and `SecurityScorecard_Issue` categories, attached to a synthetic domain-asset host so that findings rated against a domain rather than a machine are visible in an asset-oriented register.
- **Schema version 79 (upgrade phase 10).** Fifteen new tables — notification channels, subscriptions and deliveries; issue-tracker connections, status mappings and finding↔issue links; identity providers, SCIM tokens and request audit; WebAuthn credentials and MFA recovery codes; Vision One and SecurityScorecard connections with factor history; and a shared integration sync log — plus seven posture columns on `hosts` and four on `entities`. Purely additive: nothing is dropped, renamed or retyped, and an installation that configures no integration carries fifteen empty tables and behaves exactly as before. All of it is born Track 6 compliant.
- **Integration credentials are encrypted at rest.** Webhook URLs, signing secrets, issue-tracker tokens, OIDC client secrets, Vision One API keys and SecurityScorecard tokens are stored as ciphertext under a key derived from the installation's server secret, and no endpoint returns them — reads carry a has-a-credential flag or a redaction placeholder, and a write that sends the placeholder back keeps the stored value. A value encrypted on another installation fails with a message that names the remedy rather than silently authenticating as an empty string.

### Changed

- **Shipped defaults are now the safe ones.** SAML is **off** by default (it was on, pointing at a public test identity provider), `OmitAssertionSignatureCheck` is `false` in both the shipped configuration and the Puppet template, the Puppet template's SAML digest and signature algorithms moved from SHA-1 to SHA-256, and the JWT lifetime dropped from 1440 minutes to 60 with a 1440-minute ceiling enforced — a longer configured value is clamped and logged, because it is a mistake rather than a policy. New `Security:` sections on both hosts cover the TLS floor, the header policy and the credential rate limit.
- **A Release build refuses to start with the development certificate.** The shipped `appsettings.json` pointed at a `.pfx` whose private key is committed to this repository, with the password `"pass"`. A Debug build still uses it — that is what it is for — but a Release binary now refuses, rather than warning. Warning was rejected deliberately: a start-up warning is read once and then lives in a log nobody tails, and the whole point is that the insecure configuration was the one an installation got by changing nothing.
- **TLS 1.2 is allowed alongside 1.3 rather than 1.3 alone.** The API listener previously pinned TLS 1.3 only, which is stricter than the 1.2 minimum this track set out to enforce — and which silently refuses clients on older platform TLS stacks, where the observed operator workaround is to turn HTTPS off altogether. Both hosts now accept 1.2 and 1.3 and nothing older; `Security:Tls:MinimumVersion=Tls13` pins 1.3 only for an installation that controls its clients. A live scan during the audit found .NET on macOS does not offer 1.3 in the server role at all, so the 1.3-only listener would have served nothing there.
- **Integration credentials are re-encrypted with AES-256-GCM on save.** The `enc:v2:` format has a fresh salt and nonce per value and a 128-bit authentication tag; `enc:v1:` values are still read and are upgraded in place — but only after a round-trip check, because the old format is unauthenticated and decrypting with the wrong key returns plausible garbage rather than failing. A value that does not decrypt here is left byte-identical, so a credential encrypted on another installation stays recoverable there.
- **Windows and Linux packaging now share one compiled output.** The publish step moved out of `PackageWindowsGUI`/`PackageLinuxGUI` into new `PublishWindowsGui`/`PublishLinuxGui` targets, so the Inno Setup installer, the MSI and the MSIX are all cut from the same signed binaries — and the Flatpak and the Snap from the same self-contained publish — instead of each target publishing its own. `PackageWindowsInstallers`, `PackageLinuxInstallers` and `PackageAllInstallers` build the whole set; `VerifySignatures` re-checks whatever is already in `output/publish`. Existing target names and artifact names are unchanged.
- **The macOS `.dmg` now contains the app instead of a `.pkg`.** It previously wrapped the installer package, which meant a user who mounted it still had to run an installer. The `.pkg` is still produced as a separate artifact for managed deployments.
- **The macOS bundle carries a real icon and a camera purpose string.** `Info.plist` moved to a reviewed template and gained `CFBundleIconFile`, `LSApplicationCategoryType`, `NSHighResolutionCapable`, a copyright line and `NSCameraUsageDescription` — without that last key the hardened runtime kills a signed build the moment FaceID touches the camera, instead of prompting.

- **Every user-facing label in the GUI is now translatable.** `./build.sh LintUi` reported thirteen R5 violations — text typed straight into a view, which no locale can reach: *Status* (host editor), *Ctrl #:* (risk editor and risk detail), *Id:* / *HostName:* / *Mac Address:* (host detail), *Dt:* (both incident-response-plan windows), *Dt.* (risk detail), *Type* / *Content* / *Columns (comma-separated, blank = all)* (report template editor) and *Email* (user info). All thirteen now come from `Localization*.resx` through a `Str*` property, with five new keys (`Id`, `HostName`, `MacAddress`, `CtrlNumber`, `ReportColumnsHintMSG`) added in all three resource files. R5 is at zero and `GUIClient.Tests` now fails if it stops being.
- **The *Available* / *Selected* headers on every multi-select picker are localized.** `AvaloniaExtraControls.MultiSelect` is a generic control library with no access to NetRisk's localizer, so it defaults those two headers to English literals and expects the consumer to supply them; only the entity form and the incident editor did. `StrAvailable`/`StrSelected` now sit on `ViewModelBase` next to `StrSave`/`StrCancel`, and the vulnerability editor and the two pickers in the user administration view bind them — so the risk, permission and team pickers read *Disponíveis* / *Selecionados* in pt-BR instead of *Available* / *Selected*.
- **The risk register list shows what treatment bought.** Each row now carries `8.0 → 2.0 (−6.0)` under its subject, plus the rank the business reviewers gave it, from a single bulk score call per refresh rather than one request per row. The sign is the change in the score, so a treatment that reduced the risk reads as negative and one that made it worse reads as positive — which is the row worth looking at.
- **The Detailed Entities Risks report gains a pre/post-treatment table** per entity, ordered by smallest reduction first, with untreated risks shown as a dash rather than dropped: a risk missing from a pre/post table reads as one that has been treated.
- The finding lifecycle, risk creation, incident creation, IRP task assignment and scan-import completion now raise notification events. The publishers swallow their own failures, so a broken Slack webhook can never turn a successful triage decision into an error.

### Fixed

#### Security

Each item states the **exposure**, not just the change. Full detail, including how each was established and which test pins it, is in [docs/security/FINDINGS.md](docs/security/FINDINGS.md).

- **The five findings Track 7 left open are closed (NR-2026-008b, 017, 025, 028, 032).** Lockout counters are persisted to a `login_attempts` table keyed on the account *and* the source, so a deployment behind a load balancer shares one budget instead of handing out one per instance. Attachments have per-file access control: `nr_files` carries an entity and is covered by the tenancy query filter, and a new authorizer resolves whichever parent a file hangs off — risk, mitigation, incident, response plan, plan execution or acceptance — and applies that record's permission rules, on both the by-name and the enumerable by-id route. The Puppet module writes the database credential to a `0600` environment file owned by the service account instead of rendering it into `appsettings.json`, and no longer even passes the password to the template. Signing out revokes *that* session by its token id rather than only on password change, and a companion endpoint lets a client confirm it took effect. FaceID biometric templates and signature seeds are encrypted at column level with AES-GCM — not because they are easy to steal, but because a leaked password is rotated in a minute and a leaked face is not rotated at all; rows written before the change are read as-is and protected on their next write, so nobody has to re-enrol.
- **A plugin's publisher is verified before it is loaded (NR-2026-027, still risk-accepted).** This does not confine anything and is not presented as a fix: a loaded plugin still runs with the API's full authority, and .NET offers no in-process sandbox. What changed is the trust decision — a detached `.sig`/`.cer` pair (portable, works on Linux, needs no OS trust store) or an Authenticode signature is checked against an optional publisher allowlist, and the publisher is logged on every load. "Any DLL in the plugins directory" becomes "a DLL from a publisher this installation named". Report-only by default, because defaulting to refusal would break every existing installation with a plugin on upgrade, which is how a security default gets switched off permanently.
- **A single click on a link could hand an attacker anyone's session (NR-2026-001, critical).** The desktop single-sign-on flow created a pending sign-in under whatever request id the *caller* chose, on an anonymous endpoint; the browser step marked it accepted the moment a valid SAML identity appeared, with no consent; and a second anonymous endpoint returned a full session token for that identity to anybody who asked, repeatedly. So no guessing was involved: an attacker picked an id, sent a colleague the link, the colleague's existing SSO session completed the flow silently, and the attacker collected their session — any account, including an administrator's, on an installation where SAML was enabled by default. The flow now mints the id server-side for an administrator-approved client only, refuses any id the server did not mint, requires the person in the browser to **approve explicitly on a page naming the machine that asked** (with a single-use anti-forgery token, because the SAML cookie must be `SameSite=None`), and hands the token only to the client registration that minted the request, once.
- **Password-reset links and file keys were predictable (NR-2026-002, critical).** One shared non-cryptographic generator produced the JWT signing key, password-reset link keys, file and report access keys, generated passwords and the SAML request id. Several of those values are given to the requester by design — a reset link arrives by e-mail, a file key comes back in the upload response — so an attacker who requested a few for their own account could recover the generator's state and predict *other people's* reset keys. Everything now draws from the platform CSPRNG, including the FaceID liveness challenge, whose predictability undermined its own replay protection.
- **The shipped configuration served TLS with a private key published in this repository (NR-2026-003, critical).** `appsettings.json` named a committed, self-signed, expired `.pfx` with the password `"pass"`, in the file that becomes the deployment template — so an installation that changed nothing had no transport security at all against anyone who had read the source. See "A Release build refuses to start with the development certificate" above. **If any installation ever served with one of those certificates, treat it as compromised, reissue it, and rotate anything that travelled over a session it protected.**
- **The desktop client accepted any server certificate (NR-2026-004, NR-2026-005, high).** Every call went through an unconditional "accept everything" callback — carrying its own `//TODO: Remove this line` — and so did the first-run check that decides which server the client trusts from then on. Anything able to answer on the configured host and port could read and rewrite the whole session, including the password in the sign-in header. Validation is now on by default; the bypass survives only as an explicit, per-installation, loudly-logged opt-in, and a certificate failure is now reported as a certificate failure instead of "Please enter a valid URL".
- **Any authenticated user could write files anywhere the API could reach (NR-2026-006, high).** The chunked-upload endpoints passed a caller-supplied file id straight to `Path.Combine`, which is not a containment primitive, and then created directories and wrote to the result. Ids are now validated against a character allowlist *and* checked to resolve inside the upload directory.
- **Disabled users could still sign in (NR-2026-007, high).** Basic authentication checked the lockout flag but not the `enabled` flag — the one the administrator UI and SCIM deprovisioning set. The JWT path already refused them, which is what made the asymmetry easy to miss. A deactivated account retained full access through the sign-in path the desktop client uses.
- **Passwords could be guessed as fast as the server would answer (NR-2026-008, high).** Nothing counted a failed login: the `failed_login_attempts` column had no logic behind it and the lockout flag was only ever set by hand. See "Progressive login throttling" above. The counters were per process, so a multi-instance deployment got the budget per instance — recorded as NR-2026-008b and closed in Track 8 by persisting them.
- **SAML assertions were accepted without checking the identity provider's signature (NR-2026-010, high).** `OmitAssertionSignatureCheck` was `true` in the shipped configuration *and* in the production Puppet template, with SAML enabled by default. An assertion is then just XML: forge one naming any user and the API accepts it.
- **Stored integration credentials leaked which ones were equal, and could be tampered with undetectably (NR-2026-011, high).** They were encrypted with AES-CBC under a key of `SHA256(passphrase)` and an IV of `MD5(passphrase)` — both constant per installation, so identical secrets produced identical ciphertext, and CBC without authentication cannot tell a tampered value from a valid one. See the AES-GCM entry above.
- **WebAuthn enrolment endpoints carried no authorization attribute (NR-2026-009, medium).** The class's own doc comment said "The registration endpoints are authenticated"; there was no `[Authorize]` anywhere on it. They failed closed only incidentally, because the base controller throws without a principal — a 500 rather than a 401, and one refactor away from being an open enrolment endpoint. A reflective test now fails if *any* action ships without authorization or a justified place on an anonymous allowlist.
- **Session tokens were valid for a day, could not be revoked, and were validated only for their signature (NR-2026-012, medium).** Issuer, audience and algorithm are now pinned on both the minting and validating side, the default lifetime is 60 minutes, and a password change — precisely the reaction to a suspected compromise — now invalidates every token issued before it, using a timestamp column that already existed. Per-session logout still needs per-token state (NR-2026-028).
- **An administrator could point an integration at the cloud metadata service and read the response (NR-2026-013, medium).** Outbound integration URLs had no destination policy, and the response body comes back to the caller — so the target was not the internet but `169.254.169.254`, whose reply on a default instance is a set of cloud credentials. Link-local and the metadata addresses are now always refused; private ranges stay allowed, because an on-premise Jira is the normal case for this product, and can be refused with `Integrations:BlockPrivateNetworks`. Redirects are not followed, and every resolved address is checked rather than the hostname.
- **A malformed authentication header produced a 500, and passwords containing a colon were truncated (NR-2026-018, medium).** Neither the base64 decode nor the credential split was guarded, so an unauthenticated caller could pick between "401" and "server error" by malforming a header; and splitting on every colon silently discarded everything after the first one in a password.
- **Webhook secrets for the unsigned issue trackers were compared character by character (NR-2026-019, medium).** Jira and Azure DevOps cannot sign a body, so a shared URL secret is the whole authentication — and `!=` returns as soon as two characters differ. The signed providers were already comparing in constant time; these now do too.
- **Uploaded scan reports were staged in a world-writable directory (NR-2026-020, medium).** `/tmp/netrisk-api`, under predictable names, holding the most sensitive data in the product. Staging moved to an application-owned directory with `0700`; if it cannot be created the service falls back and *says* the fallback is world-writable.
- **The SAML session cookie could be sent over plain HTTP (NR-2026-016, medium).** `SecurePolicy` was "same as request". Now always `Secure`.
- **A scan-report URL could launch an arbitrary application on the analyst's workstation (NR-2026-023, medium).** The URL comes from an imported scan file, and on macOS the launcher was `Process.Start("open", "-u " + url)` — one string the operating system re-splits, so a URL containing a space smuggled `-a SomeApplication` past it. On Windows an arbitrary shell-executed target launches a local path as readily as it opens a link. Links are now validated as absolute `http`/`https` with no whitespace, and arguments are passed as a list rather than a re-parsed string.
- **Two smaller ones with no current path, fixed because the premise could change:** three `information_schema` queries interpolated the schema name instead of parameterising it (NR-2026-021), and the legacy Nessus parse path allowed DTD processing (NR-2026-022) — unreachable today, since nothing calls the factory that reaches it, but "unreachable today" is not a property that stays true on its own. The three *live* importers were verified to prohibit DTDs by a test that asserts the refusal, rather than by reading the comment beside the setting.
- **Password-reset links are indexed by SHA-256 instead of MD5 (NR-2026-014, medium),** on both the API and the WebSite — those rows are pushed to the website verbatim, so a digest change on one side alone would have made every reset link look expired. And the website sync now says out loud when it is running with certificate validation disabled (NR-2026-026).

Six of the fixes above were themselves wrong on the first attempt and were corrected before this shipped — including a Content-Security-Policy that would have forbidden the very consent form the single-sign-on fix depends on, and a lockout keyed on a source address that behind a reverse proxy is shared by an entire organisation. Both would have been worse than the vulnerability they fixed. They are listed, with what caught each one, in [docs/security/FINDINGS.md](docs/security/FINDINGS.md) § "Regressions introduced by this track's own fixes", because the pattern is worth recording: a control tested at the level it was written at looks correct, and the break appears one layer up.

#### Other

- **A rejected write was reported to the desktop as a network failure.** RestSharp's verb extensions (`PostAsync`, `PutAsync`) throw on any non-2xx, so `FindingsAdminRestService`'s branch that reads a structured error body out of a 400 or 422 was unreachable: the user saw "error communicating with the server" for a request the server had understood and deliberately refused. The client now uses `ExecuteAsync` and distinguishes the two cases by whether a status code came back at all — RestSharp populates `ErrorException` and sets `ResponseStatus` to `Error` for a refusal as well as a transport failure, so only `StatusCode == 0` means "nothing answered". The same shape is used by the new governance client, where the refusals — a forbidden transition, a breach of appetite, a segregation-of-duties violation — are the whole point of the response.
- **The Impact-vs-Probability report sent its minimum score twice and its maximum never.** `StatisticsRestService` added the `minRisk` query parameter twice and omitted `maxRisk`, so the server always applied its default upper bound and the report's maximum filter did nothing. The existing test asserted the bug; it now asserts the corrected parameters.
- **`JobManager` prevented the API from starting in Development.** It is registered as a singleton and took a scoped `IAuthenticationService` it never used, which the .NET DI container's scope validation — on in Development, off in Production — rejects at start-up. The unused parameter is gone.

- **`EmailService` accumulated recipients across sends.** `IFluentEmail` is a builder whose address list grows with each `.To()`, and one instance is injected per service — so a second message from the same service instance was also delivered to the first message's recipient. Nothing in the product sent two mails from one instance until the Track 4 email notification channel did, at which point a fallback email would have reached whoever happened to be notified before. The address lists are now cleared before every send. `SendNotificationAsync` also treats an unsuccessful `SendResponse` as a failure: FluentEmail reports a refused message by returning one rather than by throwing, so a caller that only caught exceptions would have recorded a rejected notification as delivered.


## [2.16.3] - 2026-08-25

This release includes new features and improvements.

### Added

### Changed

- **The Vulnerability editor dialog was reorganised and made responsive.** It opened at a fixed 1150×600 and declared no `MinWidth`/`MinHeight`, so `DialogWindowBase`'s sizing contract pinned that opening size as the floor — the window could grow but never shrink, and nothing inside it grew when it did: the Description, Solution and Comments boxes were nailed to `Height="80"`, the risk `MultiSelect` to `MaxHeight="400"`, and every row of the content grid was `Auto`, so extra height became dead space at the bottom while a short screen simply clipped the form. The layout is now the one the UI standard describes (`docs/ui-standard.md` §5.2/§5.3.1/§5.6): a full-width header, a two-pane content row split by a `GridSplitter` (form left, risk association right), and the canonical centered action row. The form's three free-text boxes sit on star rows with a `MinHeight` floor so they absorb the slack, and the left pane is wrapped in a vertical `ScrollViewer` so it scrolls instead of clipping once the floors are reached. Fields are grouped under `header2` section headings — *Details* (title, score, description, solution, comments) and *Classification* (impact, technology, team, computer, analyst, application) — with labels in a shared `Auto` column so they line up, and inputs on `*` columns with `MinWidth` rather than fixed widths. Verified at 900×600 and 1500×1000.
- The score spinner now formats to two decimals. The column is a float, so a stored 7.4 was widening to `7.40000009536743` in the box; the bound value is untouched, and the stepper moves by 0.1 instead of 1.
- The dialog's own case-insensitive *Risk Filter* is now the only filter on the risk panel — `MultiSelect.ShowFilter` is off, so the two empty search boxes that used to sit under *Available*/*Selected* and duplicated it are gone.
- The validation rules that gate Save now state themselves in the window, in a `validationSummary` line above the action row, instead of only in the disabled button's tooltip (closes the IX-4 gap recorded for this dialog in `docs/ux-interaction-standard.md`). Cancel is now `IsCancel`, and the Add-computer button has a tooltip.

### Fixed



## [2.16.2] - 2026-08-25

This release includes new features and improvements.

### Added

### Changed

### Fixed

- **The Vulnerabilities window came up completely blank — no rows, no column headers, not even the toolbar labels or the row count.** `MainWindow.axaml` handed the view its `DataContext` by binding it to a `VulnerabilitiesViewModel` property on the shell, but `MainWindowViewModel` built that view model in a **field initializer**, so it never passed through its `RaiseAndSetIfChanged` setter and never raised `PropertyChanged`. The binding resolved to null while the control was still being constructed — not yet attached to the tree, so DataContext inheritance had no source to offer — and with no change notification ever raised it never re-evaluated. `OnDataContextChanged` therefore hit its `_viewModel is null` guard on every pass, `BuildSource()` never ran, and `TreeDataGrid.Source` was never assigned, which is why the grid showed not even an empty header row. Everything bound went with it: all 43 text blocks measured zero-width (including the localizer constants, which fall back to the resource key and so can never be empty), all seven status-gated toolbar buttons reported themselves *enabled* because their `false` defaults were never overwritten, and Reload did nothing because its command binding was dead too. The view now creates its own view model in its constructor, as `DashboardView`, `RiskView`, `EntitiesView`, `HostsView` and `AssessmentView` already do — after `InitializeComponent()`, since `OnDataContextChanged` builds the grid source and needs the named `TreeDataGrid` to exist by then. The shell's now-unused property is gone: left in place it would have been a second view model, re-subscribing to `AuthenticationSucceeded` and duplicating every load. `MasterDashboardView` and `IncidentsView` keep the shell binding, which works for them precisely because their view models *are* assigned through the setter, on first navigation.
- **Repairing that surfaced a second defect that had been unreachable behind it, and which aborted the whole client on launch.** With `BuildSource()` finally executing, the finding-lifecycle column threw `Expression of type 'DAL.Enums.FindingStatus' cannot be used for return type 'System.Object'` from inside the `MainWindow` constructor, taking the process down with `SIGABRT` before the login window appeared. TreeDataGrid walks a column getter as an expression tree, and its `ExpressionChainVisitor.VisitMethodCall` admits any call whose **return** type is a reference type, then builds a `Func<TModel, object>` over that call's **instance** — so `x => x.LifecycleStatus.ToString()` passed the guard on `string` and then failed to box the enum receiver. The column now hands the enum over raw; `TextCell` renders through `value?.ToString()` anyway, so the displayed text is identical. Two neighbouring columns escape the same trap only by accident and were left alone deliberately: `x.SlaDueDate.Value.ToString("yyyy-MM-dd")` survives because the preceding `== null` test moves the visitor's chain head off the member access, and `x.DaysOverdue(DateTime.UtcNow)` survives because `int?` *is* a value type and so fails the guard that would have built the bad lambda.



## [2.16.1] - 2026-08-25

This release includes new features and improvements.

### Added

### Changed

### Fixed



## [2.16.1] - 2026-08-25

This release includes new features and improvements.

### Added

- **A root `Makefile` as the discoverable entry point for the everyday developer commands.** `make` with no target lists every available target with a one-line description, so the commands documented across CLAUDE.md (Nuke build, `dotnet run`/`test`, the EF migration wrappers) are reachable without first knowing which script or project path to type. `make gui` starts the Avalonia desktop client with the `--environment` flag it needs to boot (`ENV=dev` by default), and there are matching targets for the API, website, background jobs and console client, plus `build`, `test`, `coverage`, `db-update` and `migration-add`. Targets that need an argument fail with a usage line instead of invoking the underlying tool with an empty one. Every target delegates to the existing tooling — nothing about the build is reimplemented here.

### Changed

### Fixed

- **A failed `netrisk-console database update` can now simply be run again.** MariaDB implicitly commits every DDL statement, so the `START TRANSACTION` that wrapped 42 of the upgrade scripts rolled nothing back — when version 77 died part-way, two-thirds of it had already committed while `db_version` still read 76, and the only way forward was hand-written SQL. All 73 non-empty `DB/Structure/{n}.sql` scripts are now guarded statement by statement (`IF NOT EXISTS` / `IF EXISTS` where MariaDB has it; an `information_schema` probe driving `PREPARE`/`EXECUTE` for the 89 renames and primary-key swaps where it does not), and the misleading transaction wrappers are gone, replaced by a note explaining why. The `Data` scripts went the other way: they are pure DML, so all 78 are now wrapped in a real transaction with the `db_version` bump inside it as the genuine commit point — a Data script that fails rolls back whole. Both appliers force `AllowUserVariables` on their connection, since MySqlConnector would otherwise read the guards' `@nr_ddl` as a parameter placeholder and reject the script before the server saw it. Three tests hold the line: a statement-by-statement convention check that needs no database, the apply-order table-reference replay, and an integration test that applies all 78 versions with every Structure script run **twice** and requires the resulting schema — every column, index and foreign key — to match a single clean pass exactly. Two real defects surfaced while proving this: `information_schema` compares identifiers case-insensitively, so guarding the Track 6 case-only renames (`Incidents` → `incidents`, `OS` → `os`) silently skipped them until the probes were made `BINARY`; and MariaDB evaluates a sibling `ADD`'s `IF NOT EXISTS` against the table as it was *before* the statement, so guarding the `ADD INDEX` in `reports`' `DROP INDEX idx_name, ADD INDEX idx_name` dropped the index and never restored it.
- **`netrisk-console database update` no longer aborts halfway to 78 with `Table 'netrisk.files' doesn't exist`.** The Track 3 upgrade script `DB/Structure/77.sql` attached the risk-acceptance evidence FK to a table called `files`, but that table was renamed to `nr_files` back in `DB/Structure/3.sql` — the EF migration the script was split from had the name right, and the hand-split copy did not. The failure was as bad as it was because MySQL implicitly commits every DDL statement: the `START TRANSACTION` wrapping the script bought nothing, so the twenty-odd statements before the bad one had already landed while `db_version` was still 76, leaving the database between versions with no rollback. The script now names `nr_files` (the index and constraint keep their `idx_files_…`/`fk_files_…` names, which is what the EF snapshot expects). A new test replays every numbered structure script in the order `DatabaseService.Update()` applies them, tracking creates, drops and renames, and fails if a script touches a table that does not exist at that point — the class of typo that reads correctly, compiles, reviews cleanly, and only shows up against a production database.
- **Adding or editing a risk no longer kills the desktop client — and six other dialogs that were equally unreachable.** `DialogService` resolves a dialog's view from its view-model name by convention, and the convention was a bare `Replace("ViewModel", "")`. That only ever matched views named `*Dialog`, because those pair with a `*DialogViewModel`; every view named `*Window` pairs with a view model that has no `Window` in its name, so the lookup returned nothing and threw `View for EditRiskViewModel was not found!`. Since the throw happened inside a `ReactiveCommand` with nothing subscribed to `ThrownExceptions`, ReactiveUI's default handler rethrew it on the dispatcher and the process aborted with `SIGABRT` — pressing "Add risk" took the whole app down, unsaved work included. The convention now tries the bare stem first (so the dialogs that already resolved resolve to exactly the same type) and then the `Window` and `Dialog` suffixed forms, which repairs Add/Edit Risk, Close Risk, Edit Mitigation, Edit Incident, Incident Response Plan, IRP Task and Vulnerability Import together. Two guards keep it fixed: a ReactiveUI exception handler installed at builder time, so an escaping command error is logged and reported in a dialog instead of terminating the app; and a test that scans every `ShowDialogAsync` call site in `GUIClient` and fails if any of them names a view class that does not exist — the mismatch is invisible to the compiler, since resolution is reflective, so a test is the only place it can be caught before a user finds it.
- **The v77 schema upgrade no longer fails partway through on the attachment column.** `Structure/77.sql` added Track 3's `risk_acceptance_id` evidence column with `ALTER TABLE \`files\``, but that table is renamed to `nr_files` back in `Structure/3.sql` and the EF model maps it as `nr_files` — so the statement aborted on any real database, leaving the upgrade half-applied and `db_version` short of 77. Every Track 3 read path then failed: the vulnerability register, `GET /Risks/{id}/Vulnerabilities`, `/Vulnerabilities/sla/compliance` and `/Vulnerabilities/LastScanDate` all returned 500 because the columns and tables they need were never created. The integration harness could not have caught this, because the test that builds the full numbered schema was pinned to version 75 and so never applied 77 at all; it now builds to whatever `DatabaseInformation.yaml` declares as the target, which both exercises the newest upgrade files against real MariaDB and fixes the two `EntityScopeQueryFilterTests` that were failing with `Unknown column 'v.component'` — the same staleness seen from the other side, a schema stopping short of columns the model already maps. Phase-specific tests keep their pinned versions, since for those the version under test is the point.
- **`nuke CreateAllDockerImages` (and any Release build) no longer hangs forever on Apple Silicon.** `PackageMacGUI` used to cross-publish `osx-x64` by running the x86_64 .NET SDK inside a `--platform linux/amd64` container, which means running it under QEMU. QEMU mis-emulates the lock-free atomics in MSBuild's `XmlNameTableThreadSafe`, so the MSBuild worker node died with an `AccessViolationException` and `SIGABRT` while the parent `dotnet publish` kept waiting on the dead node's pipe — the container never exited, and the build blocked with no output and no error. The container was never needed: `GUIClient` publishes plain IL (no ReadyToRun, AOT, single-file or trimming), so an `osx-x64` publish only resolves and copies the `osx-x64` runtime pack and nothing x64 is ever executed. It now publishes natively on an arm64 host in well under a minute, and the produced apphost and `libcoreclr.dylib` are verified `Mach-O 64-bit x86_64`.
- **A wedged external command now fails the build instead of blocking it.** The build's `RunProcess` helper called `WaitForExit()` with no timeout, so any hung child process stalled the whole run indefinitely. It now enforces a 30-minute budget per command — generous enough for a cold `docker build` — then kills the process tree and throws, logging whatever the child emitted before it died, which is where the real cause tends to be. Draining that partial output is itself time-bounded, since a surviving grandchild holding the pipe open would otherwise recreate the very hang the timeout exists to break.



## [2.16.0] - 2026-08-24

This release includes new features and improvements.

### Added

- **Track 3 (ASPM) — extensible scanner importers (milestone 3.1).** A versioned `IVulnerabilityReportImporter` contract in the plugin SDK, and ten built-in importers against it: Tenable Nessus, a generic SARIF 2.1 importer (which alone unlocks CodeQL, ESLint, Bandit, Checkov, gitleaks and anything else with a SARIF exporter), OWASP ZAP, Trivy, Semgrep, OpenVAS/Greenbone, Burp Suite, Snyk, Grype and GitHub Dependabot. The contract's cardinal rule is that an importer parses and returns records — it never touches the database, the network or the file system — so persistence, deduplication and entity scoping stay in `ServerServices` and a third-party importer is safe to load and trivial to unit-test. `GET /Vulnerabilities/importers` lists built-ins and plugin importers indistinguishably; `POST /Vulnerabilities/import/{importerName}/{fileId}` resolves by name (the reserved name `auto` sniffs the file's content instead) and runs the import as a background job, because a 500 MB scan file makes a synchronous endpoint a timeout waiting to happen. `GET /Vulnerabilities/import-jobs/{id}` reports status and counts. Every importer reports the records it could not fully parse rather than dropping them silently, which is the classic importer bug: an import that lost a third of its rows otherwise looks exactly like a clean one. The legacy Nessus parser was refactored onto the contract and the old write-as-you-parse path retired, so `import/nessus/{fileId}` — which the desktop client still calls — now runs the same pipeline as everything else.
- **Track 3 — finding lifecycle and audit trail (milestone 3.2).** A dedicated seven-state triage lifecycle (`Active`, `Verified`, `FalsePositive`, `OutOfScope`, `Duplicate`, `RiskAccepted`, `Mitigated`) in a new `status_id` column, separate from the register's fifty-value general-purpose `Status` so the two cannot be confused, with the transition matrix enforced in the service and surfaced as HTTP 422 rather than only in the UI. Suppressing transitions require a stated reason; a duplicate must name the finding it duplicates. Two behaviours on re-import carry the milestone: **sticky triage** — a false positive, out-of-scope or accepted verdict survives the scanner reporting the finding again — and **regression detection** — a mitigated finding the scanner sees again reopens as Active with an event saying so. Every transition writes an append-only `finding_status_history` row recording who, when, why and whether a human, an import or a job did it; there is no update or delete path to that table anywhere in the API, which is the whole point of it. Rendered as a timeline on the finding detail view.
- **Track 3 — formal, expiring risk acceptance (milestone 3.2.3–3.2.4).** A `risk_acceptances` entity generalizing Track 8.1's design: authorizing manager, business justification, compensating controls, residual-score snapshot, evidence attachments, and a **mandatory** expiry date — an acceptance without one is precisely the failure this exists to prevent, "accepted" quietly becoming "forgotten". Accepting findings suppresses them and records an event per finding; revoking or expiring reactivates them. A daily Hangfire job expires lapsed acceptances, reactivates what they covered with `source=Job`, and warns the authorizing manager at T-30 and T-7. The pass is idempotent — running it twice on the same day changes nothing the second time, so re-running a failed job is not something an operator has to think about — and it leaves a finding somebody has already re-triaged where it is, rather than dragging a human decision back to Active.
- **Track 3 — the deduplication engine (milestone 3.3).** Layered, per-scanner strategy chains: `UniqueIdFromTool` (the scanner's own stable id, highest precedence when present), `HashBased` (SHA-256 over a configurable ordered field set, defaulting to tool + rule id + asset + location + CVE), `LegacyHashCode` (the pre-Track-3 Nessus hash, kept so a re-import matches rows the old code created instead of duplicating the whole register once), and `Custom` via a plugin. Keys are **persisted** on the finding and never recomputed, so upgrading the algorithm affects only new imports. The design property throughout is that dedup **groups without discarding**: a second sighting raises the occurrence count and moves the last-seen date, and never overwrites a human-entered field. Findings a **full** scan no longer reports are candidates for auto-close, off by default per scanner — a partial scan mistaken for a full one closes everything outside its slice. Every import is reconstructible from a new `scan_imports` log. The administration screen edits each scanner's chain and field set and includes a preview panel that computes two findings' keys and reports whether they would merge, without saving anything.
- **Track 3 — SLA tracking and aging (milestone 3.4).** Effective-dated `sla_configurations` per severity, seeded to the CISA benchmarks the spec cites (Critical 15 days, High 30, Medium 60, Low 90; triage 2/5/10/15), with an optional per-entity override. Changing a policy supersedes the old row rather than editing it, so a change never rewrites a past compliance number. `sla_due_date` is computed at creation from the policy in force **when the finding appeared** and recomputed on severity change with the reason on the finding's timeline; `DaysOverdue` is derived at read time and never stored, so it cannot drift, and suppressed states pause the clock — a finding nobody is allowed to work on does not accrue overdue days. Surfaced as sortable grid columns, a dashboard compliance widget, and a daily digest job: one message per owner listing everything of theirs that is breached or approaching, rather than the per-finding alerting that trains people to filter the alerts. De-duplicated by (finding, threshold, due date), so a crossing notifies exactly once and moving a deadline legitimately re-arms it.
- **Track 3 — CI/CD-first integration (milestone 3.5).** Scoped, revocable `nrk_`-prefixed API tokens: 256 bits of entropy, stored hashed and shown once, with a public key-id half so authentication is one indexed read and a leaked token is grep-able by secret scanners. Scopes (`vulnerabilities:import`, `:read`, `:write`, `risks:read`) narrow what the token can do on top of the permissions of the user it acts as — the two are an AND — and administrator privileges are deliberately never granted through a token. A `POST /Vulnerabilities/import/{importer}` endpoint takes the raw scan payload as the request body in one curl-able call, streaming it to disk rather than buffering it; an optional `Idempotency-Key` header makes a CI retry storm harmless by returning the original import instead of importing again. `netrisk-console ci gate --job <id> --fail-on new-critical` evaluates a small policy grammar (`new-<severity>`, `any-<severity>>N`, `sla-breach`, `none`) and exits non-zero on violation. "New vs pre-existing" rides on the dedup engine, which is what makes gating non-flaky — a build does not fail for a vulnerability that was already known and accepted. Copy-pasteable, pinned recipes for GitHub Actions, GitLab CI and Azure Pipelines live in [docs/ci/](docs/ci/), each covering the platform-native way to handle the credential.

- **Track 2 — the Master Dashboard (milestone 2.3.3), end to end.** Administrators get a cross-entity posture view: one card per business entity with open risks (banded high/medium/low), open vulnerabilities (critical/high/medium), open incidents, mean risk score and a composite posture bar, ordered worst-first, above an organisation-wide totals band. The roadmap recorded this milestone's backend as complete, but no `/dashboard/master` endpoint or rollup service existed — so both tiers were built. `MasterDashboardService` groups each of the three fact tables by `entity_id` **once** and stitches the results together, rather than the per-entity fan-out the milestone spec rules out; records whose `entity_id` is still null are surfaced in an explicit "Unassigned" bucket so the totals reconcile with the per-module screens. Organisation-wide mean risk is weighted by open-risk count, so a one-risk entity cannot pull the average as hard as a thousand-risk one. Results are cached for two minutes on a singleton and handed out as copies, and the GUI's Refresh bypasses that cache. `GET /Dashboard/Master` is gated by `RequireAdminOnly`; the nav entry is admin-only and a refusal renders as a state of the view rather than a modal box.
- **Track 2 — IRP template editing and automation rules (milestones 2.4.1 and 2.4.2).** A new Administration section edits incident-response playbooks: template CRUD, clone-from-existing, and an ordered task list with instructions, relative due offset, coordinator-approval gate and a predecessor dependency. The matching rule that decides which incidents activate a template (category + status) and each task's assignee rule (fixed user or role) are authored through pickers — the automation engine reads them as JSON, and this screen composes and parses those documents rather than making an author type them. `ClientServices` gained the `IrpTemplatesRestService` the milestone noted was missing entirely, and the API gained the template-task CRUD it never had (`GET/POST /IrpTemplates/{id}/Tasks`, `PUT/DELETE .../{taskId}`) plus `POST /IrpTemplates/{id}/Clone`. Predecessor edges are validated for acyclicity on save — a cycle would make the generated plan impossible to schedule — deleting a task re-parents its successors instead of orphaning them, and a clone writes its tasks in topological order so predecessors always exist before their dependants. A cloned or newly created template starts **disabled**, so it cannot begin matching live incidents before it has been reviewed.
- **Track 2 — incident-response Gantt with critical path (milestone 2.4.3).** `IrpScheduleService` runs a real CPM forward/backward pass over a plan's tasks and `GET /IncidentResponsePlans/{id}/Schedule` returns early/late start, slack, the critical-path chain, and per-task blocked and overdue flags. The GUI renders it as a parented, singleton Gantt window opened from the plan editor: bars coloured by state (critical, overdue, blocked), slack per row, a "now" marker, and a legend. Computing the path server-side means every client draws the same bars.
- **Track 2 — multi-entity scoping is now actually enforced (milestones 2.3.1 and 2.3.2), completing Track 2.** The roadmap described this as "enforced server-side"; it was not. `ApplyEntityScope` was called from exactly one query — `RisksService.GetAllAsync` — and `RisksController` never passed it a `ClaimsPrincipal`, so it always received null and returned the query unfiltered. Vulnerabilities, hosts, incidents, assessments, exports and reports had no scoping call at all. Any authenticated user could read every tenant's data. Enforcement now lives on the model as EF Core global query filters, which is the mechanism the 2.3 spec names first: the five `entity_id`-bearing types are filtered directly, and the nine record types that inherit an entity from a parent (mitigations, management reviews, host services, assessment questions, answers, runs and run answers, fix requests) are filtered through it, so a service that never thinks about scoping still cannot cross the boundary. Because query filters also govern `Find` and `FirstOrDefault`, an update or delete aimed at another entity's row resolves to nothing and turns into a clean not-found rather than a silent cross-tenant write. The one thing a query filter cannot cover — creating a record stamped with someone else's `entity_id`, or re-stamping one of your own on the way out — is refused in `AuditableContext.SaveChanges` and surfaces as a 403. A caller holding exactly one entity gets new records filed there automatically; a caller holding several must say which. An authenticated user with no assignment sees nothing, while global admins and non-HTTP callers (background jobs, the console client, migrations) stay unrestricted. Verified by 21 negative tests across every service including exports, and by MariaDB integration tests that assert the predicate reaches SQL instead of being evaluated client-side — the in-memory provider would have passed either way.
- **Track 2 — Entity Access administration (milestone 2.3.2).** A new Administration section grants and revokes per-entity roles for a user, backed by the `UserAccessRestService` client the API had been waiting on. Revocation is a soft revoke, so "who could access what on date T" stays answerable. The screen calls out explicitly that a user with no assignment sees no data at all, which is the intended deny-by-default and otherwise surprises people.
- **Track 2 — persisted IRP task dependencies and the blocked-task override (milestone 2.4.3).** Response-plan tasks can now declare that one waits on another; the edges live in a new `incident_response_plan_task_dependencies` table (schema phase 7, `db_version` 76) and are validated acyclic on save, since a cycle makes the plan impossible to schedule. A task with no explicit edge still falls back to the `ExecutionOrder`/`IsSequential` stage ordering, so plans authored before this schedule exactly as they did. Completing a task whose predecessors are unfinished now requires a stated reason and records who overrode the block and when.

- **API controller test coverage, 8.7% → 77.1%.** The REST layer had tests for 4 of its 37 controllers; 731 new tests now cover 31 of them, every action and each of its outcome branches — not-found, bad request, unauthorized, conflict, and the catch-all 500 handlers that had never once been executed. What remains uncovered is the authentication and bootstrap surface (`AuthenticationController`, the JWT/Basic handlers, the policy providers, `Program.cs` and the bootstrappers) and `FaceIDController`, which needs a real ONNX runtime. `API.Tests` registration became convention-based to make this sustainable: `ServiceRegistration` discovers every `Mocked*.Create()` factory and every controller by reflection, and `BaseControllerTest.ResolveController<T>(configure)` layers per-test doubles on top, so covering a new controller no longer means editing a file shared with every other test.
- **ClientServices test coverage, 5.2% → 70.6%.** The desktop client's REST layer had 3 of ~44 services tested; 1,083 new tests now cover 26 of them, every method with its happy path and each error branch it actually contains. The blocker was structural rather than effort: `IRestService.GetClient()` returns a **concrete** `RestClient`, which no NSubstitute double can satisfy, so the old shared mock could only reach the handful of methods that use `GetReliableClient()`. `ClientServices.Tests/Mock/StubRestBackend.cs` replaces it by stubbing the layer underneath — a real RestSharp client over a fake `HttpMessageHandler` — so serialization, status handling and RestSharp's own extension methods all run for real, and a test can assert the verb, path, query and body the service actually sent. Registration is convention-based as in `API.Tests`, and `ServiceResolutionTest` asserts every discovered service contract resolves, which is what caught the two services whose dependencies nothing supplied. Still uncovered: `AuthenticationRestService` and `FaceIDRestService`, the two Nessus/ScoreCard importers, and `RestService` itself.
- **Test projects for the four projects that had none.** `SharedServices.Tests`, `BackgroundJobs.Tests`, `ConsoleClient.Tests` and `WebSite.Tests` cover `LanguageManager`, the website-sync setting helpers and `TmpCleanup`'s retention cutoff, the CLI command surface and the numbered-SQL upgrade ritual, and the website's signed `/sync` authentication boundary. `ConsoleClient.Tests` asserts the ritual itself rather than any one migration: that `targetVersion` matches the highest numbered script, that Structure and Data agree and have no gaps, that every data script bumps `db_version` to its own number, and that every script on disk is declared as `<Content>` so it actually reaches a release.

### Changed

- `JobManager` gained an `IJobManager` interface. A controller that starts a background job could not otherwise be built in a test without standing up the whole messaging and localization stack behind it.
- `MasterDashboardService` is registered as a **singleton** rather than a transient with static cache state, so one process-wide cache does not leak between tests running in parallel.
- New features and bug fixes must now ship with tests in the same change — the happy path plus each error branch for a feature, a failing-then-passing regression test for a fix. Recorded in [CLAUDE.md](CLAUDE.md) and [src/AI_TESTING_INSTRUCTIONS.md](src/AI_TESTING_INSTRUCTIONS.md).

### Fixed

- **`dotnet ef migrations script` could not run, and every regenerated model snapshot broke the build.** A `string` column with a `char(n)` store type makes EF Core 10's `ElementMappingConvention` treat the property — a string being an `IEnumerable<char>` — as a primitive collection of `char`. The MySQL provider has no char element mapping, so the model build died with a `NullReferenceException` raised deep inside the type mapping source, naming no property, and taking `migrations script`, `HasPendingModelChanges` and `database update` down with it. `processed_sync_actions.client_action_id` was the only such column. Expressing it as `HasMaxLength(36).IsFixedLength()` avoided writing `char(36)` in `OnModelCreating`, but the snapshot generator re-resolves store types and wrote it back, so the trap re-armed itself on every `migrationAdd.sh` and had to be patched out by hand each time — which is how Track 3's own migration was authored. The column is now `varchar(36)` (schema phase 9, `db_version` 78); the two hold the same 36-character id and differ only in trailing-space padding, which a UUID string never has. `Guid` columns are unaffected and deliberately not changed: Pomelo maps them to `char(36)` too, but a `Guid` is not a collection of anything. `DAL.IntegrationTests/StringColumnTypeGuardTest` now fails immediately if the shape is reintroduced — in the model or in the generated snapshot — and explains the cause instead of leaving the next person to bisect a null reference.
- **`CWE-089` and `CWE-89` were treated as different weaknesses.** SARIF rule tags write the padded form (`external/cwe/cwe-089`) where the advisory databases write the unpadded one, so a finding imported from SARIF would not match its own CWE in any lookup, and a dedup key built from the CWE list would not match the same finding from another scanner. Leading zeros are now stripped on extraction. Found by the Track 3 importer tests.
- **A gate policy of `none` passed a build whose import had failed.** The opt-out was evaluated before the failed-import check, so a pipeline that only reports would report success for a scan that never landed. Found by the Track 3 gate tests.
- **Cross-entity data exposure.** See the multi-entity scoping entry above: entity scoping was declared but not wired up, leaving every authenticated user able to read, update and delete records belonging to business entities they were never assigned to.
- **The schema upgrade to `db_version` 75 and 76 could not run from a build.** `DB/Structure/75.sql`, `76.sql` and their `DB/Data` counterparts were committed but never declared as `<Content>` in `ConsoleClient.csproj`, so they were not copied to the output directory — a packaged console client stopped at 74 while `DatabaseInformation.yaml` asked for 76, meaning the IRP task-dependency table above would never have been created on any real database. Every numbered script must be hand-declared in the project file, which is exactly the step that gets forgotten; `ConsoleClient.Tests` now fails if a script on disk is not declared.
- **Two locales of the same language crashed the language list.** `LanguageManager.AllLanguages` keyed its dictionary by the two-letter ISO language code, so configuring `AvailableLocales` with both `en-US` and `en-GB` (or `pt-BR` and `pt-PT`) made `ToDictionary` throw `ArgumentException`. Because the dictionary is built lazily, the throw surfaced from the property getter — anywhere the language list was read — rather than at startup near the misconfiguration. The locales now collapse onto one entry per language, the first one configured winning.
- **The desktop client's cache ignored its own expiry, and swept itself on a background thread.** `MemoryCacheService.Get` returned the stored value without ever comparing it to the expiry stamp it had saved alongside it, so an entry lived until an unrelated sweep happened to remove it — a user name, team or entity list edited elsewhere could keep being served long past the sixty minutes it was cached for. That sweep was itself `async void` over a `Task.Run`, so it mutated the plain `Dictionary` backing the cache from a background thread while the caller that started it was already reading it, and any exception it raised was unobservable. Eviction is now lazy and synchronous — a read checks the entry it found and drops it if it is stale — so expired data is never served, there is no background thread to race, and the behaviour is deterministic enough to be tested. `UsersRestService`, `TeamsRestService` and `VulnerabilitiesRestService` take the cache as a constructor dependency instead of pulling it out of the static service-provider accessor, which is what made their cache-hit paths untestable.

- `RuleBrokenException` gained a constructor that carries a message. The existing single-argument overload set only the rule name and left `Message` as the framework default, so a caller catching one learned nothing about what had gone wrong.
- **The desktop client discarded the reason a request was refused.** Two patterns in `ClientServices` threw away the error information a caller needs. First, `IncidentResponsePlansRestService` translated a 400 into a `RuleBrokenException` carrying the server's explanation — "adding this dependency would close a cycle", "an override reason is required" — but the branch was unreachable: RestSharp's `PostAsync` raises `HttpRequestException` on that status before any status check runs, so the Gantt view's rule-violation toast could never fire and every refusal read as a communication failure. Second, a handful of methods raised a specific exception inside a `try` whose `catch (Exception)` immediately re-wrapped it, so the guard was pointless: both `EmailsRestService` send methods, `AssessmentsRestService.DeleteRun` (whose `RestException` was replaced by a bare `Exception`, losing the HTTP status), and `HostsRestService.GetAllHostServiceAsync` (where a caller could not tell "no data" from "the transport broke"). Those catches are now narrowed to `HttpRequestException`, matching the pattern the rest of each file already used.
- **Six client-side writes reported a failed server response as success.** Every one of these let the desktop client tell the user the change had been saved while the server had refused it. `HostsRestService.DeleteService` had its guard **inverted** — `if (response.StatusCode == HttpStatusCode.OK) throw` — so a successful delete raised and a rejected one returned quietly. `MitigationRestService.Save`, `FilesRestService.DeleteFile` and `ReportsRestService.DeleteReportAsync` guarded only on `response == null`, which RestSharp's untyped verbs never return, making the check dead code. `MessagesRestService.DeleteMessageAsync` never looked at the status, and `ReadMessageAsync` discarded the response entirely. Worst for data integrity, `RisksRestService.SaveRiskScoring` threw only when the error body happened to deserialize into an `OperationError`, so any other error body meant a discarded risk score read back as saved. The reason these all had the same shape is RestSharp 114's status handling: 404 is the *only* non-2xx status the verb extensions do not raise on, so a rejected write arrives as a perfectly ordinary completed response and nothing but an explicit status check distinguishes it. All six now check the status; where an `OperationError` is present it reaches the caller as before, and a shared `RestServiceBase.TryReadOperationError` helper means an error body that is absent or is not an `OperationError` produces a plain failure instead of the raw `JsonException` that `SaveRisk` used to leak. `NotificationsViewModel`'s two `async void` command handlers gained the try/catch they now need, since an escaping exception there would have been unhandled rather than shown.
- **Misleading client-side error messages.** `ConfigurationsRestService.SetBackupPassword` reported "checking backup password status" on failure — copied from the getter; `TechnologiesRestService` named `/Technology` as the failing URL while the request went to `/Technologies`; and `EmailsRestService`'s update-mail method reported the fix-request path. `RisksRestService.DeleteRisk` and `DeleteRiskScoring`, `RolesRestService.UpdateRolePermissions`, `MgmtReviewsRestService.Create` and `EntitiesRestService`'s entity cache also threw bare `Exception`s where the rest of the layer throws typed ones, which left callers unable to distinguish a refused request from a bug.



## [2.15.0] - 2026-08-21

This release includes new features and improvements.

### Added

- **Track 1 Milestone 1.5 — Interaction & Workflow Standardization (Phases A–E).** Applies the interaction standard in [docs/ux-interaction-standard.md](docs/ux-interaction-standard.md) (IX-1…IX-9) across the desktop client, completing Track 1. 164 files changed (+5.7k/-4.1k lines). The phase-by-phase record is in that document's new Part V; the highlights:
  - **One dialog stack.** The nine legacy hand-`new`-ed edit windows — CloseRisk, AddFaceImage, EditMgmtReview, EditMitigation, EditRisk, VulnerabilityImport, EditIncident, and the IRP plan and task windows — now derive from `DialogWindowBase<TResult>` and open through `DialogService`, so they get Esc, Ctrl/Cmd+S, owner-centring and typed results from one place instead of nine. Saved records travel back as typed results and the caller updates its own collection, replacing the events the dialogs used to raise into their parents. Launcher-side size overrides are gone: window size is declared in XAML only. `DialogService` now parents to and dims the **actual** launching window (via the new `IDimmableWindow`) rather than always MainWindow, so dialogs opened from a report manager no longer centre over and grey out the wrong window. `ISaveableDialog` is wired wherever a `SaveCommand` exists, fixing dead Ctrl+S in the report dialogs, ChangePassword and CreateReport. `DialogWindowBase` also stopped forcing `Min = Max` on open, so a dialog declaring `CanResize="True"` (the assessment runner and dialogs, FixRequestDialog) is actually resizable — the XAML no longer lies.
  - **A real feedback language.** New `INotificationService` + `NotificationHost` toast stack: routine successes ("Saved", "Deleted", "Test run triggered") are transient notes instead of modal boxes the user must dismiss — nine success `MessageBox`es converted, and the two report manager windows, which reported *nothing at all* on save/delete/test, now report. Errors keep their modal box, because they need acknowledging. Validation messages surface inline under the field and on the disabled Save button's tooltip. `ViewModelBase` gained `IsBusy`/`WithBusyAsync`, and the six views that showed no busy indication at all (Entities, Incidents, Users, Hosts, Devices, Configuration) now do — the entity-tree reload in particular. Gated toolbar buttons state *why* they are disabled via the new `ActionTooltipConverter` (permission vs. current status), and every delete confirmation goes through one `ConfirmationDialog` helper — Yes/No, item name interpolated, cascade spelled out — replacing the four different button sets (YesNo, OkCancel, OkAbort, YesNoAbort) the same job used to use.
  - **The risk lifecycle became a workflow.** Plan-mitigation, Revise, Add-review, Close and **Reopen** moved out of the scrolling detail pane (where they were 22px icons) into a state-driven toolbar on RiskView, enabled per the risk's current status and modelled on the vulnerability triage toolbar. After a management review commits, the next step it recorded is now offered instead of being captured and ignored (`RiskHelper.GetNextStepAction`, unit-tested); after creating a risk, planning its mitigation is offered.
  - **Forms that fit.** EditIncidentWindow's seven stacked 120px narrative boxes became tabs and its Save/Save&Close/Close triple became one Save + Cancel — Save now commits and closes rather than silently flipping the window into Edit mode. The IRP task form's 25-row flat grid became four named sections. `EntityForm`, which built its entire UI imperatively in 450 lines of C#, was rebuilt as XAML over per-field-kind `DataTemplate`s with a real view-model, a Cancel alongside Save, dirty tracking, and validation that is **enforced** rather than merely displayed. DeviceView — the only view in the app with per-row action buttons — became a register with a selection toolbar and a status bar. The two near-duplicate report manager windows now share one `ManagerShell` control.
  - **Shell polish.** A new `INavigationService` owns all shell routing and auxiliary windows; `WindowsManager` (a global window list that view-models grepped) is deleted, as is every `$parent.Parent.Parent…×8` `CommandParameter`. Reports, Notifications and Administration are now modeless, parented, singleton auxiliary windows — Administration no longer blocks the whole shell. MainWindow and the auxiliary windows persist and restore their geometry, clamped to a screen that still exists. `AuxiliaryWindowBase` gives every plain window Esc. Ctrl+F works on six module views with one semantic (reveal, focus, live-filter), and the editor dialogs have explicit TabIndex chains.
  - **Dead surfaces removed:** the orphaned `RisksPanelView` trio, the duplicate `AssessmentQuestionView` editor (assessment questions are edited inline by the builder — IX-5 forbids two editors for one object), the dead `btn_SettingsOnClick` path, and the duplicated `StrThreatSources` block. The gear icon labelled "Settings" that opened Administration is now labelled Administration, and the read-only window misnamed `Settings` is now `AboutWindow`. `LoadConfigurationWindow` was rebuilt: localized (including the "Well-come" typo), sized, validated, with Esc/Enter and a Cancel.
  - **Verification, and its limits.** The solution builds clean at zero warnings and all 563 unit tests pass. The GUI itself was **not** exercised at runtime while this work was done — Avalonia cannot start a window on this host from a non-interactive shell (`RenderTimer ... -6661`), so a manual click-through of the migrated dialogs is worth doing before release. `./build.sh LintUi` still reports 162 pre-existing `docs/ui-standard.md` violations (16 R1, 4 R4, 26 R5, 116 R6 — many R6 are false positives from the linter matching per line); those belong to the UI-STD-001 reference item, not to IX-1…IX-9, and only the ones inside files touched here were fixed.
- **`GUIClient.Tests`** — a first test project for the desktop client, covering the validation layer (13 tests). It deliberately does not reference `GUIClient` (that would pull Avalonia into a headless run); it compiles the files under test directly. Also `ServerServices.Tests/Track1` covering the review next-step mapping (7 tests). Unit-test total: 563.

### Changed


- **Upgraded the `libs/` submodules and reattached their detached HEADs.** All five were sitting at their tracking-branch tips already (nothing to pull), but three — `netrisk-plugin-sdk`, `reliable-rest-client-wrapper` and `NessusParser` — were on a detached HEAD; they are now on `main`/`master` respectively. `Aura.UI` was deliberately left on its `avalonia12` branch rather than moved to its default `master`: `avalonia12` is 9 commits ahead and `master` is still on Avalonia 11.2.2, so switching would have regressed it. Package upgrades, each committed and pushed in its own repository:
  - **Aura.UI**: Avalonia (+`Desktop`, +`Markup.Xaml.Loader`) 12.0.1 → 12.1.1, `ReactiveUI` 23.2.1 → 24.1.0, `ReactiveUI.Avalonia` 12.0.1 → 12.1.1, `System.Reactive` 6.1.0 → 7.0.0, `Xaml.Behaviors.*` 12.0.0 → 12.0.5. The ReactiveUI 24 `Unit` → `RxVoid` rename needed no source changes there, as the library declares no `ReactiveCommand<…, Unit>` members. Avalonia 12.1's `Bitmap.Save(Stream, int?)` deprecation was fixed in `BlurryImage` with `PngBitmapEncoderOptions.Default`, the same fix applied in `GUIClient`.
  - **netrisk-plugin-sdk**: `Serilog` 4.3.1 → 4.4.0, `SkiaSharp` 3.119.2 → 3.119.4.
  - **reliable-rest-client-wrapper**: `Polly` 8.6.6 → 8.7.0.
  - **TreeDataGrid.Avalonia**: `AvaloniaVersion` 12.0.1 → 12.1.1, plus `AvaloniaSamplesVersion` 12.0.* → 12.1.* so that repo's own samples/tests don't hit an NU1605 downgrade against the newer library.
  - All six submodule projects NetRisk consumes now report up to date. `SkiaSharp` 3.119.4 → 4.151.1 is a deliberate hold: Avalonia.Skia 12.1.1 depends on 3.119.4, and `SKBitmap` is spread across the public `INetriskFaceIDPlugin` surface, which is registered as a **shared type** with `PluginLoader` — crossing the SkiaSharp major would change that type's identity and break externally built FaceID plugins at load time. It should move when Avalonia moves.
  - **Not fixed, pre-existing**: Aura.UI's samples and `Tests/MathsForUI.Test` do not build, and did not before this change either (verified by rebuilding at unmodified HEAD). They target `net8.0` while the libraries moved to `net10.0` (NU1201), and `Aura.UI.Gallery.Web` additionally needs the `wasm-tools-net8` workload. Retargeting them and porting the galleries off Avalonia 11.2.2 is separate work.
  - Verified after the upgrade: NetRisk builds clean at the 16-warning baseline, all 566 tests pass, and the GUI launches and renders correctly — which exercises both Aura.UI (theme) and TreeDataGrid (grids).

- **`JetBrains.Annotations` 2025.2.4 → 2026.2.0** in the four test projects. Compile-time annotations only, no runtime impact.
- **`NSubstitute` 5.3.0 → 6.2.0** across `API.Tests`, `ServerServices.Tests`, `ClientServices.Tests` and `DAL.IntegrationTests`. The mocking surface in use is entirely core API (`Substitute.For`, `Returns`, `Received`, `Arg.Any`/`Arg.Is`, `Throws`/`ThrowsAsync`, `When`), none of which changed in v6, so the major bump needed no test edits. All 566 tests pass.
- **Removed the now-redundant `SQLitePCLRaw.bundle_e_sqlite3` 2.1.13 pin from `WebSiteData`.** It was added to patch GHSA-2m69-gcr7-jv3q / CVE-2025-6965 back when EF Core Sqlite 10.0.7 pulled `SQLitePCLRaw` 2.1.11. After the EF Core bump to 10.0.11 the transitive version is 2.1.12, which already bundles SQLite 3.53.3 — well past the 3.50.2 fix. Verified by removing the pin and re-running `dotnet list package --vulnerable --include-transitive`: still clean, with the whole `SQLitePCLRaw` family resolving to 2.1.12. Dropping the pin means the SQLite provider now tracks whatever EF Core ships instead of being held one patch ahead by hand. Deliberately *not* bumped to `SQLitePCLRaw` 3.0.5: that is a major release EF Core 10 does not expect, and pinning across it would reintroduce exactly the kind of divergence this removal eliminates.
- **Replaced the deprecated `Serilog.Sinks.RollingFile` 3.3.0 with `Serilog.Sinks.File` in `GUIClient`, and gave the client real log rolling.** The package was NuGet-deprecated (Legacy), and it turned out to be a half-finished migration: `GUIClient` already referenced `Serilog.Sinks.File` 7.0.0 and already called `.WriteTo.File(...)`, so the RollingFile package was a dead reference with no call sites and no config-driven activation (logging is wired in code in `LoggingBootstrapper`, and `Serilog.Settings.Configuration` isn't used anywhere). What survived from the old sink was the *idiom*: the filename was hand-stamped `log-{yyyy-MM-dd}.txt` once at startup while `.WriteTo.File` had no `rollingInterval`. For a desktop client left open past midnight that meant every subsequent day's entries kept landing in the file for the day it was launched, with no size cap and no retention.
  - `GUIClient` now uses `.WriteTo.File(path, fileSizeLimitBytes: 10000000, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Day)` — the same configuration `API`, `BackgroundJobs` and `WebSite` already use, so the client is no longer the odd one out. The date is supplied by the sink rather than by the filename, and daily rolling brings Serilog's default 31-file retention with it.
  - **User-visible change**: GUI log files are now named `nr-gui<yyyyMMdd>.log` (matching the existing `nr-api.log` convention) instead of `log-<yyyy-MM-dd>.txt`, in the same `…/NRGUIClient/logs` directory. Pre-existing `log-*.txt` files are left untouched and are not covered by the new retention limit, so they can be deleted by hand if desired.
  - `dotnet list package --deprecated` now reports nothing for the solution's own projects; the only remaining entries are `ReactiveUI` / `ReactiveUI.Avalonia` inside the `libs/Aura.UI` submodule.


- **Test suite migrated from xUnit v2 to xUnit v3 (all five test projects).** `xunit` 2.9.3 was deprecated in favour of `xunit.v3`; the suite now runs on **Microsoft.Testing.Platform (MTP)** instead of VSTest, because xunit.v3 4.0.0 depends on MTP and the .NET 10 SDK no longer supports running MTP projects through VSTest. All **566 tests still pass**, including the 23 Testcontainers MariaDB integration tests — no tests were lost or skipped in the move.
  - `global.json` **moved from `src/` to the repository root** and gained a `test.runner` = `Microsoft.Testing.Platform` section. The runner setting is what makes `dotnet test` work at all; without it the SDK attempts VSTest and errors out. It has to be at the root because `global.json` is resolved by walking up from the current directory, and the documented commands run from the repo root. Moving it also means the SDK pin now applies from the root, which it previously did not.
  - Test projects are now self-executing (`<OutputType>Exe</OutputType>`), as xunit v3 requires.
  - Removed from every test project: `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio` and `coverlet.collector`. All three are VSTest-specific and unused under MTP. Coverage now comes from `Microsoft.Testing.Extensions.CodeCoverage` 18.10.0, which also gave `DAL.IntegrationTests` coverage instrumentation it never had.
  - Dropped the `System.Security.Cryptography.Xml` transitive pins from `API.Tests` and `ServerServices.Tests`. They existed to patch a vulnerable transitive dependency of `Microsoft.NET.Test.Sdk`; with the test SDK gone, nothing references that package any more (verified against `project.assets.json`), so the pins were dead references with misleading comments. `dotnet list package --vulnerable --include-transitive` remains clean.
  - `MariaDbContainerFixture` updated for the v3 `IAsyncLifetime` contract, which returns `ValueTask` rather than `Task`.
  - **xUnit's filter flags are not forwarded through `dotnet test`** — they silently match zero tests, so `--filter "Category!=Integration"` no longer works. Filtering is done by invoking the built test executable directly (`-class`, `-method`, `-trait-`). [CLAUDE.md](CLAUDE.md) documents the working recipes.
  - `xUnit1051` is suppressed solution-wide in [src/Directory.Build.props](src/Directory.Build.props). The v3 analyzers advise threading `TestContext.Current.CancellationToken` through every call that accepts a `CancellationToken`; that is sound but it is an 83-call-site test refactor rather than part of this upgrade, and left unsuppressed it buried the 16 pre-existing real warnings. Wiring cancellation through the suite is follow-up work.

- **Dependency refresh across the solution (patch/minor level only)**: brought the Microsoft-stack packages (EF Core, `Microsoft.Extensions.*`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `System.Drawing.Common`) and `Mapster` from 10.0.7 to 10.0.11, `Serilog` 4.3.1 → 4.4.0, and the `dotnet-ef` tool manifest 10.0.9 → 10.0.11. Also `System.IdentityModel.Tokens.Jwt` 8.17.0 → 8.22.0, `BCrypt.Net-Next` 4.1.0 → 4.2.0, `BouncyCastle.Cryptography` 2.6.2 → 2.7.0, `MySqlConnector` 2.5.0 → 2.6.2, `MySqlBackup.NET.MySqlConnector` 2.7.0 → 2.7.1, `Hangfire` 1.8.23 → 1.8.24, `QuestPDF` 2026.6.0 → 2026.7.3, `ClosedXML` 0.105.0 → 0.105.1, `LiveChartsCore` 2.0.2 → 2.0.5 and `Microsoft.AspNetCore.Localization` 2.3.9 → 2.3.12. No behavioural change intended; full solution build and all 546 non-integration tests pass.
- **`SkiaSharp` moved off a preview build**: `API` and `Tools` referenced 3.119.3-preview.1.1 because Avalonia.Skia 12.0.2 depended on a preview. Avalonia.Skia 12.0.5 and later depend on the stable 3.119.4, so both projects now reference **3.119.4** stable. Deliberately *not* moved to the 4.x line, which Avalonia 12 does not use.
- **Avalonia updated 12.0.2 → 12.1.1** (`Avalonia.Controls.DataGrid` 12.0.0 → 12.1.2, `Avalonia.Skia` 12.0.2 → 12.1.1). Required raising the `Tmds.DBus.Protocol` transitive security pin from 0.92.0 to 0.94.2, because Avalonia 12.1.1 depends on >= 0.94.1 and the old pin would have downgraded it; 0.94.2 still satisfies the GHSA-xrw6-gwf8-vvr9 floor the pin exists to enforce. Avalonia 12.1 deprecated `Bitmap.Save(Stream, int?)`, so the four call sites in `GUIImageTools` and `AvaloniaToSkiaConverter` now pass `PngBitmapEncoderOptions.Default` — the exact equivalent of the previous default. GUI verified running: dashboard, charts and login dialog all render.
- **ReactiveUI upgraded 23.2.19 → 24.1.0, `Splat` 19.3.1 → 21.0.0, `System.Reactive` 6.1.0 → 7.0.0, `ReactiveUI.Avalonia` 12.0.1 → 12.1.1.** This is a framework change, not a version bump: ReactiveUI 24 ships its own Rx primitives and renamed `System.Reactive.Unit` to `ReactiveUI.Primitives.RxVoid`, so `ReactiveCommand.Create(...)` now returns `ReactiveCommand<RxVoid, RxVoid>`. Ported **359 generic-argument sites across 46 files** in `GUIClient` and `AvaloniaExtraControls`. Notes on how it was done:
  - `RxVoid` is imported with a **type-only using alias** (`using RxVoid = ReactiveUI.Primitives.RxVoid;`) rather than `using ReactiveUI.Primitives;`. A plain namespace import pulls in ReactiveUI's own `Subscribe`/`Select`/`Throttle` extension methods, which are ambiguous with `System.Reactive`'s and produced 46 `CS0121` errors. The alias keeps every existing Rx pipeline resolving to `System.Reactive` exactly as before, so operator and scheduler semantics are unchanged.
  - ReactiveUI 24 no longer brings `System.Reactive` or `DynamicData` in transitively. Both are now explicit references in `GUIClient`: `System.Reactive` for `Observable`/`Subject`/`Throttle`, and `DynamicData` 9.4.33 purely for its Kernel extension methods (`IndexOf`, `AddRange`) used in `Program.cs`, `EditEntityDialogViewModel` and `VulnerabilitiesViewModel`. Nine other `using DynamicData;` imports were genuinely unused and were removed.
  - The two deliberate `System.Reactive` uses in `AssessmentRunViewerViewModel` (`Subject<Unit>` / `Unit.Default`) are unchanged — only `ReactiveCommand` type arguments moved to `RxVoid`.
  - `AvaloniaExtraControls` needed no `System.Reactive` reference afterwards; its only use was the now-removed `Unit`.
  - Verified at runtime, not just at compile time. ReactiveUI 24 requires explicit initialization and throws from `WhenAnyValue` if it is missing; the existing `UseReactiveUI(_ => { })` in `Program.cs` does satisfy it under the real app lifecycle. Confirmed in the running app: `WhenAnyValue` chains, `RxVoid` command execution, `System.Reactive` `Subscribe` on ReactiveUI 24 command output, `ThrownExceptions.Subscribe`, and `canExecute` gated by an `IObservable<bool>` all behave correctly, with no unhandled exceptions.
  - **Not covered by automated tests**: `GUIClient` view models have no test project, and the running-app check only reaches the dashboard and login window. The authenticated screens (assessments, incidents, vulnerabilities, reports, entity forms) were changed but not exercised.
- **`Pomelo.EntityFrameworkCore.MySql` 10.0.0-rtm.1 → 10.0.0-rtm.3**. Verified against a real MariaDB container (all 23 `DAL.IntegrationTests` pass) and with the EF tooling (`migrationsList.sh` resolves the model and lists all 12 migrations).
- **`YamlDotNet` 17.1.0 → 18.1.0** (`API`, `ServerServices`). The deserializer API used here (`DeserializerBuilder` / `WithNamingConvention` / `IgnoreUnmatchedProperties`) is unchanged. Covered by the 31 SchemaUpgrade tests, which include one that parses the real shipped `src/ConsoleClient/DB/SchemaUpgradePhases.yaml`.
- **`Spectre.Console` 0.55.2 → 0.57.2** and `Spectre.Console.Cli.Extensions.DependencyInjection` 0.24.0 → 0.28.0. `Spectre.Console.Cli` **stays at 0.55.0** — that is still its latest stable release (the next one is `1.0.0-alpha`), and it floors at `Spectre.Console` >= 0.55.0 so it accepts 0.57.2. The DI extension 0.28.0 wants `Microsoft.Extensions.DependencyInjection` 10.0.11, which the Tier 1 bump already provides. CLI verified at runtime: root help plus the `database`, `database upgrade-schema` and `keys` subcommands all render.
- **`Microsoft.ML.OnnxRuntime` 1.25.1 → 1.29.0** (`API`). Note this is inert: nothing in `API`, `ServerServices`, `Tools` or `Model` references ONNX at all — face embeddings are computed client-side in `GUIClient` (`AddFaceImageViewModel`, the only `FaceONNX` consumer), and the server side only does HMAC template anchoring via `BiometricTools`. See the note below.
- **Nuke build stack updated**: `Nuke.Common` 9.0.4 → 10.1.0, `Microsoft.Build*` 17.14.28 → 18.9.6, `NuGet.Packaging` 6.14.3 → 7.9.0, `Tools.InnoSetup` 6.7.1 → 7.1.0. Nuke 10 removed `SolutionModelTasks.ParseSolution`, so `build/Build.cs` now uses the replacement `AbsolutePath.ReadSolution()` extension. Verified by running the `Usage`, `Restore` and `CompileApi` targets — the last one exercises `Solution.GetProject(...)`, so the parsed solution model is confirmed working, not just compiling.

### Fixed

- **The desktop client's validation had been dead since February 2026.** `GUIClient/Validation/ValidationExtensions.cs` was a stub: `ValidationRule(...)` returned `Disposable.Empty` and `IsValid()` returned `Observable.Return(true)`. `ReactiveUI.Validation` had been dropped in commit `4c4abaa5` ("Fix Avalonia ReactiveUI startup") and replaced with these no-ops to keep the tree compiling, so for six months **not one** of the ~40 `ValidationRule`s declared across 15 view-models gated anything — every `SaveEnabled`/`CanSave` flag driven by `IsValid()` was permanently true. The July 2026 UX study recorded this as "validation is invisible"; it was in fact absent.
  - Replaced with an in-tree `ValidationContext` (rather than the package, which has not been rebuilt against ReactiveUI 24). It keeps the same declaration surface — existing `this.ValidationRule(...)` / `this.IsValid()` call sites are unchanged — and additionally exposes the failing rules' text, which is what the new disabled-Save tooltips and inline error summaries bind to. Rules fail closed: a rule that has not produced a value yet counts as invalid, and a predicate that throws counts as invalid rather than tearing down the rule.
  - **This is a behaviour change, not a pure refactor.** Dialogs whose rules genuinely fail will now disable Save where they previously did not — *including on legacy records that do not satisfy the rules*, for example an existing host stored without a valid FQDN or IP. That is the intended behaviour of the rules as written, but it is worth knowing before the release.
  - Two view-models declared a local `IsBusy` that shadowed the new base-class one; both now use the base property. `ChangePasswordDialog`'s confirmation rule only re-evaluated when the confirmation box changed, so typing the password *after* the confirmation left a stale result — it now watches both fields.

- **Stale `NU1608` suppression rationale in `src/Directory.Build.props`**: the comment justified the solution-wide suppression partly by Pomelo.EntityFrameworkCore.MySql pinning EF Core Relational to 9.x. `DAL` now uses Pomelo 10.0.0-rtm.1, which allows `[10.0.0, 10.0.999]` and therefore accepts the EF Core 10.0.x the solution resolves. Verified by restoring with the suppression lifted: the only remaining `NU1608` is the legacy jQuery.UI.Core 1.8.9 / jQuery 3.7.1 pairing in `WebSite`. The suppression is still needed, but only for that reason; the comment now says so.
- **Stale `SQLitePCLRaw` pin comment in `WebSiteData`**: it described EF Core Sqlite 10.0.7 pulling `SQLitePCLRaw` 2.1.11. After the EF bump, 10.0.11 pulls 2.1.12, which already bundles SQLite 3.53.3 (verified from the packaged native library) — past the 3.50.2 fix for GHSA-2m69-gcr7-jv3q. The 2.1.13 pin is kept deliberately one patch ahead but is no longer load-bearing for the advisory, and the comment now records that.

- **Every database operation failed with a `NullReferenceException` because the EF model could not be built**: `ProcessedSyncAction.ClientActionId` (added with the website sync feature) was mapped with `HasColumnType("char(36)")`. Because a `string` is an `IEnumerable<char>`, an explicit `char(n)` store type makes EF Core 10 route the property through primitive-collection mapping, where the missing `char` element mapping throws inside `RelationalTypeMappingSource.FindCollectionMapping` and aborts model finalization. Since `DALService.GetContext()` builds `NRDbContext` with the MySQL provider, this broke the API, BackgroundJobs and ConsoleClient on their first DB access — not just tests. Now expressed as `HasMaxLength(36).IsFixedLength()`, which resolves to the **same `char(36)` column** (no schema change) without tripping the collection path. Reproduced against EF Core 10.0.7 and 10.0.11 and Pomelo 10.0.0-rtm.1 and rtm.3 — no version upgrade avoids it, so the mapping had to change. The model snapshot and the `AddProcessedSyncActions` designer model were updated to match, restoring `dotnet ef` tooling. `Guid` properties mapped to `char(36)` are unaffected.
- **`ServerServices.Tests` could not construct `IncidentsService`**: the service took an `IIrpAutomationService` constructor dependency that was never registered in the test DI container, so all four `IncidentsServiceTest` cases failed at construction. Registered it in `ServerServices.Tests.DI.ServiceRegistration` alongside the other incident services, matching `InMemoryServiceTestBase`.
- **High-severity DoS vulnerabilities in the transitive `System.Security.Cryptography.Xml` dependency**: crafted encrypted XML could cause uncontrolled resource consumption (GHSA-8q5v-6pqq-x66h / CVE-2026-50525 and GHSA-cvvh-rhrc-wg4q / CVE-2026-47302, both CVSS 7.5, fixed upstream in 10.0.10). Bumped the existing transitive pins from 10.0.7 to 10.0.11 in `API.Tests` and `ServerServices.Tests`, and from 10.0.8 to 10.0.11 in the Nuke `build` project.
- **High-severity memory-corruption vulnerability in the SQLite native library used by the website's local database** (GHSA-2m69-gcr7-jv3q / CVE-2025-6965, CVSS 7.2): EF Core Sqlite 10.0.7 pulls `SQLitePCLRaw` 2.1.11, which bundles a SQLite older than the 3.50.2 fix. Added a transitive pin for `SQLitePCLRaw.bundle_e_sqlite3` 2.1.13 (bundles SQLite 3.53.3) in `WebSiteData`; it flows to `WebSite` via the project reference. Pinning the bundle rather than just the native lib keeps the native library, providers and core in lockstep.
- **High-severity path-traversal vulnerability in `SSH.NET`** (GHSA-q939-rpr3-3284 / CVE-2026-48798, CVSS 7.1): `ScpClient`'s recursive download did not validate server-supplied filenames, so a malicious SCP server could write outside the target directory. Bumped `Testcontainers.MariaDb` from 4.6.0 to 4.14.0 in `DAL.IntegrationTests`, which depends on the fixed `SSH.NET` 2026.0.0, and moved the container image to the non-obsolete `MariaDbBuilder("mariadb:10.11")` constructor form the newer version requires.



## [2.14.2] - 2026-06-25

This release includes new features and improvements.

### Added

### Changed

### Fixed

- **Operation buttons with text labels rendered as clipped squares**: the `Button.operation` style hard-coded a fixed 25×25 size, which is correct for icon-only buttons but clipped buttons that carry a label — the System Configurations *Save* button showed a single glyph instead of "Salvar", and the Users window Save / Change Password / Add Face / Save Profile / Save Team buttons collapsed to a bare icon with no caption. Changed the fixed `Width`/`Height` to `MinWidth`/`MinHeight` so labeled buttons grow to fit their content while icon-only buttons keep their 25×25 size. (`src/GUIClient/Styles/WindowStyles.axaml`)



## [2.14.1] - 2026-06-25

This release includes new features and improvements.

### Added

### Changed

### Fixed

- **Admin window navigation icons rendered as clipped slivers**: the `Button.navigation` style (top-right Users/Devices/Configuration/Plugins toolbar in the Admin window) forced a 20×20 size while keeping the Fluent theme's default button padding and never sizing the icon, so the 24px `MaterialIcon` was squeezed into a near-zero content area and showed as a thin vertical bar. Zeroed the padding, centered the content, and sized the nav-button icon to 16×16 — mirroring the working `subButton` pattern. (`src/GUIClient/Styles/WindowStyles.axaml`)



## [2.14.0] - 2026-06-18

This release includes new features and improvements.

### Added
- **Website decoupled from the main database via signed periodic sync**: the public WebSite no longer connects to MySQL/MariaDB. It now uses a local SQLite store and exposes signed `/sync` endpoints; the server (BackgroundJobs/Hangfire) periodically pushes the display data the site needs and pulls back visitor actions (fix reports, comments, password changes, link deletes, IRP task outcomes) to apply them via the existing services. Authentication uses ECDSA P-256 request signatures with one-time (TOFU) public-key enrollment. (`SyncContracts`, `WebSiteData`, `WebSite/Controllers/SyncController`, `ServerServices` `SyncKeyService`/`SyncClient`/`SyncPushBuilder`/`SyncIngestService`, `BackgroundJobs` `SyncBulkJob`/`SyncFastJob`)
- **`netrisk-console keys` and `website` commands**: `keys create`/`rotate`/`show` manage the server's sync signing keypair (persisted under the server app-data folder); `website enroll --url` installs the public key on a website (TOFU). (`ConsoleClient`)
- **Configurable website sync intervals in the GUI**: System Configurations now has Website URL, bulk sync interval (default 60 min) and fast-lane interval (default 2 min) for the security-sensitive path (password-reset links, password changes). (`ConfigurationView`, `ConfigurationsController` `WebsiteSync`)
- **`processed_sync_actions` table** (db_version 75): idempotency ledger so website-originated actions apply exactly once under at-least-once delivery.

### Changed
- **`IIncidentResponsePlansService.ChangeExecutionTaskSatusByIdAsync`** gained an overload taking the visitor's action time, so a task execution's duration reflects when the user acted rather than the (later) sync-apply time.

### Fixed



## [2.13.4] - 2026-06-18

This release includes new features and improvements.

### Added
- **Assessment template import now carries answer options**: the JSON and Excel import formats support per-question answer options, and the importer persists them. JSON gains an `Answers` array (`Answer`, `Order`, `RiskScore`, `SubmitRisk`, `RiskSubject`) per question; Excel gains an optional **Answers** column (pipe-separated options). The import preview now also reports the answer-option count. (`ImportsService`, `AssessmentImportPreview`)

### Changed
- **Bundled NIST CSF 2.0 and ISO/IEC 27001:2022 starter templates now ship with answer options**: each question carries an implementation-status scale (Not / Partially / Largely / Fully implemented, Not applicable) so an imported assessment is immediately answerable in the run viewer instead of showing empty dropdowns.

### Fixed
- **NIST and ISO assessment imports were not bringing the answers**: imported assessments had empty answer dropdowns in the Assessment Run Viewer because `ImportsService` only persisted questions and the bundled templates contained no answer options. The importer now persists answer options and the templates include them. (`ImportsService.PersistAsync`, `nist-csf-2.0.json`, `iso-27001-2022-annex-a.json`)



## [2.13.3] - 2026-06-18

This release includes new features and improvements.

### Added

### Changed
- **Editing an assessment execution now uses the paged run viewer too (GUIClient)**: the Edit flow still showed the old flat grid of all questions with inline answer combo-boxes (the last place the non-paged layout survived). Editing is now consistent with creating/answering — a slim metadata step (Entity / Host / Comments) followed by the same paged **Assessment Run Viewer** (page-by-page navigation, progress bar, auto-saved drafts, and Submit/Enviar on the Review page). The in-dialog question grid, its Commit button and the now-dead answer-selection plumbing were removed. (`AssessmentRunDialog.axaml`, `AssessmentRunDialogViewModel`, `AssessmentsRunsListViewModel`)

### Fixed



## [2.13.2] - 2026-06-18

### Added
- **Questionnaire preview in the assessment builder (GUIClient)**: a **Pré-visualizar / Preview** button in the builder toolbar opens the paged run viewer in a read/answer-only preview mode, so authors can see exactly how the questionnaire will render to a respondent (pages, explanations, answer options, conditional show/hide) without creating a real execution or persisting anything. (`AssessmentBuilderView.axaml`, `AssessmentBuilderViewModel`, `AssessmentRunViewerParameter`, `AssessmentRunViewerViewModel`)

### Changed
- **New assessment executions now use the paged run viewer (GUIClient)**: creating a new execution previously used a single flat grid of all questions. The flow is now a slim metadata step (Entity / Host / Comments) that creates the run, followed by the same paged **Assessment Run Viewer** used for viewing/answering — page-by-page navigation, progress bar, auto-saved drafts and a **Submit (Enviar)** action on the Review page that commits the run and creates vulnerabilities from high-risk answers. The flat question grid now appears only when editing an existing run. (`AssessmentRunDialog.axaml`, `AssessmentRunDialogViewModel`, `AssessmentRunViewer.axaml`, `AssessmentRunViewerViewModel`, `AssessmentsRunsListViewModel`)
- **Redesigned assessment questionnaire builder (GUIClient)**: the Questions tab's grid + modal-dialog authoring flow was replaced with an inline, single-column **card canvas** modeled on modern form builders (Google Forms / Jotform). Each question is a card showing a page badge and indicators; clicking **Edit** expands an in-place editor — no modal — with question text, **Page**/**Order**, a rich-text **Explanation** field with a live Markdown preview pane, inline **answer options** (text + risk score + subject, add/remove), and a structured **show/hide rule** ("Show this question only if [question] [equals / is one of / is answered] [value]") built with dropdowns instead of raw JSON. Cards are grouped/ordered by page with **move up/down** reordering, and there are **Add question** / **Add page** actions. This makes the multi-page, conditional, rich-text capabilities authorable directly (previously only reachable via import). (`AssessmentBuilderViewModel`, `AssessmentQuestionCardViewModel`, `AssessmentAnswerEditViewModel`, `AssessmentBuilderView.axaml`, `AssessmentView.axaml`)

### Fixed
- **User Info dialog showed a clipped logout button and a stale version (GUIClient)**: the "Logout and Quit / Descontectar e Fechar" button reused the fixed 25×25 icon-only `operation` style, so its label was clipped to a single character; it is now an auto-sizing labelled button. (`UserInfo.axaml`)
- **Product version is now a single source of truth and bumps automatically (build)**: `AssemblyVersion`/`FileVersion` were hardcoded (and drifted to `2.4.5`) in all 16 project files, so a plain `dotnet run` reported the wrong version in the User Info dialog. The version now lives once in `src/Directory.Build.props` and every project inherits it. The Nuke `Bump`/`BumpMajor`/`BumpMinor`/`BumpPatch` targets rewrite that single file, and the changelog bump now also recognises the `[NEXT]` unreleased placeholder (previously it only matched a numeric `[x.y.z]`, which is why releases had to be hand-edited and left the project versions behind). (`src/Directory.Build.props`, all `*.csproj`, `build/Build.cs`)
- **Assessment builder editor layout fixes (GUIClient)**: the inline "Add answer option" button reused the fixed-width icon-only `subButton` style, so its label was clipped to "+ A"; it is now an auto-sizing labelled button. The answer-option **Risk** numeric field sat in a too-narrow column where its value was hidden behind the spinner arrows; the column was widened and the answer rows now have **Answer / Risk / Subject** column headers. (`AssessmentBuilderView.axaml`, `AssessmentQuestionCardViewModel`)
- **Assessment pages, order and rich-text explanation are now editable when authoring questions (GUIClient)**: previously `PageNumber`, `Order` and `ExplanationMarkdown` could only be set by importing a template, so manually-created questions all landed on page 1 and the multi-page experience only appeared for imported assessments. The Add/Edit Question dialog now has **Page**, **Order** and **Explanation (Markdown)** fields, the Questions list shows a **Page** column and is ordered by page then order, and the run viewer ("application") consequently renders the real page structure. (`AssessmentQuestionViewModel`, `AssessmentQuestionView.axaml`, `AssessmentViewModel`, `AssessmentView.axaml`)

## [2.13.1] - 2026-06-17

### Fixed
- **`ServerServices.Tests` no longer fails to compile**: `ServiceBehaviorInMemoryTest` constructed `ReportsService` with the old three-argument signature after the QuestPDF rendering dependency was added; it now resolves the already-registered `IQuestPdfRenderingService` from the test DI container, so the whole test project (including the assessment dry-run import tests) builds and runs again. (`ServiceBehaviorInMemoryTest`)

## [2.13.0] - 2026-06-17

### Added
- **Interactive paged assessment-run viewer (GUIClient)** — completes Milestone 2.2: opening a run now launches a dedicated viewer with a left-rail page list (per-page completion state), previous/next navigation, and a final review page that lists unanswered required questions with jump-to-page links. Each question renders its rich-text `ExplanationMarkdown` help (via a new lightweight `MarkdownPresenter` control), nested sub-questions are indented, and answers are picked from the question's predefined options. Conditional show/hide is enforced server-side — the viewer fetches each page's visible questions through `GET /Assessments/runs/{runId}/pages/{pageNumber}/questions` and re-evaluates after every save. Draft answers auto-save with a ~2s debounce (`PATCH /Assessments/runs/{runId}/answers`), show a "saved at HH:mm" indicator, drive a live progress bar, and resume at the last page on reopen (`GET …/answers/draft`). Submitted runs open read-only. (`AssessmentRunViewerViewModel`, `AssessmentRunQuestionViewModel`, `AssessmentRunPageViewModel`, `AssessmentRunViewer.axaml`, `MarkdownPresenter`)
- **Assessment template import dialog with dry-run validation (GUIClient + server)**: an "Import template" button on the Assessments view opens a dialog to pick a JSON or Excel (`.xlsx`) template. The dialog **dry-runs first** — it calls a new `POST /Imports/assessment/preview` endpoint that validates the file and returns a summary (page/question counts, warnings, and row-level errors) **without writing anything**; the Import button stays disabled until the preview is valid. Invalid files import nothing and show row-level reasons. On confirm, the same file is committed via `POST /Imports/assessment`. (`AssessmentImportDialogViewModel`, `AssessmentImportDialog.axaml`, `ImportsController.PreviewAssessment`, `ImportsService`, `AssessmentImportPreview`)
- **Bundled assessment starter packs (GUIClient)**: the import dialog offers one-click **NIST CSF 2.0** and **ISO/IEC 27001:2022 Annex A** question sets (paged, with rich-text explanations), shipped as Avalonia assets under `Assets/AssessmentTemplates/`. Questions are paraphrased from the control outcomes (not reproduced verbatim) and serve as scaffolds — answer options are added afterward in the Questions tab. (`nist-csf-2.0.json`, `iso-27001-2022-annex-a.json`)
- **Dry-run validation in the import service (server)**: `IImportsService` gained `PreviewAssessmentFromJsonAsync`/`PreviewAssessmentFromExcelAsync`; parsing/validation is now shared between preview and commit, and committing an invalid template throws before any DB write (so invalid files import nothing). Covered by new `ImportsServiceInMemoryTest` cases. (`ImportsService`, `IImportsService`)
- **ClientServices REST methods for the assessment workflow**: `GetVisibleQuestionsForPageAsync`, `GetDraftAnswersAsync`, `SaveDraftAnswerAsync`, `PreviewTemplateAsync` and `ImportTemplateAsync` were added to `IAssessmentsService`/`AssessmentsRestService` to back the viewer and import dialog. (`AssessmentsRestService`)
- **File reports can now be generated from report templates (GUIClient + server)**: the "Create Report" dialog's report-type dropdown now lists every report template alongside the two built-in reports, so a template-based PDF can be produced as a regular file report (not only as a scheduled email export). Picking a template creates a report whose parameters carry the template id; the server renders the latest template version through the QuestPDF engine (same data source as scheduled exports) and stores the resulting PDF. (`CreateReportDialogViewModel`, `ReportTypeOption`, `ReportDialogResult`, `FileReportsViewModel`, `ReportParameters`, `ReportsService`)

### Changed
- **"Create Report" dialog restyled to the standard dialog visual identity (GUIClient)**: centered content, centered button bar with `IsDefault` on Create, a named window and consistent margins — matching the other edit dialogs (e.g. `EditEntityDialog`) instead of its bespoke left-aligned layout. (`CreateReportDialog.axaml`)

## [2.12.8] - 2026-06-17

### Changed
- **Report Template / Schedule Manager windows now follow the standard master/detail schema (GUIClient)**: the two manager windows were rebuilt to use the same layout and styling as the rest of the app (e.g. `IncidentsView`) instead of their bespoke schema — a `header`/`header2` title and section headers, a bottom control bar of `subButton`/`type2`/`type3` action buttons (Create/Update/Test/Delete) in place of the top `toolbar` border, a `GridSplitter` between list and detail, a `form_label` + `form_text2`/`form_long_text` detail grid (guarded by selection), dates rendered through `DateToFormatedStringConverter`, and the standard `footer`. (`ReportTemplateManagerWindow.axaml`, `ReportScheduleManagerWindow.axaml`)

### Fixed
- **Nullable-safety in the report manager view-models (GUIClient)**: `SelectedTemplate`/`SelectedSchedule` are now nullable and the Update/Delete/Test commands no-op when nothing is selected, avoiding a null-dereference on an empty selection. (`ReportTemplateManagerViewModel`, `ReportScheduleManagerViewModel`)
- **Deprecated `Watermark` replaced with `PlaceholderText` on `TextBox` (GUIClient)**: in the Edit Report Schedule dialog and the Risks panel filter. Dialog-result DTOs for report template/schedule editing now default their string properties to `string.Empty`. (`EditReportScheduleDialog.axaml`, `RisksPanelView.axaml`, `EditReportTemplateDialogResult`, `EditReportScheduleDialogResult`)

## [2.12.7] - 2026-06-17

### Fixed
- **Could not select a version in the Edit Report Schedule dialog (GUIClient)**: the dialog populates the "Versão" dropdown from the selected template's `Versions` navigation collection, but `GET /ReportTemplates` only eager-loaded `Owner`, so every template arrived with an empty `Versions` collection and the dropdown was always empty (leaving Save disabled). The endpoint now `.Include(t => t.Versions)` like `GetById` already did. (`ReportTemplatesController.GetAll`)

## [2.12.6] - 2026-06-17

### Fixed
- **Creating a child entity type (e.g. `organizationUnit`) failed with a server 500 instead of validation (GUIClient)**: definitions that require a parent (a mandatory property whose default value is the `"Parent"` sentinel) were submitted without a parent, and the server rejected them with `Parent is required` surfaced only as a generic `InternalServerError`. The add-entity flow now validates up front and shows a clear "select a parent entity" message (new `ParentRequiredMSG` localization) instead of committing.
- **Assessment run could not be saved — "Could not parse entity id from selection:" (GUIClient)**: the Entity `AutoCompleteBox` in the assessment-run dialog was missing its `SelectedItem` binding (the Host box had one), so the selected entity never reached `SelectedEntityName`. Saving then failed to parse the (empty) entity. Added `SelectedItem="{Binding SelectedEntityName}"`.
- **Opening dialogs crashed with "No service for type … has been registered" (GUIClient)**: a startup refactor switched `DialogService` to resolve dialog view-models from the DI container (`Program.ServiceProvider.GetRequiredService`) instead of instantiating them reflectively, but the dialog view-models were never registered. This crashed core flows such as **adding an entity** (`EditEntityDialogViewModel`) and the Reports **"+"** button (`CreateReportDialogViewModel`), among others. `GeneralServicesBootstrapper` now registers **every** concrete `DialogViewModelBase<>`-derived view-model by reflection, so all dialogs resolve (and future ones are covered automatically).
- **Report Template / Schedule Manager windows didn't follow the platform visual identity (GUIClient)**: the two manager windows rendered raw, unstyled Avalonia controls (plain grey buttons, no theming) against the dark app. They now match the rest of the GUI — a themed toolbar with Material icon buttons and tooltips, styled section headers (`header`/`header2`), a labelled detail panel (`label`/`formData`), and a footer bar. (`ReportTemplateManagerWindow.axaml`, `ReportScheduleManagerWindow.axaml`)

## [2.12.5] - 2026-06-17

### Added
- **Report-template designer (GUIClient)**: the template editor is no longer a raw-JSON form. It now has a structured section editor (add / remove / reorder Title, Text and Table sections), branding controls (primary/secondary color with live swatches, font, and logo upload), a **"New from preset"** picker shipping three built-in starters (Executive Risk Summary, Vulnerability Posture, Incident Review), a **"Save as copy"** action, and a **live rendered PDF preview** pane. Preview is served by a new `POST /ReportTemplates/preview` endpoint that renders the first page to a PNG with sample data via `QuestPdfRenderingService.RenderPreviewImageAsync` (exposed client-side through `IReportTemplatesService.RenderPreviewAsync`).
- **Scheduled-export configuration screen (GUIClient)**: the schedule editor replaces the raw cron string and recipients-JSON textboxes with a frequency builder (Daily / Weekly / Monthly + time + day + timezone, compiled to/parsed from a 5-field cron) and a recipient-list editor. The schedule manager list now surfaces each schedule's **last run time and status**, and a test run refreshes that status.
- **Export actions on the Reports views (GUIClient)**: the Risk Review table and the Risks-vs-Costs, Impact-vs-Probability, Entities-Risks and Vulnerabilities-by-Time charts gained an **Export** button. Export is client-side ("what you see is what you export") to CSV (UTF-8 BOM, formula-injection-escaped) and typed Excel (ClosedXML) via the new `Tools.GridDataExporter` helper.

## [2.12.4] - 2026-06-17

### Changed
- **GUIClient export controls**: replaced the separate PDF/CSV/Excel toolbar buttons on the Risks, Vulnerabilities, Hosts, and Incidents views with a single **Export** button that opens a modal dialog to pick the format. The export icon buttons also now follow the standard view toolbar look-and-feel (previously the Incidents/Hosts export buttons rendered as default unstyled buttons). Format selection lives in the shared `Tools.ExportFileSaver.PickFormatAsync` helper.

### Fixed
- **Unreadable PDF exports with many columns**: the default report layout dumped every entity property into a portrait A4 grid, squeezing ~28 columns into slivers that wrapped one character at a time. PDF reports now render in **landscape**, column headers are humanized (`ReportedByEntity` → `Reported By Entity`) so they wrap on word boundaries, and when a report has more columns than fit a readable grid (> 9) it automatically switches to a per-record **card layout** (label/value pairs, two per row) instead of an unreadable wide table. Narrow, column-selected templates keep the grid. (`QuestPdfRenderingService`)
- **GUIClient crash when saving with a malformed entity/host selection**: clicking **Save** in the assessment-run dialog threw `IndexOutOfRangeException` (crashing the whole app) when the entity field didn't contain the expected `Name (id)` format. Hardened the `Name (id)` parsing behind a shared, exception-free `Tools.String.LabelIdParser` helper and applied it across all affected GUI editors (assessment run, edit vulnerability, edit risk, entities-risks report, entity form) — invalid selections now log and abort gracefully instead of crashing, and names that themselves contain parentheses are parsed correctly.

## [2.12.3] - 2026-06-17

### Added
- Implemented the GUI for the Advanced Reporting Engine, including:
  - A report-template designer to create, update, and delete report templates.
  - A scheduled-export configuration screen to manage scheduled reports.
  - PDF, CSV, and Excel export actions on the Risks, Vulnerabilities, Hosts, and Incidents views.

## [2.12.2] - 2026-06-16

### Fixed
- **GUIClient assessment question editor**: corrected the window layout (fixed oversized/empty window, stretched the question box and reworked the answer-edit row so inputs and action buttons align), added hover tooltips to all answer/question buttons, and made **Guardar** commit an answer still being edited in the side fields before saving — previously that in-progress edit was silently discarded.
- **GUIClient assessment questions grid**: top-aligned the `ID` and `Ações` columns so their cells line up (the question text was bottom-aligned while the ID was centered).
- **GUIClient incident response plan window**: the per-attachment Download/Delete buttons rendered as blank squares because their icons had no explicit size inside the small buttons — sized the icons, enlarged the buttons, and added Download/Delete/Add tooltips.
- **GUIClient mitigation and management-review editors**: fixed both window layouts — replaced the contradictory `SizeToContent.WidthAndHeight` + fixed size with height-to-content sizing, stretched the right-hand text fields so they no longer overflow the window edge, removed the dead space at the bottom, and made the Save/Cancel buttons span a visible bottom row.

## [2.12.1] - 2026-06-16

### Fixed
- Authentication crashed for every user (`Table 'netrisk.user_entity_roles' doesn't exist`) because the multi-entity scoped roles feature shipped in 2.11.0 without its database migration. Added the missing migration `AddUserEntityRoles` and the corresponding numbered SQL (`DB/Structure/74.sql` + `DB/Data/74.sql`, `targetVersion` → 74), which also creates the other drifted tables/columns introduced alongside it (Reports redesign, IRP templates, assessment-run answers and `entity_id` scoping columns).

## [2.12.0] - 2026-06-15

### Added
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.4 (Incident Response Automation - IRP)**: Implemented customizable Incident Response Plan (IRP) templates and automated task compilation/assignee notifications matching SOAR playbooks.
  - Created `IrpTemplate` and `IrpTemplateTask` database models under `DAL`, mapped via Fluent API configurations in `NRDbContext` with cascade deletion rules.
  - Implemented the `IrpAutomationService` workflow matching engine to automatically instantiate IRPs and tasks from blueprints when a matching incident is created.
  - Added support for dynamic relative due date offsets (e.g. T+4h) and human-in-the-loop task approval gates (`requires_confirmation` status proposed).
  - Integrated the automation trigger directly inside the `IncidentsService.CreateAsync` pipeline with non-conflicting DbContext scoping.
  - Created the REST-compliant `IrpTemplatesController` exposing `/IrpTemplates` CRUD endpoints.
  - Added full test coverage in `IrpAutomationServiceInMemoryTest` achieving 100% success.

## [2.11.0] - 2026-06-15

### Added
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.3 (Multi-Entity & Multi-Tenant Support)**: Implemented data segregation by "Business Entity" and enforced role-based scoped access (RBAC) across assets, risks, and vulnerabilities.
  - Added `EntityId` FKs and navigations to core entities `Risk`, `Host`, `Incident`, and `Assessment` under `DAL` (where `Vulnerability` already has `EntityId`), mapped via Fluent API configurations in `NRDbContext`.
  - Created the `UserEntityRole` model to link users, entities, and roles, supporting active audit soft-deletion (`revoked_at` column).
  - Extended the authentication handlers `JwtAuthenticationHandler` and `BasicAuthenticationHandler` to query active user-entity assignments and inject them as `entity_id` and `scope` claims.
  - Developed the generic static helper `ApplyEntityScope` under `ServerServices` to dynamically filter queryable datasets based on user claims.
  - Integrated dynamic scoping directly into `RisksService` (including `GetAllAsync` and `GetUserRisks` sync query) to restrict dataset visibility at the service layer.
  - Created `UserAccessController` to manage user-entity-role assignments (Get, Assign, Revoke).
  - Added full integration test coverage in `MultiEntityScopedAccessTest` verifying user-scoped isolation and global admin bypass with 100% success.

## [2.10.0] - 2026-06-15

### Added
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.2 (Enhanced Assessments Workflow)**: Implemented the backend database structures, visibility algorithms, auto-saving logic, and external template parsers for GRC assessments.
  - Extended `AssessmentQuestion` with ParentQuestionId (nesting), PageNumber (pagination), ConditionJson (rules), and ExplanationMarkdown (help text).
  - Extended `AssessmentRun` with ProgressPercentage and CurrentPageIndex.
  - Created the `AssessmentRunAnswer` model under `DAL` to support saving in-progress draft responses.
  - Implemented the on-the-fly conditional evaluation algorithm `GetVisibleQuestionsForPageAsync` inside `AssessmentsService` supporting 'equals', 'notempty', and 'in' logic operators.
  - Implemented `SaveDraftAnswerAsync` to securely upsert in-progress user responses.
  - Developed `ImportsService` supporting template importing from standard JSON files and Excel worksheets (NIST / ISO 27001) using ClosedXML.
  - Created `ImportsController` exposing the `/Imports/assessment` upload endpoint and added REST routes for auto-saving drafts and visibility checks in `AssessmentsController`.
  - Added comprehensive test coverage in `AssessmentsServiceGapInMemoryTest` and `ImportsServiceInMemoryTest` achieving 100% success.

## [2.9.0] - 2026-06-15

### Added
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.1 (Phase 3: Scheduled GRC Reports)**: Implemented scheduled report runs, automated PDF compiles, and email dispatches with PDF attachments.
  - Added the `ReportSchedule` database model under `DAL`, mapped via Fluent API configurations in `NRDbContext` with cascade deletion rules.
  - Implemented the `ScheduledReportJob` background worker under `ServerServices` to generate dynamic PDF summaries of incidents and send attachment-bearing emails via `FluentEmail` in memory.
  - Developed the `ReportSchedulesController` in the `API` project with CRUD endpoints on `/ReportSchedules`, supporting active integration with the **Hangfire** scheduler (`RecurringJob.AddOrUpdate` / `BackgroundJob.Enqueue`).
  - Added full test coverage in `ScheduledReportJobInMemoryTest` using NSubstitute.
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.1 (Phase 2: Customizable Report Templates)**: Implemented the backend database structures, APIs, and fluid QuestPDF rendering engine for dynamic customizable templates.
  - Added `ReportTemplate` and `ReportTemplateVersion` database models under `DAL`, mapped via Fluent API configurations in `NRDbContext` following standard conventions.
  - Implemented the REST-compliant `ReportTemplatesController` with endpoints `GET`, `POST`, `PUT`, `DELETE` on `/ReportTemplates` to manage report templates with versioned layout and branding histories.
  - Introduced the modern **QuestPDF** library (v2026.6.0) under `ServerServices` and integrated the `IQuestPdfRenderingService`/`QuestPdfRenderingService` engine.
  - Configured QuestPDF for dynamic JSON layouts supporting logos, colors, customizable typography, title sections, body text, and complex table layouts.
  - Integrated QuestPDF directly into the main `ExportService` so standard PDF exports automatically use the brand-new, ultra-modern templates.
  - Added 100% test coverage in `QuestPdfRenderingServiceInMemoryTest`.
- **Track 2 (GRC Core & Reporting Engine) — Milestone 2.1 (Phase 1: Core Export Service)**: Implemented the backend server-side export engine including the `IExportService` contract and its concrete implementation `ExportService`.
  - Added support for generating CSV files safely against Formula Injection (CWE-1236) using UTF-8 BOM.
  - Added support for generating Excel (XLSX) spreadsheets using ClosedXML with strongly-typed columns and custom formatting.
  - Added a placeholder PDF table exporter using PDFsharp/MigraDoc with global FontResolver integration.
  - Implemented the generic `ExportController` with endpoint `GET /Export/{format}` allowing Sieve-filtered export of major entities (`Risk`, `Vulnerability`, `Host`, `Incident`) without pagination limits.
  - Added full test coverage in `ExportServiceInMemoryTest` achieving 100% success rate.

## [2.8.0] - 2026-06-12

### Changed
- **Track 6 — Milestone 6.4 Phase 6b (drop deprecated tables), `db_version` 73**: **DESTRUCTIVE** removal of everything deprecated in Phase 6a after the recorded observation window — drops all 23 `zz_deprecated_*` tables and finally the orphan columns `risks.regulation`/`risks.project_id`. The legacy `risks.status` text column is **intentionally kept** (its Phase 5 `status_id` replacement must coexist for one release before removal — not in this milestone). Gated by the tool: requires the `6a` Success entry in `schema_upgrade_log` aged ≥ the manifest's `observationDays` **and** explicit `--yes`; the automatic pre-phase backup is the only recovery path. The 23 entity classes are deleted from `DAL`. EF migration `Track6Phase6bDropDeprecatedTables` (its `Down()` is irreversible by design) + numbered SQL `Structure/Data/73.sql` (drops under `FOREIGN_KEY_CHECKS=0` since deprecated tables retained their inter-table FKs through the rename); applied via `database upgrade-schema --phase 6b --yes`. Verified end-to-end on MariaDB (every `zz_deprecated_*` gone, orphan columns dropped, `status` retained, `--yes`/observation gate enforced).
- **Track 6 — Milestone 6.4 Phase 6a (deprecate dead tables), `db_version` 72**: deprecated the 23 zero-reference tables (functional: `contributing_risks_impact`/`...likelihood`, `questionnaire_pending_risks`, `residual_risk_scoring_history`, `framework_control_test_results_to_risks`, `framework_control_type_mappings`, `permission_to_permission_group`, `mitigation_accept_users`, `risk_to_additional_stakeholder`/`...location`/`...technology`, `framework_control_test_comments`/`...audits`, `failed_login_attempts`, `user_pass_history`; enumeration: `control_phase`/`control_type`, `file_type_extensions`, `regulation`, `risk_function`, `test_status`, `threat_catalog`/`threat_grouping`) by **unmapping them from EF** (DbSets + `OnModelCreating` configs removed) and **RENAMING them to `zz_deprecated_*`** — reversible, data preserved, forgotten access fails loud. Also unmapped (no DDL) the orphan columns `risks.regulation`/`risks.project_id` (no live referent, zero code use), physically dropped in 6b. Security note: `failed_login_attempts`/`user_pass_history` confirmed unused (no login-lockout / password-reuse logic; `UserPassReuseHistory` is the live one and is **not** removed). EF migration `Track6Phase6aDeprecateDeadTables` (hand-written to RENAME, mirroring the numbered SQL, rather than EF's scaffolded `DropTable`) + numbered SQL `Structure/Data/72.sql`; applied via `database upgrade-schema --phase 6a`. Manifest `removalCandidates`/census corrected to the live snake_case table names (the PascalCase entity names never matched a case-sensitive DB). Verified end-to-end on MariaDB (≥23 tables renamed, originals gone, seeded row preserved through the rename, orphan columns retained for 6b).
- **Track 6 — Milestone 6.4 Phase 5 (status type standardization), `db_version` 71**: added `risks.status_id` (`int`) as the type-safe replacement for the free-text `risks.status` and backfilled it from the known status strings (`New`=0, `Mitigation Planned`=1, `Mgmt Reviewed`=2, `Closed`=3; unmapped legacy values left `NULL`). **Create-copy-coexist**: the legacy `status` column is retained — the old column is never dropped in the same release that introduces its replacement. The C# `Risk.StatusId` maps to a new `DAL.Enums.RiskStatus` (mirrors `Model.Risks.RiskStatus`, since `Model`→`DAL` is the project dependency direction so `DAL` cannot reference `Model`) via explicit `HasConversion<int>()`; `BiometricTransaction.TransactionResult` also gained an explicit `HasConversion<int>()` (model-only — its column is already `int`). EF migration `Track6Phase5StatusTypeStandardization` + numbered SQL `Structure/Data/71.sql`; applied via `database upgrade-schema --phase 5`. *(The temporal `ON UPDATE CURRENT_TIMESTAMP` columns were intentionally left as-is — they are audit timestamps that should keep auto-updating.)* Verified end-to-end on MariaDB (column is `int`, backfill mapping correct, unmapped value `NULL`, legacy `status` retained).
- **Track 6 — Milestone 6.3 Phase 4 (indexing + BLOB→text), `db_version` 70**: added hot-path indexes justified by the real Sieve filters/sorts (`ApplicationSieveProcessor`) and the risk listing query — `idx_vulnerabilities_first_detection`/`idx_vulnerabilities_last_detection`, `idx_hosts_status`/`idx_hosts_registration_date`, the composite `idx_risks_status_submission_date`, and `idx_user_email` — and dropped the redundant `UNIQUE` `id` index on `framework_control_tests` (already covered by the PK). Converted the text-bearing `blob` columns to proper text types and changed their C# properties from `byte[]` to `string`: `user.email` → `varchar(255)` (app-written UTF-8, direct conversion; MapsterConfiguration/`AuthenticationController`/`EmailController`/`UserCommand` simplified — the email map is now an identity), and `frameworks.name`/`description`, `framework_controls.long_name`/`description`/`supplemental_guidance`, `permissions.description` → `TEXT`. **Encoding note:** the legacy framework/permission BLOBs hold Windows-1252/latin1 seed bytes (they contain bytes like `0x94` that are *not* valid UTF-8 and the app never writes them), so they convert via a `latin1`→`utf8mb4` round-trip (lossless transcoding) rather than a direct `MODIFY` that would error; validate on a production clone first. `permissions.description` is returned raw by `GET /Users/permissions`, so that JSON field changes from base64 to plain text. EF migration `Track6Phase4IndexingBlobText` keeps the snapshot in sync; the numbered SQL `Structure/Data/70.sql` uses the latin1 round-trip and omits EF's incidental `risk_scoring` index rename (the real schema already names that index `id`). Applied via `database upgrade-schema --phase 4`. Verified end-to-end on MariaDB (cp1252 bytes transcode to the correct Unicode, app UTF-8 preserved, indexes present and in `EXPLAIN` possible_keys, redundant index gone).
- **Track 6 — Milestone 6.3 Phase 3 (relationships), `db_version` 69**: promoted the orphan correlation columns to navigable, indexed foreign keys to `user(value)` — `risks.owner`/`manager`/`submitted_by` (`fk_risks_*`), `framework_controls.control_owner` (`fk_framework_controls_control_owner`), `framework_control_tests.tester` (`fk_framework_control_tests_tester`), all made nullable with **`ON DELETE SET NULL`** and EF navigations (`Risk.OwnerUser`/`ManagerUser`/`SubmittedByUser`, `FrameworkControl.ControlOwnerUser`, `FrameworkControlTest.TesterUser`). Added `incidents.reported_by_id` (nullable FK `fk_incidents_reported_by`, `Incident.ReportedByUser`), **keeping the free-text `ReportedBy` column for external reporters** and best-effort backfilling the FK by exact, unambiguous `user.name` match. **Orphan-safe order:** dangling references are logged to a new `schema_upgrade_orphans` audit table *before* being NULLed (the log is the recovery record, since `Down()` cannot un-null), then the constraints are added — applying the constraint against un-cleaned data fails by design. Resolved the `IncidentToIncidentResponsePlan` mapping ambiguity (removed the dead commented block; the join is mapped once via `UsingEntity`). `Risk.ProjectId` has no live `projects` table, so it gains **no FK** and is flagged a Milestone 6.4 removal candidate. EF migration `Track6Phase3Relationships` + numbered SQL `Structure/Data/69.sql` (hand-authored for the orphan log/cleanup/backfill ordering EF can't express); applied via `database upgrade-schema --phase 3`. Verified end-to-end on MariaDB (orphans logged then nulled, valid refs untouched, backfill matches only unique names, `ON DELETE SET NULL` confirmed, FK columns indexed).
- **Track 6 — Milestone 6.2 Phase 1c (collation unification), `db_version` 68**: converted the entire schema to **utf8mb4 / utf8mb4_unicode_ci** — every legacy `utf8mb3` (and `utf8mb4_general_ci`) table, covering all 99 base tables present at `db_version` 67 (including legacy tables not mapped by EF), via `ALTER TABLE … CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci` (table default + all char columns) plus `ALTER DATABASE`. This lets text columns store 4-byte characters (emoji, etc.) that `utf8mb3` silently rejected. EF model collation annotations updated to match; migration `Track6Phase1cCollationUtf8mb4` keeps the snapshot in sync, but the numbered SQL `Structure/Data/68.sql` uses `CONVERT TO` (one statement per table) rather than EF's verbose per-column output. Applied via `database upgrade-schema --phase 1c`. Manifest phases 3–6b renumbered (+1, now `db_version` 69–73). Verified end-to-end on MariaDB: no `utf8mb3` table/column remains, existing data preserved, and a 4-byte emoji round-trips. *This completes the deferred 6.2 collation work; `dotnet-ef` is now pinned to 10.0.9 via a local tool manifest.*
- **Track 6 — Milestone 6.2 Phase 2b (column naming), `db_version` 67**: renamed the last stray PascalCase column `comments.IsAnonymous` → `is_anonymous` (the Phase 1b boolean fix had kept the legacy name). EF migration `Track6Phase2bIsAnonymousColumnRename` + numbered SQL `Structure/Data/67.sql`; applied via `database upgrade-schema --phase 2b`. Manifest phases 3–6b renumbered (+1, now `db_version` 68–72). Verified end-to-end on MariaDB (new column present, old gone, value preserved).
- **Track 6 — Milestone 6.2 Phase 1b (boolean width normalization), `db_version` 66**: normalized the genuine booleans `comments.IsAnonymous` and `framework_controls.deleted` from `tinyint(4)` to `tinyint(1)` (deferred from Phase 1). The C# properties changed from `sbyte` to `bool` (Pomelo maps `bool`↔`tinyint(1)`), along with the `SecurityControlStatistic.Deleted` DTO field and the call sites (`CommentsController`/`CommentsService`/`FixRequestController`/`VulnerabilityFixChatDialogViewModel`/`StatisticsController`). EF migration `Track6Phase1bBooleanNormalization` + numbered SQL `Structure/Data/66.sql`; applied via `database upgrade-schema --phase 1b`. Manifest phases 3–6b renumbered (+1, now `db_version` 67–71). Verified end-to-end on MariaDB (column type → `tinyint(1)`, 0/1 values preserved). *(Note: the `comments` column is still physically named `IsAnonymous` (PascalCase) — a snake_case rename is a separate naming gap, not part of the deferred width fix.)*
- **Track 6 — Milestone 6.2 Phase 2 (naming uniformization), `db_version` 65**: renamed the 8 PascalCase tables to snake_case (`Incidents`→`incidents`, `IncidentResponsePlans`→`incident_response_plans`, `IncidentResponsePlanTasks`/`...Executions`/`...TaskExecutions`, `IncidentToIncidentResponsePlan`→`incident_to_incident_response_plan`, `FaceIDUsers`→`face_id_users`, `BiometricTransaction`→`biometric_transactions`, `FixRequest`→`fix_requests`) and the hybrid columns (`vulnerabilities_to_actions.actionId`/`vulnerabilityId`→`action_id`/`vulnerability_id`; `reports.creationDate`/`creatorId`/`fileId`→`created_at`/`creator_id`/`file_id`; `hosts.FQDN`/`OS`→`fqdn`/`os`; `messages.Message`→`message`). EF migration `Track6Phase2NamingUniformization` + numbered SQL `Structure/Data/65.sql` (hand-cleaned to drop Pomelo's `DELIMITER`-based PK procedure — the join-table PK is composite, not auto-increment — so `MySqlConnector` can apply it). **RENAME only — no data loss; C# entity/DTO names unchanged** (mapping via `ToTable`/`HasColumnName`). Verified end-to-end against the real legacy schema on MariaDB (renames + row-count/value parity).
- **Track 6 — Milestone 6.2 Phase 1 (safe fixes), `db_version` 64**: renamed the typo indexes (`idx_biometic_id`/`idx_biometic_anchor` → `idx_biometric_transaction_id`/`idx_biometric_transaction_anchor`; `idx_irpt_sequencial`/`idx_irpt_optinal` → `idx_irpt_sequential`/`idx_irpt_optional`) and removed the illegal `0000-00-00` column defaults on `mgmt_reviews.next_review` (default dropped) and `mitigations.last_update` (→ `CURRENT_TIMESTAMP`) — these break MariaDB strict mode. Authored as EF migration `Track6Phase1SafeFixes` + numbered SQL `Structure/Data/64.sql`; applied via `database upgrade-schema --phase 1`. C# entities/DTOs unchanged. Boolean `tinyint(1)` normalization and broad collation unification are **deferred** (they need `sbyte`→`bool` type changes / a per-column survey, beyond Phase 1's rename-only safety). Verified end-to-end against the real legacy schema on MariaDB in `DAL.IntegrationTests`.

### Added
- **Track 6 (Database Uniformization) — 6.1 tooling foundation**: introduced the `schema_upgrade_log` audit table (EF entity + migration `20260611141630_SchemaUpgradeLog`, applied via numbered SQL `db_version` 63) that records every schema-upgrade run, and a data-driven phase manifest (`src/ConsoleClient/DB/SchemaUpgradePhases.yaml`) describing the Track 6 phases (1–6b) with their target `db_version`, census queries, post-apply validations, and destructive-phase gate metadata.
- **Track 6 — `netrisk-console database upgrade-schema` command**: new ConsoleClient operation with `--phase`, `--env`, `--check`, `--dry-run`, `--yes`, and `--output`. `--check` runs read-only pre-flight (connectivity, current-vs-expected `db_version`, phase SQL-file presence, and the destructive `6b` observation-window gate against `schema_upgrade_log`); `--dry-run` prints/writes the exact numbered SQL a phase would apply (both mutate nothing); and a real apply runs the full **backup → census → apply numbered SQL → post-apply validation → audit-log** sequence, aborting before any change if pre-flight fails and refusing destructive/prod runs without `--yes`. Post-apply validations cover index/foreign-key/column-type/table existence and custom scalar checks against `information_schema`. Backed by `ISchemaUpgradeService`/`SchemaUpgradeService`, the pure `SchemaUpgradePlanner`/`SchemaUpgradeManifestLoader`, and `SchemaUpgradeValidator`.
- **Track 6 — `netrisk-console database baseline`**: new ConsoleClient operation (Plan Phase 0) that records the pre-uniformization baseline — current `db_version`, pending EF migrations, model-vs-snapshot divergence (`HasPendingModelChanges`), and a row-count census of the Phase-6 removal candidates (data-driven from the manifest's `removalCandidates`) that recommends `drop` (empty/absent) vs `archive` (has data). Optional `--output` writes a Markdown report. Read-only.
- **Track 6 — `DAL.IntegrationTests` harness**: new test project using Testcontainers (`Testcontainers.MariaDb`) that boots a throwaway MariaDB container to verify the shipped `schema_upgrade_log` DDL, the EF entity round-trip, the full apply orchestration (apply + validate + audit-log, plus the validation-failure path), and the baseline census end-to-end against real MariaDB (NetRisk's production database). Tagged `Category=Integration` (requires Docker; exclude from the fast unit run with `--filter "Category!=Integration"`). Unit coverage for the tool is 31 tests in `ServerServices.Tests`.
- **Track 6 — conventions documented** in [CLAUDE.md](CLAUDE.md): the target schema convention (so new entities are born compliant), how migrations actually reach production (numbered SQL + `db_version`), and the schema-upgrade/baseline tooling. Completes Milestone 6.1 (the operational production-baseline *run* against the live prod DB remains, by nature, an ops step). See [roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md](roadmap/track-6/MILESTONE_6.1_TOOLING_PREPARATION.md).

## [2.7.7] - 2026-06-11

### Changed
- **Test coverage for the data and server-service layers**: added unit tests raising `DAL` coverage to ~99% (excluding generated EF migrations) and `ServerServices` line coverage from ~11% to ~90%. New tests cover the change-auditing pipeline (`AuditableContext`, `Auditing.Base`) and the domain services, with at least one behavior test per service. Introduced an EF Core in-memory test harness (`InMemoryServiceTestBase`) and a `coverage.runsettings` that excludes generated migrations and genuinely-untestable I/O/rendering classes so the reported figure reflects testable logic. No production code changed.

## [2.7.6] - 2026-06-10

### Changed
- **File uploads now stream in chunks instead of a single request**: the GUI client previously POSTed the whole file base64-encoded inside one JSON body to `POST /Files`, so any attachment over ~22 MB exceeded Kestrel's 30 MB request-body limit and the connection was reset (surfaced to the user as a "Broken pipe"). `UploadFileAsync` now requests an upload id, sends the content in 5 MB chunks to `POST /Files/local/chunk`, then calls a new `POST /Files/local/complete` to finalize. This keeps every request small regardless of file size.

### Fixed
- **Chunked uploads were never persisted**: the server's chunk endpoint only reassembled the parts into a `.dat` file on disk and then stopped — it never created the `NrFile` database record, never stored the content (files are persisted as a DB blob), and never associated the file with its incident/risk/plan/task/mitigation, so a chunk-uploaded file was orphaned and never appeared as an attachment. Added `CompleteChunkedUpload` (and the `POST /Files/local/complete` endpoint) which reassembles the chunks, loads the content, persists the record with its entity association via the same path as a single-shot upload, and cleans up the temporary chunk files. The chunk endpoint no longer auto-combines, so finalization is the single authoritative reconciliation step.
- **API request-body limit raised and made configurable**: Kestrel's default 30 MB `MaxRequestBodySize` is now raised to 100 MB and configurable via `Files:MaxRequestBodySizeBytes`, protecting non-chunked endpoints and giving headroom for the chunk-finalize call.

## [2.7.5] - 2026-06-10

### Fixed
- **GUI crash when adding a file to an incident**: the "Add file" button on the Edit Incident window passed the window itself as a `CommandParameter` into `BtFileAddClicked`, a parameterless `ReactiveCommand<Unit, Unit>`. ReactiveUI rejects the type mismatch at execute time (`Command requires parameters of type System.Reactive.Unit, but received parameter of type EditIncidentWindow`), and the unhandled error tore down the app. The stale `CommandParameter` was removed — the handler already gets the window from its `ParentWindow` property.
- **GUI crash when a file upload fails**: file-add handlers awaited `FilesService.UploadFileAsync` with no error handling, so a failed upload (e.g. a dropped connection surfacing as `Broken pipe` / `RestComunicationException`) escaped the `ReactiveCommand` pipeline and crashed the process via ReactiveUI's default exception handler. The upload is now wrapped in a try/catch that logs the error and shows an error dialog (`ErrorUploadingFileMSG`) instead of crashing, across all five upload sites: incidents, risks, incident response plans, IRP tasks, and mitigations.

## [2.7.4] - 2026-06-09

### Fixed
- **macOS x64 GUI cross-publish (`PackageMacGUI`) failing on Apple Silicon with `NU3012`**: the Docker `linux/amd64` cross-publish does a from-scratch NuGet restore inside the container, where the online certificate revocation check flagged the author signatures on the ReactiveUI/Splat packages as revoked, aborting `dotnet publish`. (The host build never hit this because those packages were already restored and cached, so signature verification didn't re-run.) The Docker `dotnet publish` invocation now sets `NUGET_CERT_REVOCATION_MODE=offline` so restore skips the online revocation lookup. The thrown error was also misleading — it only surfaced Docker's image-pull progress because `RunProcess` includes just stderr in the exception message.

## [2.7.3] - 2026-06-09

### Fixed
- **Docker containers failing to start with a misleading `no such file or directory` on `/entrypoint.sh`**: the entrypoint scripts are stored in git as LF, but with no `.gitattributes` a build host configured with `core.autocrlf=true` (Windows) checked them out as CRLF, so `COPY entrypoint-*.sh /entrypoint.sh` baked a `#!/bin/bash\r` shebang into the image. The kernel then tried to exec the interpreter `/bin/bash\r`, which doesn't exist. Added a repository `.gitattributes` that pins line endings (LF for `*.sh`/source/config, CRLF only for Windows `*.bat`/`*.cmd`/`*.ps1`) and renormalized all previously-CRLF-tracked files to LF. As defense in depth, each Dockerfile now strips CRs from the entrypoint (`sed -i 's/\r$//'`) before `chmod`.

## [2.7.2] - 2026-06-09

### Fixed
- **Docker image builds failing during image export (`failed to Lchown ... no such file or directory`)**: every payload image did `COPY <payload> /netrisk` as root and then let puppet recursively re-own `/netrisk` (`file{'/netrisk': recurse => true}`). For the API/BackgroundJobs images this re-chowned the 177 MB `OpenFaceONNX.dll` (and the rest of the payload) into a second large layer, which tripped Docker Desktop's overlayfs/containerd snapshotter when extracting the layer on export. Ownership is now set once at copy time via `COPY --chown=7070:7070` (the numeric uid/gid of the puppet `netrisk` user) across all four Dockerfiles, and the redundant recursive `/netrisk` chown was dropped from the `api` and `backgroundjobs` puppet manifests. This both fixes the export failure and shrinks the images by not duplicating the payload across layers.

## [2.7.1] - 2026-06-08

### Fixed
- **`CreateAllDockerImages` Nuke target failing in `CreateDockerImageWebSite`**: the website image build unconditionally copied the Windows/Linux/macOS GUI installer artifacts into the image, so on hosts where a given platform was not packaged (e.g. the `.dmg` files on Windows) the missing source tripped Nuke's `source.DirectoryExists() || source.FileExists()` assertion and aborted the whole run. Installer copies now go through a `CopyInstallerIfPresent` helper that skips and logs a warning when an artifact is absent, so the image builds with whatever installers the current host produced.

## [2.7.0] - 2026-06-08

### Changed
- **Upgraded `Pomelo.EntityFrameworkCore.MySql` to `10.0.0-rtm.1`** (from `9.0.0`) to align the MySQL EF Core provider with the EF Core 10 packages already in use. The v10 build is sourced from the `uox-netrisk` Cloudsmith feed, which is now wired into the package source mapping.

## [2.6.2] - 2026-06-03

### Fixed
- **Clipped icons in `subButton` toolbars**: the `Button.subButton` style (add/edit/search/reload/delete toolbars on the Entities, Hosts, Incidents, and Risk views) never zeroed its default padding nor sized its child `MaterialIcon`, so the 25×25 button squeezed and clipped the glyph. Added `Padding=0` + centered content alignment and a `Button.subButton > MaterialIcon` rule sizing the icon to 16×16, mirroring the working `detailButton` pattern. Verified live on the Entities view.

## [2.6.1] - 2026-06-03

### Fixed
- **Broken "show search" toolbar icon**: the search toggle button on the Entities, Hosts, Incidents, and Risk views referenced `Kind="SelectSearch"`, which is not a valid Material Design Icons name, so the `MaterialIcon` control rendered fallback glyph text instead of an icon. Changed to `Kind="Magnify"` (the same icon already used by the search-execute buttons).

## [2.6.0] - 2026-06-03

### Added
- **macOS global menu redirection** (Milestone 1.4): a `NativeMenu` mirroring the application menu is attached to `MainWindow`. On Apple Darwin it surfaces in the system global menu bar and the in-window `Menu` is collapsed (bound to a new `IsNotMacOS` flag); on Windows/Linux the in-window menu is used as before.
- **Platform-native window-control alignment** (Milestone 1.4): the navigation bar is inset dynamically (`MainWindowViewModel.NavBarMargin`) so that, once the menu row collapses on macOS, its left-edge content clears the native top-left traffic-light controls. Platform probes consolidated into `Helpers/PlatformInfo`.
- **Keyboard accessibility sweep** (Milestone 1.4):
  - Global `Ctrl+P` opens the reporting/export surface from anywhere in the main window.
  - `Ctrl+S` (save) and `Esc` (dismiss) wired on the Risk and Incident edit windows.
  - Centralised `Esc` (dismiss) and `Ctrl/Cmd+S` (save, via the new `ISaveableDialog` opt-in) for every modal dialog inheriting `DialogWindowBase`.
  - `Ctrl+F` toggles the search panel on the Entities and Incidents views.
  - Logical `TabIndex` ordering plus `IsDefault`/`IsCancel` buttons on the Login window and entity dialog.
- **System tray integration** (Milestone 1.4): `Helpers/TrayIconManager` adds a Windows notification-area icon / macOS menu-bar extra with a quick-status preview (sign-in state and version, refreshed every 15s), an Open/Hide/Exit context menu, and minimise-to-tray behaviour on Windows.

### Fixed
- **macOS notification bell overlapping the traffic-light window controls**: the navigation bar's left inset (`NavBarMargin`, 80px on macOS) was bound on the `NavigationBar` element as a bare `{Binding NavBarMargin}`, which resolved against the control's own `NavigationBarViewModel` instead of the `MainWindowViewModel` that exposes the property — so it silently fell back to a zero margin and the notification bell sat under the native top-left window buttons. Bound the margin explicitly against the MainWindow's DataContext (`#MWindow.((dvm:MainWindowViewModel)DataContext).NavBarMargin`) so the bell clears the controls.

## [2.5.1] - 2026-06-03

### Fixed
- **Widespread broken bindings under compiled bindings**: enabling `AvaloniaUseCompiledBindingsByDefault` (Milestone 1.3) silently broke every `{Binding}` that targeted a non-public view-model member — compiled bindings can only reach public members, whereas the previous reflection bindings reached private ones. This left labels blank, tab headers falling back to the `ViewLocator` ("Not Found: GUIClient.Views.…View"), command buttons inert, and child-VM content panels empty (e.g. the entire `AdminWindow`). Audited all views against their `x:DataType` view-models and promoted the 194 bound members across 26 view-models (plus `UserInfoViewModel`) from `private` to `public`. Verified live: `UserInfo` and `AdminWindow`/`UsersView` now render fully.

## [2.5.0] - 2026-06-03

This release includes new features and improvements.

### Added
- **Compiled bindings enabled globally** (`AvaloniaUseCompiledBindingsByDefault=true` in GUIClient): every view now declares an explicit `x:DataType`, giving compile-time binding validation and faster rendering with a lower RAM footprint. (Milestone 1.3)
- **High-performance virtualizing `TreeDataGrid`** for the dense vulnerability grid, replacing the `DataGrid`. Source/columns are built in code-behind (`FlatTreeDataGridSource<Vulnerability>`) reusing the existing converters and status cell template, with two-way selection sync.
- TreeDataGrid via the `libs/TreeDataGrid.Avalonia` submodule (MIT, .NET-Foundation source ported to Avalonia 12; security-reviewed), since Avalonia 12's official `Avalonia.Controls.TreeDataGrid` package is now commercially licensed
- Explicit `VirtualizingStackPanel` on the primary dense data lists (incidents, hosts, risks, users, notifications) to enforce UI virtualization and guard against accidental regressions
- `RiskScoringPair` record (replaces `Tuple<Risk, RiskScoring>`) so the vulnerability risk panel binds with compiled bindings
- Project docs: `CLAUDE.md`, `ROADMAP.md`, per-feature docs under `docs/features/`, `docs/ui-standard.md`
- Transitive pin for `Tmds.DBus.Protocol` 0.92.0 in GUIClient (addresses GHSA-xrw6-gwf8-vvr9)
- Transitive pin for `System.Security.Cryptography.Xml` 10.0.7 in API.Tests and ServerServices.Tests (addresses GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf)
- UI standard compliance audit (`roadmap/UI_STANDARD_AUDIT.md`) and remediation plan (`roadmap/UI_STANDARD_COMPLIANCE_PLAN.md`)

### Fixed
- **macOS window dragging restored**: the custom title-bar `Menu` stretched the full window width with `ElementRole="User"` (non-draggable), leaving no `TitleBar` surface to grab; set `HorizontalAlignment="Left"` so the menu only occupies its items and the rest of the title-bar row is draggable again.
- **`--environment` argument parsing** in `GUIClient`: now accepts both `--environment=dev` and `--environment dev` forms, guards against a missing value, and corrects the prior bug that validated the wrong variable (plus the "Unkown environment" typo).
- Compile-time binding errors surfaced by enabling compiled bindings (previously silent, failing reflection bindings): added missing `StrActions` (AssessmentViewModel), `StrNotifications` (NavigationBarViewModel), `IsViewOperation`/`IsCreateOperation` (EditIncidentViewModel), and `CanCancel`/`CanClose` (IncidentResponsePlanTaskViewModel); corrected stale `ElementName`/`#name` references in `EditIncidentWindow`, `IncidentResponsePlanTaskWindow`, `EditMgmtReview`, `MainWindow`, and `AssessmentView`; typed the TreeViewItem style bindings in `EntitiesView`

### Changed
- **GUIClient UI compliance pass**: all Avalonia views now conform to the UI standard — hardcoded hex/named Background and Foreground colors removed from layout containers, all dialog/action/navigation buttons carry canonical CSS classes (`dialog1`, `dialog2`, `operation`, `subButton`, `navigation`, etc.), fixed-width inputs replaced with `MinWidth`, navigation buttons carry `Classes="navigation"`, `Classes="dark"` applied to modal windows. Semantic state colors (ProgressRing spinner, notification bell, FaceID status icons) preserved intentionally.

### Changed
- **Avalonia 11.3.11 → 12.0.1** across GUIClient, AvaloniaExtraControls, and the Aura.UI submodule. Trade-offs documented in ROADMAP.md (dev-tools overlay removed, tab drag-reorder removed, SVG assets replaced by Material icons, `SpacedGrid` replaced by native `Grid` spacing).
- ReactiveUI 22.3.1→23.2.1, ReactiveUI.Avalonia 11.3.8→12.0.1, Splat 17→19
- Material.Icons.Avalonia 2.4→3.0, MessageBox.Avalonia 3.x→12.x, Deadpikle.AvaloniaProgressRing 0.10→0.11
- LiveChartsCore family 2.0.0-rc5.4 → 2.1.0-dev-292
- SkiaSharp 3.119.2 → 3.119.3-preview.1.1 (required by Avalonia.Skia 12)
- Spectre.Console 0.51→0.55.2, Spectre.Console.Cli 0.51→0.55.0, Serilog.Sinks.Spectre 0.5→0.6.0 (breaking: `Command.Execute` now takes `CancellationToken`; visibility `protected`)
- Dependency refresh across all projects (patch/minor updates):
  - Serilog 4.3.0→4.3.1, Serilog.Sinks.Console 6.0.0→6.1.1, Serilog.Extensions.Hosting 9→10, Serilog.Extensions.Logging 9→10
  - Microsoft.Extensions.* 10.0.2→10.0.7 (Hosting, Localization, Configuration.Abstractions, DependencyInjection, DependencyInjection.Abstractions, DependencyModel)
  - Microsoft.AspNetCore.Authentication.JwtBearer 10.0.2→10.0.7, SystemWebAdapters 2.2.1→2.3.0
  - System.IdentityModel.Tokens.Jwt 8.15.0→8.17.0, System.Drawing.Common 10.0.2→10.0.7
  - BCrypt.Net-Next 4.0.3→4.1.0, DeviceId 6.9→6.11, Polly 8.5.2→8.6.6
  - MySqlConnector 2.4.0→2.5.0, MySqlBackup.NET.MySqlConnector 2.6.5→2.7.0
  - SkiaSharp family 3.119.1→3.119.2
  - Microsoft.ML.OnnxRuntime 1.23.2→1.24.4
  - JetBrains.Annotations 2025.2.2→2025.2.4, xunit.runner.visualstudio 3.1.4→3.1.5, Microsoft.NET.Test.Sdk →18.5.0
  - Tools.InnoSetup 6.4.3→6.7.1
- `MainWindow.axaml`: removed `ExtendClientAreaToDecorationsHint`, dead acrylic border, and redundant nested Grid wrappers; simplified layout to `RowDefinitions="Auto, Auto, *"` (menu → navigation → content)
- `NavigationBar.axaml`: replaced fragile level-index ancestor bindings (`$parent[7]`/`$parent[6]`) with type-safe `$parent[views:MainWindow]` lookups
- UI compliance pass across all GUIClient views: removed inline `Background`/`Foreground` hex literals, added canonical button classes (`dialog1`, `dialog2`, `operation`, `subButton`, `navigation`), converted fixed `Width=` to `MinWidth=` on form inputs, migrated form `StackPanel`s to responsive `Grid` layouts with `form_label` classes
- `LoginWindow.axaml`: migrated form to responsive `Grid`, added `dialog1`/`dialog2` button classes with icons
- `CloseDialog.axaml`, `FixRequestDialog.axaml`: button classes normalized, `Classes="dark"` added to window
- `NavigationBar.axaml`: `Classes="navigation"` added to all nav buttons

### Fixed
- High-severity transitive vulnerabilities in `Tmds.DBus.Protocol` and `System.Security.Cryptography.Xml`
- GUIClient startup crash on Avalonia 12 caused by `LiveChartsCore.SkiaSharpView.Avalonia` 2.0.1 still targeting Avalonia 11 APIs (`Avalonia.Input.Gestures.PinchEvent`)
- `libs\Aura.UI\Aura.UI.sln` now loads cleanly after aligning the remaining Aura.UI test/desktop sample projects with `.NET 10` + Avalonia 12 and excluding the legacy Blazor gallery sample from the solution
- MainWindow top-bar overlap where native OS title bar and custom `<Menu>` rendered in the same zone (caused by `ExtendClientAreaToDecorationsHint="True"` without the matching transparency stack)
- Navigation bar buttons crashing with `NullReferenceException` / `ArgumentNullException` after layout flattening due to hardcoded ancestor-level bindings resolving to `null`



## [2.2.0] - 2026-02-06

This is a major maintenance release with .NET 10 upgrade and significant UI improvements.

### Added
- Responsive window layouts for EditRiskWindow (controls now expand/contract with window resizing)
- Responsive DataGrid columns in RisksPanelView with user controls for reordering, resizing, and sorting
- Tooltips to status icons in RisksPanelView for better user experience
- AssetTargetFallback configuration to support .NET 8/9 packages in .NET 10 projects

### Changed
- **Upgraded to .NET 10.0** with C# 13 language support across all projects
- Updated NuGet package source mapping to allow all packages from nuget.org by default
- Upgraded Hangfire from 1.8.21 to 1.8.23
- Upgraded MySqlConnector from 2.4.0 to 2.5.0
- Upgraded Newtonsoft.Json from 13.0.3 to 13.0.4 (security update)
- Upgraded Microsoft.Extensions.DependencyInjection from 9.0.9 to 9.0.12
- EditRiskWindow now starts at 1200x800 with minimum size of 900x650
- EditRiskWindow controls converted from fixed-width StackPanels to responsive Grid layouts
- RisksPanelView DataGrid columns now use star-sizing for proportional distribution
- Updated submodules: NessusParser, Aura.UI, netrisk-plugin-sdk, reliable-rest-client-wrapper
- Improved horizontal and vertical responsiveness across GUIClient views

### Fixed
- Resolved duplicate Applications.resx resource conflicts
- Fixed EF Core dependency warnings in DAL project
- Fixed ServerServices compile warnings (CS8603 nullable reference warnings in MapsterConfiguration)
- Fixed ServerServices CS0219 warning (unused variable in FaceIDService)
- Fixed PDFsharp restore failures
- Fixed API build failures and license gating issues
- Fixed GUI client build errors during migration
- Fixed Avalonia ReactiveUI startup issues
- Fixed EditRiskWindow buttons not staying at bottom during vertical resize
- Fixed EditRiskWindow right panel overlapping dropdowns on narrow windows
- Fixed risk deletion and closure bugs
- Package dependency warnings across all projects resolved



## [2.1.4] - 27/09/2025

This is a maintenance release with several bug fixes and improvements.

### Added

### Changed

### Fixed
- Risk closure bug


## [2.1.0] - 27/08/2025

This is a maintenance release with several bug fixes and improvements.

### Added
- A search on the incident response plan list
- Risk calculation command line command
- Plugin system
- FaceId plugin verification
- FaceId registration
- FaceId verification for risk closure
- Created the security classification entity
- Created the organization data entity
- Created the organization data group entity

### Changed
- Layout improvements on the incident window
- Changed the position of the edit button on the entities view
- Upgraded several packages to the latest version
- Incident ReportedByEntity field is now nullable
- Upgraded to Avalonia 11.3
- Upgraded to .NET 9
- Upgraded LiveCharts
- Bussiness process entitiy has new fields

### Fixed
- Return to the first pagination on the risk vulnerability list after selecting a new risk
- The search on the incident response plan list
- Bug in risk association
- Contributing score no longer considers closed vulnerabilities
- Bug in closing incident response plan window

## [2.0.7] - 2025-08-01

This is a bug fix release.

### Added

### Changed
- Filter to only show approved incidents response plans on the incident window
- Layout improvements on the incident window

### Fixed
- Risk vulnerability pagination
- Risks loading time
- Added missing scroll view on the incidents window
- Removed leftover foreign key on the incident response plan


## [2.0.6] - 2025-07-01

This is a bug fix release.

### Added
- Risk vulnerability pagination

### Changed


### Fixed
- Risks loading time


## [2.0.0] - 2025-06-01

This is a new major release that brings some new features and improvements.

### Added
- Incident Management
- Incident Response Plans
- New Dashboard graphics and improved performance
- Last import date on vulnerability data
- Filters on the entity list

### Changed
- Ordering of the entity list
- Filters for the multi select fields
- Risk filter location

### Fixed
- Several bug fixed - please see [Github issues](https://github.com/ffquintella/netrisk/issues)

## [1.7.1] - 2024-11-06

This is a new major release that brings some new features and improvements.

### Added

- Vulnerability chat tracking and improved e-mail communication
- New Dashboard graphics and improved performance
- Started to use .net migrations as a way to manage the database schema

### Changed

- The way risk catalogs are stored and managed

### Fixed

- Several bug fixed - please see [Github issues](https://github.com/ffquintella/netrisk/issues)

## [1.6.1] - 2024-10-15

...


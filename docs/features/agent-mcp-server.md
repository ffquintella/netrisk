# Agent co-work MCP server (GUI)

Status: **M61 implemented** (host, registration, keys, leases — see [Implementation notes (M61)](#implementation-notes-m61))
and **M62 implemented** (read tools — see [Implementation notes (M62)](#implementation-notes-m62)); no edit tool exists
yet (M63). The rest of this page is the design.

The desktop GUI hosts a **NetRisk MCP server** so an external AI agent (Claude Code, Claude Desktop, any
MCP client) can work *beside* the logged-in user: read and edit the same items, with the same
permissions, under the user's eyes. It is **not** the in-process developer-tools server used to debug the
GUI; it is a product feature with its own trust model.

## Principles

1. **The agent is the user, never more than the user.** Every tool call goes through the GUI's existing
   `ClientServices` → API path with the logged user's session. Server-side authorization is the only
   authority; the MCP layer adds restrictions, never privileges. A tool the user cannot perform is not
   listed (`tools/list` is filtered by the user's permissions) and, if called anyway, the API refuses it.
2. **The session token never leaves the GUI.** The agent receives an *agent key*, not the user's JWT.
3. **Nothing is standing.** Registration is a one-time human decision; access is a short lease the human
   grants block by block.
4. **Humans keep the decisions.** Actions that carry segregation of duties (approvals, risk acceptance
   sign-off, committee votes, permission/role changes, gate decisions) are never exposed as tools — the
   agent can prepare, the human decides in the GUI.
5. **Everything is visible and attributable.** A live activity panel shows each call; the audit trail
   records "user *U* via agent *A*".

## Flow

```
Agent                         MCP server (in GUI)                    User
  | register_agent(name,desc)        |                                  |
  |--------------------------------->| pending registration             |
  |                                  |--- approval dialog ------------->|
  |                                  |<-- approve / deny ---------------|
  |<-- agent_key (shown once) -------|                                  |
  |                                                                      
  | request_access(blocks=N, mode, reason)  [Authorization: agent key]   
  |--------------------------------->| pending request                  |
  |                                  |--- dialog: agent, mode, minutes,-|
  |                                  |    reason; user may shorten,     |
  |                                  |    downgrade to read-only        |
  |<-- lease {expires_at, mode} -----|<-- approve / deny ---------------|
  | tools/call … (while lease valid)                                     
  | (lease ends) -> 403 access_expired; agent must request again        
```

### Registration and the agent key

- `register_agent` is the only unauthenticated tool. It takes a display name, a description and client
  info, and creates a *pending* registration. It is rate-limited and capped (a flood of pending requests
  collapses into one counter in the GUI, never a stack of dialogs).
- The approval dialog shows name, description, client info, connecting process where the OS can tell,
  and a short fingerprint also returned to the agent so the user can match the two.
- On approval the key is generated with `Tools.RandomGenerator`/`RandomNumberGenerator` (≥256 bit),
  returned **once**, and stored only as `HashTool.CreateSha256` (a high-entropy token — no bcrypt),
  compared with `CryptographicOperations.FixedTimeEquals`. The registration is bound to the user id and
  can be listed, renamed and revoked in the GUI ("Agents" window). Revocation is immediate.
- A key without a valid lease can do exactly two things: `request_access` and `get_status`.

### Access leases

- A lease is requested in **blocks of 10 minutes**, 1–6 blocks (cap configurable). The user sees agent,
  reason, requested duration and mode, and can approve fewer minutes or downgrade the mode.
- Modes: **read** and **read-and-edit**. Edit tools are simply absent under a read lease.
- Leases are in-memory and end at expiry, user logout, session lock, application exit, or revocation.
  No auto-renew: a renewal is a new request. A lease may be *paused* from the GUI (calls refused, lease
  clock keeps running) or *ended* at any time.
- One active lease per agent; a second request while one is active extends nothing until approved.

## Transport and local security

- **Streamable HTTP on loopback only** (`127.0.0.1`, random per-launch port, advertised through a
  per-user discovery file with `0600`/user-ACL permissions), plus a small stdio bridge executable for MCP
  clients that only speak stdio. Opt-in: the server is **off by default** (a setting in the GUI) and
  closed when the user logs out.
- Reject any request whose `Host` is not the loopback literal or whose `Origin` is present and not
  allowed (DNS-rebinding defence). No CORS.
- Registration is bound to the machine and OS user; the discovery file is the only rendezvous.

## Tool surface

Tools are thin wrappers over existing `ClientServices` calls — no new business logic and no direct DAL
access from the GUI. Each tool declares its required permission and whether it reads or edits.

| Group | Read (read lease) | Edit (read-and-edit lease) |
|---|---|---|
| Risks | search, get, history, reviews, mitigations | create/update risk, add comment, plan mitigation draft |
| Vulnerabilities & findings | search, get, lifecycle | update status/assignment, add note |
| Incidents & IRPs | search, get, timeline | create/update incident, add timeline entry |
| Assessments | runs, answers, evidence | answer/comment, attach reference |
| Entities, hosts, CMDB | search, get, relationships | update descriptive fields |
| Reports / ICR | view overview, drill-down | — |
| Session | `get_status`, `whoami` (user name, role names, no token) | — |

**Never exposed:** delete, bulk delete, approvals/sign-offs/gates/committee actions, role and permission
administration, settings, plugin and integration configuration, backups, anything that returns a secret
(credential columns, vault references, keys) — keep it so ([Security Conventions](../../CLAUDE.md)).
Edits are single-item, validated by the same rules as the GUI, and use the item's concurrency token: a
stale write fails and the agent must re-read.

## One-click registration on installed agents

A button in the GUI ("Connect an agent" in the Agents window) writes the NetRisk MCP entry into the
configuration of AI clients installed on the machine, so the user never edits JSON by hand. It registers
the **server** only; it never grants anything — the agent still goes through `register_agent` and the
approval dialog, and the key is never written to a config file.

| Client | Detected by | Written to | Entry |
|---|---|---|---|
| Claude Code | `claude` on `PATH` or `~/.claude.json` | user-scope MCP config (via `claude mcp add` when the CLI is present, else a JSON merge) | stdio bridge |
| Claude Desktop | app bundle / install dir | `claude_desktop_config.json` → `mcpServers` | stdio bridge |
| GitHub Copilot (VS Code) | VS Code (stable/Insiders) user dir | user-level `mcp.json` → `servers` | stdio bridge |
| GitHub Copilot CLI | `copilot` on `PATH` or its config dir | its `mcp-config.json` | stdio bridge |
| ChatGPT | desktop app present | **not automatic** (see below) | guided |

Rules:

- **Detect, then show.** The dialog lists each client found with its config path and current state
  (*not registered*, *registered*, *registered but stale* when the bridge path changed). Undetected
  clients are listed greyed with the reason; the user can still point at a config file manually.
- **Preview and consent.** Before any write the dialog shows the exact diff to that file; nothing is
  written without a click. This is a change to another application's configuration, so it is always
  explicit and per client.
- **Merge, never overwrite.** Parse the existing file, add or replace only the `netrisk` entry, keep
  every other server and key, write atomically, and keep a timestamped `.bak` next to it. A file that
  does not parse is left untouched and reported.
- **Remove.** The same screen offers *Unregister*, which deletes only the `netrisk` entry.
- **Stdio bridge as the common denominator.** Every entry launches the bridge (T345), which finds the
  running GUI through the discovery file. The entry holds no port, token or key, so it survives GUI
  restarts and contains nothing to leak. If the GUI is not running the bridge reports that clearly to
  the client.
- **ChatGPT.** As of this design ChatGPT reaches MCP servers as *remote connectors* over a public HTTPS
  URL, which a loopback server deliberately is not, and has no local config file to write. The button
  therefore does not pretend: it shows what is supported and the steps, and the first release treats
  ChatGPT as unsupported until a local mechanism exists. The client list is data (a table of
  detector + writer), so adding one is a new row, not a redesign. **Verify each client's current
  config location and schema at implementation time; they change between releases.**
- **Paths from the OS only.** Locations come from the platform's known folders, not from caller input,
  and writes go through `Tools.Security.SafePathTool` containment on the resolved config directory.

Evidence to name: `AgentClientRegistrarMergeTest` (other servers and keys preserved, malformed file
untouched, `.bak` written, idempotent on re-run), `AgentClientRegistrarRemoveTest`, and
`AgentClientDetectionTest` per platform path table.

## Co-work experience

- **Activity panel**: live list of calls (time, tool, item, read/edit, result), with *Pause* and *End
  access* always visible; a status-bar indicator shows agent name and the lease's remaining time.
- **Item presence**: an item the agent is reading/editing shows a marker; a view the agent changes
  refreshes and highlights the changed fields; the user's own unsaved edit to the same item conflicts
  the standard way (concurrency token) rather than being overwritten.
- **Confirm-each-write** option on the lease: every edit waits for a one-click approval — recommended for
  a first session with a new agent.
- **Expiry warning** one minute before the lease ends, with *Grant another 10 min*.

## Audit and attribution

- Each call carries an agent-context header the GUI adds (`X-NetRisk-Agent: <registration id>`); the API
  records it in the audit trail next to the user. The header is informational — **it never changes
  authorization**, and a test proves a call with and without it is authorised identically.
- Local log (agent id, tool, item id, outcome, timings) with no field *values* for personal data.
- Registration, approval, denial, lease grant/expiry/revocation are audit events.

## Risks and how the design answers them

| Risk | Answer |
|---|---|
| **Prompt injection** — vulnerability titles, incident text or comments are attacker-influenced and reach the agent | Agent authority = lease mode; no tool can approve, delete or escalate; confirm-each-write; tool outputs tagged as untrusted data; activity panel makes odd behaviour visible |
| **Data leaves the machine** — items go to the agent's model provider (LGPD, FGV norm) | Lease dialog states it; per-item classification (S52) can exclude classes from agent tools; off by default; noted in the DPIA/ROPA records |
| Stolen agent key | Useless without a human-granted lease; leases are minutes; revocation list; key hashed at rest |
| Registering rewrites another app's config | Detect → diff preview → explicit click, merge-only, `.bak`, no secret written |
| Malicious local process | Loopback + origin checks, discovery file permissions, registration needs on-screen approval with fingerprint |
| Approval fatigue | Pending requests collapse and are rate-limited; default lease is one block; read-only is the default mode in the dialog |
| Privilege confusion | One path to the API, user's session, server decides; tool list filtered; test enumerates every tool against a user without the permission |

## Verification (what must be *named* as evidence)

- `AgentToolsNeverEscalateTest`: every registered tool, called as a user lacking its permission, is
  refused by the API; the tool list for that user omits it.
- `AgentKeyHandlingTest`: key is CSPRNG-length, stored hashed, compared in constant time, shown once.
- `AgentLeaseTest`: expiry, logout, lock, pause, revoke, read-lease refusing edit tools, 6-block cap.
- `AgentToolInventoryTest`: fails on a tool not in the reviewed allowlist, and on any tool whose name
  matches delete/approve/sign/permission/secret.
- `AgentLoopbackGuardTest`: non-loopback Host and foreign Origin refused.
- Observed at runtime: a real MCP client registering, being approved, being refused after expiry.

## Implementation notes (M61)

Code: `src/GUIClient/Agents/Core` (Avalonia-free, compiled into `GUIClient.Tests`), the GUI glue in
`src/GUIClient/Agents`, the dialogs and the Agents window under `Views/Agents`, the bridge in `src/McpBridge`.
Evidence: `AgentKeyHandlingTest`, `AgentLeaseTest`, `AgentLoopbackGuardTest`, `AgentStdioBridgeTest`.

- **Transport is hand-rolled, not the ModelContextProtocol SDK.** The SDK's HTTP transport needs ASP.NET Core
  (Kestrel) inside the self-contained desktop app plus new third-party packages; M61 needs only `initialize`,
  `ping`, `tools/list`, `tools/call` and notifications. `LoopbackHttp` speaks one bounded HTTP/1.1 request per
  connection (Content-Length only, 16 KiB of headers, 256 KiB of body, no chunked bodies, no repeated
  `Host`/`Origin`/`Authorization`); responses are `application/json`, `GET` answers 405 (no server stream).
  Optional `Mcp-Session-Id` sessions carry the client info shown in the dialog. Revisit when M62+ needs more.
- **Listener:** `TcpListener` on `127.0.0.1` port 0 (the OS picks the per-launch port), exclusive on Windows, at
  most 8 connections. Started only for a signed-in user with the setting on (`AgentMcpServerEnabled`, absent =
  off, in the encrypted per-user configuration store); closed on sign-out and session expiry.
- **Launch token.** Besides Host/Origin, every request must carry `X-NetRisk-Launch-Token`, a per-launch 256-bit
  value stored only in the owner-only discovery file. Loopback is shared by every OS user on the machine; without
  it the file's permissions would protect nothing. Direct HTTP clients must send it; the bridge does.
- **Discovery file:** `<client data folder>/agents/mcp-discovery.json` — 0600 in a 0700 directory, or a protected
  user-only ACL on Windows; written atomically, deleted on stop and exit, never holds a key. The bridge refuses any
  URL that is not `http://127.0.0.1:<port>/mcp` and never uses a proxy. **On Unix only**, the reader also refuses a
  file that group or others can access (`AgentLoopbackGuardTest.TestTheDiscoveryFileIsOwnerOnlyAndHoldsNoAgentKey`);
  on Windows the ACL is set when the file is written and is not re-checked when it is read.
- **Key transport.** The agent presents its key as `Authorization: Bearer <key>` *or* as the `agent_key` tool
  argument (an MCP client cannot set headers from a tool result); two different keys in one call are refused.
- **Fingerprint first.** A new `register_agent` answers `pending` with the pending id and fingerprint at once, so
  the agent can show the code before the user decides
  (`AgentKeyHandlingTest.TestTheFingerprintReachesTheAgentBeforeTheUserDecides`). Only a call carrying `pending_id`
  (and `request_access` with `request_id`) waits, up to 45 s. The key is generated when an approved registration is
  collected, returned once, and discarded if not collected within two minutes. A poll whose client hangs up is
  cancelled and issues nothing (`TestAKeyIsNeverIssuedToAPollWhoseClientHasGone`).
- **One lease gate.** Every tool other than the three bootstrap ones runs only after `McpDispatcher` checks a valid
  key and `AgentLeaseManager.Authorize` for that tool's read/edit kind, and never receives the key
  (`AgentLeaseTest.TestTheLeaseGate*`, `TestABusinessToolNeverReceivesTheAgentKey`).
- **Lifecycle order.** Stopping closes the listener and the approval queue before ending leases and dropping
  requests; starting clears whatever an earlier session left; revoking cancels requests before ending the lease
  (`TestNoGrantCanLandWhileTheServerStops`, `TestStartingTheServerClearsWhatAnEarlierSessionLeft`,
  `TestAGrantDuringRevocationLeavesNoLease`).
- **Approval dialogs** focus Deny and keep Approve/Grant disabled for `AgentApprovalTiming.ArmDelay` (1 s) after
  opening, so a keystroke meant for another window cannot approve (`AgentApprovalDialogSafetyTest`).
- **Storage:** registrations (id, user id, name, description, client, fingerprint, `key_sha256`, dates) are a JSON
  list under `AgentRegistrations` in the same per-user LiteDB store as the client's other settings.

Known limitations and follow-ups:

- **Slow-client exhaustion (accepted).** The listener serves at most 8 connections and gives each 15 s to send
  its request, so another local process — including one run by another OS user, since loopback is shared — can keep
  every slot busy and deny service to agents. It cannot get past the guard or the launch token; it can only make
  agents wait. Accepted for M61; revisit with per-connection accounting if it matters.
- **Lock** ends leases on Windows only (`SystemEvents.SessionSwitch`); macOS and Linux have no lock signal wired.
- **Connecting process.** The registration dialog does not name the process behind the connection.
- **Packaging.** The installers do not ship `netrisk-mcp-bridge` yet.

## Implementation notes (M62)

Code: `src/GUIClient/Agents/Core` — `AgentToolAccess.cs` (permission rules, the reviewed inventory, the registry),
`AgentToolResults.cs` (errors, envelope, limits), `AgentResultShape.cs` (allowlist shapes, credential names),
`AgentDataPolicy.cs` (entity properties, personal data, secret values) — the tools in `Core/Tools`, and the Cyber Risk
Overview's two in `src/GUIClient/Tools/RiskIndex/RiskOverviewAgentTools.cs`. ClientServices gained
`IEntitiesService.GetAllFromServerAsync`, `IHostsService.ClearCache`, `IMemoryCacheService.Clear` and `SessionCacheReset`.
Evidence: `AgentToolsNeverEscalateTest`, `AgentToolInventoryTest`, `AgentToolResultShapeTest`, `AgentToolSessionCacheTest`
(`GUIClient.Tests/Agents`); `SessionCacheResetTest` and the new cases in `EntitiesRestServiceTest`,
`HostsRestServiceStubTest`, `MemoryCacheServiceTest` (`ClientServices.Tests`).

**Tools (all read; none edits).** Each rule mirrors the API policy of the endpoints the tool calls; the reviewed list
is `AgentToolInventory` and the registry refuses any tool not on it or differing from it:

| Tool | ClientServices call | Rule (server policy) |
|---|---|---|
| `whoami` | `AgentSessionUser.Of(IAuthenticationService)` (no API call) | signed-in user |
| `search_risks`, `get_risk`, `get_risk_mitigation` | `IRisksService.GetAllRisksAsync`, `GetRiskScoringAsync`; `IMitigationService.GetByRiskIdAsync` | role `Administrator` or `riskmanagement` (`RequireRiskmanagement`) |
| `list_risk_reviews` | `IRisksService.GetRiskMgmtReviews` | any `review_*`, no role (`RequireMgmtReviewAccess`) |
| `search_findings`, `get_finding`, `get_finding_lifecycle` | `IVulnerabilitiesService.GetFilteredAsync`, `GetOneAsync`, `GetStatusHistoryAsync` | `vulnerabilities` or Admin claim |
| `search_incidents`, `get_incident` | `IIncidentsService.GetAllAsync`, `GetIncidentResponsPlanIdsByIdAsync` | `incident_management` or Admin claim |
| `search_response_plans`, `get_response_plan` | `IIncidentResponsePlansService.GetAllAsync`, `GetByIdAsync`, `GetExecutionsByPlanIdAsync` | `incident-response-plans` or Admin claim |
| `list_assessments`, `list_assessment_runs`, `get_assessment_run_answers`, `list_assessment_evidence` | `IAssessmentsService.GetAssessmentsAsync`, `GetAssessmentRuns`, `GetAssessmentRunAnsers`; `IAssessmentEvidenceService.GetRunEvidenceAsync` | claim `assessments`, no role (`RequireAssessmentAccess`) |
| `search_entities`, `get_entity`, `get_entity_relationships` | `IEntitiesService.GetAllFromServerAsync` | module Entities (`asset`) — stricter than the server (valid user) |
| `search_hosts`, `get_host` | `IHostsService.GetFilteredAsync` (by id for `get_host`), `GetAllHostServiceAsync`, `GetVulnerabilitySummaryAsync` | `hosts` or Admin claim |
| `get_risk_overview`, `list_risk_overview_children` | `IRiskIndexService.GetOverviewAsync`, `GetNodesAsync` | `risk_index_view` or Admin claim |

`get_status` stays a bootstrap tool (M61), outside the lease gate.

- **One gate, checked twice.** `McpDispatcher` runs a business tool only after: valid key → lease (M61) → the signed-in
  user (`AgentSessionUser.Of`: only while `IsAuthenticated`, since logout and an expired token leave the user info set)
  is the registration's user → the user holds the tool's permission. The same check runs again when the tool returns;
  a sign-out, another user, a lost permission, or an ended, paused or replaced lease drops the result. `tools/list` shows
  a business tool only to a user holding its permission; with nobody signed in, only the three bootstrap tools.
- **Fresh reads only.** No tool answers from a process-wide cache: entities come from `GetAllFromServerAsync`, which
  neither reads nor writes the 60-minute cache, and `get_host` from the filtered list by id rather than `GetOne`'s host
  cache. `SessionCacheReset` also clears the memory cache and the host cache on every sign-in and expired session, for the
  GUI as well (attached in `App`).
- **API refusal = clean error.** `AgentToolErrors` maps exception types and status codes only (`refused`, `not_found`,
  `invalid`, `invalid_filter`, `no_answer`, `server_error`, `server_unavailable`, `tool_failed`); server text and
  exception messages are never relayed; a null ClientServices answer fails the tool.
- **Allowlist shapes, enforced.** Each tool declares a record `ResultType`. The registry refuses a tool whose type names
  a credential (`AgentSecretNames`: words and joined forms, Portuguese included) or is anything but scalars, enums, lists
  and records outside the DAL/Model/framework namespaces. The dispatcher checks every result against its declared type
  and drops one carrying an undeclared field — for any `IAgentTool`, not only `AgentReadTool<>`.
- **Entity properties** are returned only if their key is on `AgentDataPolicy.SafeEntityProperties` (the reviewed
  properties of `EntitiesConfiguration.yaml` 2.7) and neither the key nor the value reads like a secret (vault reference,
  PEM, `Bearer`, JWT, `password=`-style pairs, long base64/hex runs). Withheld properties are counted per item and per
  kind in the envelope (`withheld_secret_fields`, `withheld_personal_fields`, `withheld_undeclared_fields`).
- **Personal data — pending the DPO decision.** `AgentDataPolicy.WithholdPersonalContactData` (true) withholds e-mail,
  phone and similar contact identifiers from every result; names that identify an item are kept. It is the one switch
  to relax once the S52 data-class exclusion and the DPO review exist.
- **Untrusted envelope (T352).** `{status, tool, data_trust: "untrusted", notice, withheld_*, data}`; the text content is
  the notice, then the data inside one `<netrisk-untrusted-data tool="…">` tag. The encoder keeps letters as themselves
  but always escapes `<`, `>`, `&` and quotes, so item text cannot close the tag. Vault references in any string are
  replaced by `[redacted: vault reference]`.
- **Bounds.** Pages default 20, max 50; lists of long-text items inside one item (plan tasks and executions, incident
  timeline, reviews, lifecycle, runs, answers) are paged 20 at a time (`task_page`, `execution_page`, `timeline_page`,
  `page`, `items_per_page` 1–20); other nested lists cap at 100. Titles clip at 300 and text at 2,000 characters with a
  marker; text arguments over their maximum are refused; a result over 400,000 characters answers `result_too_large`.
  `AgentToolResultShapeTest.TestEveryToolAnswersItsWorstCaseWithinTheSizeCap` proves every tool's worst case fits.

Deviations and known gaps:

- **Entity tools are stricter than the server** (the GUI's Entities module rule; the API needs only a valid user).
  `search_incidents`/`get_incident` call `GET /Incidents`, which also requires a FaceID transaction when the FaceID plugin
  is on — the client cannot see that, so such a user gets a clean `refused`.
- **Not in M62:** risk *history* (no ClientServices read for a risk's audit trail); a separate incident timeline read (the
  timeline is the record's dated fields and recorded actions); CMDB relationships beyond parent and direct children;
  assessment draft answers.
- **Follow-ups (Backlog):** single-item ClientServices reads for risks, incidents and entities — `get_risk`, `get_incident`
  and `get_entity*` load the list today, and the searches over risks, incidents, plans, assessments and entities filter
  and page client-side (only findings and hosts page on the server); `IEntitiesService.GetEntity` throws
  `NullObjectException` on a cold cache (pre-existing, not fixed).
- **Assessment refusals read `no_answer`, not `refused`:** `AssessmentsRestService` swallows the status into null and
  `AssessmentEvidenceRestService` throws without one.
- **Heuristics remain heuristics.** Credential and contact names, and secret-looking values, are pattern rules; free
  text outside entity properties (descriptions, notes) is only scanned for vault references. Text made entirely of
  characters the encoder must escape can still exceed the cap at the default page size; `items_per_page` lowers it.
- **The envelope is advisory.** It labels data; it cannot make an agent obey. The prompt-injection exercise is T359.
- **`tools/list` is not filtered by lease mode** (no edit tool exists yet); M63 should hide edit tools under a read lease.
- Three ClientServices calls are synchronous (`GetRiskMgmtReviews`, `GetAssessmentRuns`, `GetAssessmentRunAnsers`) and
  block a pool thread inside the dispatcher.
- The GUI composes the tools with `AgentReadToolCatalog.Create(IServiceProvider)`, which the inventory test also builds
  from a service collection; the bootstrapper's own `IAgentToolSource` registration and the `App` attach of
  `SessionCacheReset` are not covered by a test (GUIClient is not testable headless). `GUIClient.Tests` now references
  `ClientServices` (bringing `Tools`) and `NSubstitute`.

## Out of scope (first release)

Remote (non-loopback) agents; multiple simultaneous agents per user; agent-to-agent delegation;
server-side (API-hosted) MCP; unattended/scheduled agent runs; any agent decision on a gate.

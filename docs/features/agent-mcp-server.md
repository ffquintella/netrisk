# Agent co-work MCP server (GUI)

Status: **M61 implemented** (host, registration, keys, leases — see [Implementation notes (M61)](#implementation-notes-m61)),
**M62 implemented** (read tools — see [Implementation notes (M62)](#implementation-notes-m62)) and **M63 implemented**
(edit tools, confirm-each-write, activity panel, presence, agent attribution — see
[Implementation notes (M63)](#implementation-notes-m63)). **M64 build part implemented** (settings switch and LGPD
disclosure, data-class exclusion, user guide, observation harness — see [Implementation notes (M64)](#implementation-notes-m64)
and [Observed run](#observed-run)); the T359 security review and the T360 run against the real GUI are still open. The T354
and T356 remainders (creation, incident timeline, per-control highlight) are in [Implementation notes (T354/T356)](#implementation-notes-t354t356). User
guide: [docs/product-guides/agent-co-work.md](../product-guides/agent-co-work.md). The rest of this page is the design.

The desktop GUI hosts a **NetRisk MCP server** so an external AI agent (Claude Code, Claude Desktop, any
MCP client) can work *beside* the logged-in user: read and edit the same items, with the same
permissions, under the user's eyes. It is **not** the in-process developer-tools server used to debug the
GUI; it is a product feature with its own trust model.

## Principles

1. **The agent is the user, never more than the user.** Every tool call goes through the GUI's existing
   `ClientServices` → API path with the logged user's session. Server-side authorization is the only
   authority; the MCP layer adds restrictions, never privileges. A tool the user cannot perform is not
   listed (`tools/list` is filtered by the user's permissions) and, if called anyway, the API refuses it.
2. **The session token never leaves the GUI** — *for an agent confined to MCP.* The agent receives an *agent key*,
   not the user's JWT. **Assumption (T359 review):** the agent cannot read the user's files. An agent that runs as the
   same OS user with a shell or file access (Claude Code's Bash, for one) can open the client profile folder, where
   `configuration.db` is encrypted with a password built from two files in the same folder: it can read the JWT, flip
   `AgentExcludedDataClasses` (down to the floor) and `AgentMcpServerEnabled`, and call the API directly — every MCP-layer
   control is then advisory. Mitigations today: the user guide's sandbox and deny-rule guidance, the warning in the
   first-enable and access dialogs, and the constant floor of the exclusion policy. Stronger fixes are follow-ups
   (below, "same-user agent").
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
| **Prompt injection** — vulnerability titles, incident text or comments are attacker-influenced and reach the agent | Agent authority = lease mode; no tool can approve, delete or escalate; confirm-each-write, and after 20 unconfirmed writes in a lease every write is confirmed; tool outputs tagged as untrusted data, with invisible, bidirectional and look-alike characters escaped; the write dialog shows cleaned text and the changed span; activity panel makes odd behaviour visible. **Does not hold against an agent with a shell or file access as the same OS user:** a prompt-injected Claude Code can read the session token from the profile folder (see "Same-user agent") |
| **Data leaves the machine** — items go to the agent's model provider (LGPD, FGV norm) | Lease dialog states it; per-item classification (S52) can exclude classes from agent tools; off by default; noted in the DPIA/ROPA records |
| Stolen agent key | Useless without a human-granted lease; leases are minutes; revocation list; key hashed at rest |
| Registering rewrites another app's config | Detect → diff preview → explicit click, merge-only, `.bak`, no secret written |
| Malicious local process | Loopback + origin checks, discovery file permissions, registration needs on-screen approval with fingerprint. **A process of the same OS user** — including the agent itself when it has a shell — can read the profile folder and the session token in it; nothing at the MCP layer stops it |
| **Same-user agent** (T359 review) — the documented client, Claude Code, has Bash and runs as the user | Stated in the first-enable and access dialogs and the user guide: run the agent sandboxed, deny it the profile folder (`<ApplicationData>/NRGUIClient`), never grant edit leases to an agent with an unrestricted shell; the exclusion policy cannot go below its constant floor (sensitive personal data, children's data). **Follow-ups:** key `configuration.db` through `IMasterKeyProvider` (keychain/DPAPI/TPM) instead of files beside it; server-issued, agent-scoped, short-lived API tokens so an agent never needs the user's JWT and the server can tell its calls apart; OS-level sandboxing of the agent process |
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
- Observed at runtime: a real MCP client registering, being approved, being refused after expiry — [Observed run](#observed-run).
- `AgentDataClassExclusionTest` (T362): a withheld item is never returned by any read tool, any search or any edit tool;
  included items are unaffected; whatever cannot be classified is withheld.

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

## Implementation notes (M63)

Code: `src/GUIClient/Agents/Core` — `AgentEditing.cs` (edit-tool contract, plans, argument screening, versions),
`AgentActivity.cs` (activity log, presence, lease clock), the confirmation queue in `AgentApprovals.cs`, the gate in
`McpDispatcher.cs` — the edit tools in `Core/Tools/AgentEditToolCatalog.cs` and `AgentVersions.cs`; the GUI parts under
`Views/Agents` (`AgentWriteDialog`, `AgentStatusBar`, `AgentActivityWindow`) and `ViewModels/Agents`
(`AgentItemWatcher`, `AgentEditGuard`). T358: `Model/Agents/AgentContextHeader.cs`, `ClientServices/Http/AgentCallContext.cs`
(scope + `AgentContextHandler`, outermost in `RestService.ComposeHandlers`), `ServerServices/Security/AgentAttribution.cs`
(used by `DALService`) and `API/Middleware/AgentAttributionMiddleware.cs`. ClientServices gained
`IHostsService.UpdateHostAsync` (the existing `UpdateAsync` is `async void`, so nobody could know whether it worked).
Evidence: `AgentEditToolTest`, `AgentWriteConfirmationTest`, `AgentCoworkExperienceTest`, the edit-tool cases of
`AgentToolsNeverEscalateTest` and `AgentToolInventoryTest`, `AgentApprovalDialogSafetyTest` (write dialog) in
`GUIClient.Tests/Agents`; `AgentContextHandlerTest` and the `UpdateHostAsync` cases of `HostsRestServiceStubTest` in
`ClientServices.Tests`; `API.Tests/Security/AgentContextHeaderTest`; after the M63 security review also
`AgentScreenGuardTest`. Each guard was removed once and its test failed.

**Edit tools (T354).** Each rule mirrors every endpoint the tool calls, including the read it makes first, and adds
"not the third line" (`ThirdLineReadOnlyRequirement` refuses that audience every write). All carry `kind=edit` in
`AgentToolInventory`; `AgentEditFixtures` gives each one test arguments.

| Tool | ClientServices calls | Rule | May set | Never sets |
|---|---|---|---|---|
| `update_risk` | `GetAllRisksAsync`, `RiskSubjectExists`, `SaveRisk` | role Administrator or `riskmanagement`, claim `submit_risks` | subject (required, unique — `EditRiskViewModel`'s rules), notes, the four scenario fields | status, owner, manager, category, source, catalogs, scoring, closure/reopen, reviews, acceptance, entity link, review-request flags, submission date |
| `update_finding_status` | `GetOneAsync`, `UpdateLifecycleStatusAsync` | `vulnerabilities` + `vulnerabilities_update` | lifecycle `active` or `verified`, a justification | FalsePositive, OutOfScope, Duplicate (suppress/merge), RiskAccepted (needs an acceptance), Mitigated (closes); the register's workflow status; close/reopen |
| `update_finding_assignment` | `GetOneAsync`, `ITeamsService.GetAllAsync`, `UpdateAsync` | `vulnerabilities` + `vulnerabilities_create` | fix team (an id on the team list) | analyst (a user picker = personal data, pending the DPO), risk links (sent back unchanged), every other field |
| `add_finding_note` | `GetOneAsync`, `AddActionAsync` | `vulnerabilities` | one appended action note | existing notes |
| `update_incident` | `GetAllAsync`, `UpdateAsync` | `incident_management` | description (required), report, impact, cause, solution, recommendations, notes | name, status, kind, category, assignee, reporter, entities, dates, response-plan links |
| `answer_assessment_question` | `GetAssessmentRuns`, `GetAssessmentQuestions`, `GetAssessmentAnswers`, `GetAssessmentRunAnsers`, `CreateRunAnswer` | claim `assessments` | an answer option of that question, for an unanswered question only | an existing answer (changing one needs a delete), questions, options, run status |
| `comment_assessment_run` | `GetAssessmentRuns`, `UpdateAssessmentRun` | claim `assessments` | the run's comments (the server also makes the caller the run's analyst: shown and approved as an `analyst` line of the diff) | status, progress, answers |
| `update_entity_fields` | `GetAllFromServerAsync`, `SaveEntityAsync` | `entities_manage` | `name`, `description`, `objective`, `technology` where the type has them | risk context (criticality, internetFacing, securityClassification), parent (moves access), status, links (`Definition(…)` properties), flags/integers, a property whose current value is withheld, any `person` record |
| `update_host_fields` | `GetFilteredAsync` (by id), `UpdateHostAsync` | `hosts` + `hosts_create` | comment, OS text | name, addresses, status, team, entity (scope), source, scanner `Properties` (may hold credentials) |

- **Concurrency token — client-side, a deviation.** None of these records has a server-side token (only risk-index
  profiles and committee decisions do). Each item's read tool now returns a `version` (`AgentVersions`: a truncated
  SHA-256 over the fields an agent can read plus the record's own stamp — never a withheld property or host
  `Properties`, so it reveals nothing). An edit tool requires it, reads the item fresh to prepare the diff, and refuses
  a different version with `stale`. **The check is made twice** (security review M63 #2): the dialog may stay open for
  the whole confirmation wait (45 s) plus the queue, so the write itself reads the item again, refuses with `stale` if
  its version moved since the plan was made, and applies the agent's fields to *that* fresh copy — another user's change
  of status, owner, assignee or run status meanwhile is never written back with old values. `answer_assessment_question`
  re-checks "unanswered" at the write. **Residual window:** one round trip — between the write's own fresh read and its
  PUT the server accepts anything, and those PUTs carry the whole record (the GUI's path). Closing it needs server-side
  `If-Match` on these endpoints (follow-up). `add_finding_note` takes no version: it only appends.
- **Input = the user's content.** Every argument must be declared by the tool; a credential-like name (`AgentSecretNames`)
  or value (`AgentDataPolicy.LooksLikeSecretValue`), control/format characters, or text over its bound (titles 300,
  text 4,000, notes 2,000, host OS 255) is refused, never cut, and the error never repeats what was sent. The value
  heuristic has false positives: a 32+ character hash or token-like run in an incident description (an IOC) is refused.
  Results name changed fields and the new version only — no value goes back to the agent. The stored text carries no
  marker: each text argument says so (`AgentEditToolCatalog.StoredAsUserText`), and what records "through an agent" is
  T358 — the server log line for every request, plus `audit_logs.actor` for the audited types (risks, hosts, entities),
  not for finding notes, incidents or assessment answers.
- **The GUI's own deviation kept out:** `EditRiskViewModel` blanks `Assessment` on every save; `update_risk` preserves it.
  `comment_assessment_run` inherits the server rule that every run update sets the caller as the run's analyst
  (`AssessmentsController`); when that changes the analyst, the diff carries an `analyst` line, so the user approves it.
  `UpdateAssessmentRun` swallows refusals, so the tool reads the run back and answers `no_answer` when it did not land.
- **Listing.** `tools/list` shows an edit tool only to a request that identifies an agent — by its key header, or by an
  MCP session in which it made an authenticated call — holding a read-write lease. Clients that pass the key as an
  argument cache `tools/list` (no `listChanged`: no server stream), so `get_status` and the `request_access` grant also
  list `edit_tools` and `confirm_each_write`. The registry refuses a `RequiresEdit` tool that is not an `IAgentEditTool`
  (one that could write without reaching the confirmation gate).
- **Gate order for an edit:** key → lease (read-write) → registration's user signed in → permission → prepare (fresh
  read, version, validation, exact diff) → confirmation when the lease asks → the gate again (a sign-out, another user, a
  lost permission or an ended/paused lease while the dialog was open stops it here) → the write (fresh read, version
  again, apply) → presence/audit. If access ended *after* the write landed, the agent gets `written_access_ended` with
  only the changed field names and the new version (review M63 #5), and the activity panel lists it as such.
- **Only the tool's own calls are attributed** (review M63 #4): the agent-context scope wraps exactly the tool's
  prepare/read and write steps; presence and activity events are raised outside it with execution-context flow
  suppressed, and `AgentItemWatcher` clears the context in its UI-thread handler, so a screen's reload is never sent
  as "via agent".
- **Not delivered:** `create_risk` and `create_incident` (the GUI requires owner, manager, assignee and reporter pickers —
  user lists the agent has no reviewed read for, pending the DPO decision on personal data — plus category, source and
  scoring reads), incident timeline entries (no ClientServices call or API endpoint adds an incident action), risk
  comments, mitigation drafts and assessment evidence/attachments.

**Confirm-each-write (T357).** Chosen in the access dialog for a read-write grant, on by default
(`AgentAccessChoices.DefaultConfirmEachWrite`); the lease carries it and reports it. Each write is one item in the
existing approval queue (one dialog at a time), shows agent, fingerprint, tool, item and every field old → new, focuses
Deny and arms Approve after `ArmDelay`. An agent has at most one change waiting (review M63 #6): a parallel edit is
answered `busy` before it reads anything. Deny answers `write_denied`; no answer within `WriteConfirmationWait` (45 s,
under the clients' ~60 s) `not_confirmed`; the lease ending or being paused while it is open closes the dialog and
answers the lease's own status; a stopped queue answers `not_confirmed` without asking. It cannot be changed during a
lease (ask again).

**Activity panel and status bar (T355).** In-memory `AgentActivityLog` (200 calls; tool, item kind and id, read/edit,
status — never values), cleared when the server stops or the user changes. The status bar (main window, bottom) shows each
active lease — agent, mode, `m:ss` left, last call — with Pause/Resume, End access and Activity; in the last minute a
warning and "Grant another 10 min", which creates a *new* user-initiated request (one block, current mode) shown in the
normal access dialog with its own reason text; nothing changes until it is granted, and a grant replaces the lease with
ten minutes from approval. The activity window's Pause/Resume/End act on every active lease.

**Presence and refresh (T356).** `AgentPresence` marks items an agent touched in the last two minutes (editing wins)
and records each landed write (field names). Every screen that saves a whole record now consults `AgentSaveGuard`
(through `AgentEditGuard`) before saving, opened when the screen read its copy (review M63 #7): `EditRiskViewModel`,
`EditIncidentViewModel`, `EditVulnerabilitiesDialogViewModel`, `EditHostDialogViewModel` (from when the Hosts list
read the host), `EntityFormViewModel` and the Entities rename path (from when the tree read the entity — a cached tree
is safe, as `SaveEntityAsync` drops a saved entity from the cache), and `AssessmentRunDialogViewModel`. A conflict
refuses the save and names the agent's fields; the user reopens the item. The run viewer never edits comments, so before
each of its whole-run saves it takes comments and analyst from the server when an agent wrote to the run. Notices
("agent X is reading/editing this", "agent X just changed: notes") and in-place refresh: Risks, Incidents, Findings
(details reload), Hosts; the entity form shows the notice (it does not reload under unsaved edits — the guard refuses
instead). Changed fields are named in the notice, not highlighted per control. Not guarded, by design: the finding
status dialog and status buttons, and the run viewer's answers — they send a transition or a draft answer the server
applies to its current state, not a stale copy of the record. Evidence: `AgentScreenGuardTest` (the guard's decision,
and a source check that each listed save call is preceded by it — the view-models cannot be loaded headless).

**Agent context header and audit (T358).** Every API call an agent tool makes — read or edit — carries
`X-NetRisk-Agent: <registration id>` from an `AsyncLocal` scope the dispatcher opens around the tool's own steps; the
handler strips any other value, and the user's own calls on the same services carry none. The API reads it only after authentication and
only to annotate: `audit_logs.actor` becomes `"<login> via agent <id>"` (login shortened if needed, the marker never) —
at most 64 characters with the login always whole (the annotation is what gets shortened — review M63 #1), which
covers the audited types (risks, scoring, hosts, entities and their name/risk-context properties…), **not**
findings, incidents or assessment answers; for every endpoint, one application-log line `Agent-assisted request: user
<id> (<login>) via agent <id>: METHOD /path -> status` (route only, no query). The legacy JSON `audit` table records
the user only. Anything but an issued id — exactly 22 base64url characters, `RandomToken(16)` — is ignored, on the
client (`AgentCallContext.Begin` refuses it) and on the server alike. `AgentContextHeaderTest` evaluates every
action policy of the API for principals of every shape, reads and writes, with and without the header and with hostile
values, and requires identical decisions; it also fails if any authentication/authorization source reads the header.
The id is meaningful only to the user's desktop (the API keeps no registry): resolve it in the Agents window.

Other gaps: `update_risk` and `update_incident` read the whole list (no single-item read, as in M62); several
ClientServices calls are synchronous and block a pool thread; the team list used to validate `fix_team_id` comes from
`TeamsRestService`'s process-wide cache (validation and the dialog label only, nothing returned to the agent).
GUIClient.UiTests not run (S54 §3); the views changed (`MainWindow`, `RiskView`, `IncidentsView`, `VulnerabilitiesView`,
`HostsView`, `EntityForm`, `AgentAccessDialog`) still compile with them.

## Observed run

**T360 — 2026-11-17, automated part only.** A **real MCP client** — the official MCP Python SDK, package `mcp` 2.3.0
(`ClientSession` over `stdio_client`, protocol negotiated `2025-06-18`), taken from the local `uv` cache with `--offline`
— launched the **real stdio bridge** (`netrisk-mcp-bridge`, Debug build) which found the **real loopback server and
runtime** (`AgentCoworkRuntime`, `McpLoopbackServer` on `127.0.0.1`, owner-only discovery file, launch token) through the
discovery file. No network was used. Reproduce with `scripts/agents/observe-mcp.sh [transcript.md]`, which runs the opt-in
`GUIClient.Tests` `AgentObservedRunTest` (`NETRISK_OBSERVE_MCP=1`) and the client script
`scripts/agents/observe_mcp_client.py`; neither is packaged.

**Stand-ins, and only these:** the two approval dialogs (a scripted prompter approves the registration and grants one
10-minute *read* block — the dialogs themselves were not on screen), the clock (a manual `TimeProvider` the host advanced
by 11 minutes when the client asked, instead of waiting ten real minutes), the signed-in user (an `AuthenticatedUserInfo`
holding every tool permission) and the API behind the tools (`AgentDataClassExclusionTest`'s substituted ClientServices,
so the T362 exclusion is visible). **T360 stays open:** the run with the real Avalonia GUI and a real agent (below) has not
been done.

```text
client: netrisk-t360-observer on the official MCP Python SDK, package mcp 2.3.0
 1. -> initialize (through netrisk-mcp-bridge over stdio)
    <- protocol=2025-06-18 server=netrisk observation
 2. -> tools/list (no key yet)
    <- 26 tools: get_assessment_run_answers, get_entity, get_entity_relationships, get_finding, get_finding_lifecycle,
       get_host, get_incident, get_response_plan, get_risk, get_risk_mitigation, get_risk_overview, get_status,
       list_assessment_evidence, list_assessment_runs, list_assessments, list_risk_overview_children, list_risk_reviews,
       register_agent, request_access, search_entities, search_findings, search_hosts, search_incidents,
       search_response_plans, search_risks, whoami
 3. -> tools/call register_agent {"name": "T360 observer", "description": "Observed run of the agent co-work server (S55 T360)"}
    <- is_error=False {"status": "pending", "pending_id": "BqHi…7rIu", "fingerprint": "AAE5-84D5", "retry_after_seconds": 2, …}
 4. -> tools/call register_agent {"pending_id": "BqHi…7rIu"}
    (the host's stand-in for the approval dialog approves)
    <- is_error=False {"status": "approved", "agent_id": "azdJo96Hh7CubwXQmQbyvw", "agent_key": "<redacted: 48 chars>", "fingerprint": "AAE5-84D5", …}
 5. -> tools/call request_access {"agent_key": "<redacted: 48 chars>", "blocks": 1, "mode": "read", "reason": "T360: observe a read lease"}
    (the host's stand-in for the access dialog grants 1 block, read)
    <- is_error=False {"status": "granted", "mode": "read", "minutes": 10, "expires_at": "2026-11-17T12:10:00.0000000Z", "remaining_seconds": 600, "paused": false, "confirm_each_write": false, "edit_tools": []}
 6. -> tools/call get_status {"agent_key": "<redacted: 48 chars>"}
    <- is_error=False {"status": "ok", "agent_name": "T360 observer", "fingerprint": "AAE5-84D5", "lease": {"status": "active", "mode": "read", "minutes": 10, …}, "block_minutes": 10, "max_blocks": 6}
 7. -> tools/list (this MCP session now belongs to the agent; read lease)
    <- 26 tools; edit tools listed: []
 8. -> tools/call search_risks {"agent_key": "<redacted: 48 chars>"}
    <- is_error=False {"status": "ok", "tool": "search_risks", "data_trust": "untrusted", "withheld_personal_fields": 0, "withheld_secret_fields": 0, "withheld_undeclared_fields": 0, "withheld_classified_items": 4, "data.items": [{"id": 1, "subject": "Portal outage", "status": "New"}], "data.total": 1}
 9. -> tools/call get_risk {"agent_key": "<redacted: 48 chars>", "risk_id": 1}
    <- is_error=False {"status": "ok", "tool": "get_risk", "data_trust": "untrusted", …, "withheld_classified_items": 0, "data": {"id": 1, "subject": "Portal outage", "status": "New", …}}
10. -> tools/call get_risk (an item the T362 policy withholds) {"agent_key": "<redacted: 48 chars>", "risk_id": 2}
    <- is_error=True {"status": "withheld", "message": "This item is withheld from agents by the data-classification policy set in NetRisk (LGPD). Nothing was returned. Ask the user to work on it in NetRisk directly."}
11. -> tools/call update_risk (an edit under a read lease) {"agent_key": "<redacted: 48 chars>", "risk_id": 1, "version": "rev-fcf6209922b93b7b", "notes": "Changed by the T360 observer"}
    <- is_error=True {"status": "read_only", "message": "Your lease is read-only and this tool edits. Ask with request_access, mode read_write, if needed."}
12. -> (host) advance the test clock by 11 minutes: the 10-minute lease expires
13. -> tools/call search_risks (after expiry) {"agent_key": "<redacted: 48 chars>"}
    <- is_error=True {"status": "access_expired", "message": "No access lease is active. Call request_access to ask the user for access."}
14. -> tools/call get_status (after expiry) {"agent_key": "<redacted: 48 chars>"}
    <- is_error=False {"status": "ok", …, "lease": null, "pending_request_id": null, …}

host-side activity panel (AgentActivityLog): search_risks ok · get_risk risk 1 ok · get_risk risk 2 withheld ·
  update_risk risk 1 read_only · search_risks access_expired
host-side audit events: ServerStarted, RegistrationRequested, RegistrationApproved, KeyIssued, AccessRequested,
  AccessGranted, ToolFailed (the withheld read), ToolRefused (read_only), LeaseEnded, ToolRefused (expired)
```

(Long ids and messages are shortened with `…` above; the harness writes the whole transcript, and never the key. Re-run
after the T359 fixes on 2026-11-17: same steps and statuses; `search_risks` now withholds 4 — the test world gained a risk
linked only through the GUI's "Entity" field — and the harness also checks the raw tool results, not only the transcript.)

What the run showed beyond the scenario:

- **The bridge needs `DOTNET_ROOT` from the client.** The SDK starts a stdio server with a minimal environment (PATH,
  HOME…), so the framework-dependent `netrisk-mcp-bridge` failed with "You must install .NET" where .NET is not in its
  default location (Homebrew here). The harness passes `DOTNET_ROOT`; a user must too (`claude mcp add -e DOTNET_ROOT=…`).
  Follow-up for packaging: ship the bridge self-contained (with the M61 gap "installers do not ship the bridge").
- `tools/list` without a key lists the business read tools the signed-in user may run (M62 behaviour; calls still need a
  key and a lease) and no edit tool until the session's agent holds a read-write lease (M63).
- A withheld read is recorded as `ToolFailed` in the local audit log and as `withheld` in the activity panel.

### Manual checklist — the real GUI and a real agent (T360, still to do)

Use a test database and test data only: Claude Code sends what it reads to Anthropic.

1. Build the solution; start the GUI and sign in as a test user with `riskmanagement` (and, for step 11,
   `data_catalogue_manage`).
2. **Application → AI agents…**: tick *Allow AI agents on this computer to connect to NetRisk*. Check the LGPD
   confirmation appears (first time only), answer **No** and check the box returns to off; tick again, answer **Yes**,
   and check the status line shows `127.0.0.1:<port>` and that `<client data folder>/agents/mcp-discovery.json` exists
   with mode `0600`.
3. Note the bridge path (`src/McpBridge/bin/Debug/net10.0/netrisk-mcp-bridge` in a dev build) and run
   `claude mcp add netrisk -e DOTNET_ROOT="$DOTNET_ROOT" -- <bridge path>`; `claude mcp list` must show `netrisk`
   connected. Record `claude --version`. Configure the profile-folder deny rules and the sandboxed Bash from the user
   guide ("Run the agent sandboxed") and check that asking Claude Code to list that folder fails; record the result.
4. In Claude Code ask it to register with NetRisk. Check the fingerprint it prints equals the one in *An AI agent wants
   to connect*; check Approve is disabled for about a second; approve.
5. Ask for one block of read access with a reason. In *An AI agent is asking for access* check the reason, one block and
   read preselected, and the LGPD box naming the withheld classes; grant.
6. Check the status bar (agent, *Read*, `m:ss` counting down) and open **Activity**.
7. Ask the agent to search risks and open one: Activity shows `ok`; the risk screen shows "agent … is reading this item".
8. Ask it to change that risk's notes: refused `read_only`; nothing changes on screen.
9. **End access**, then ask for read-and-edit with *Confirm each change before it is saved* on; ask for the same change;
   check *An AI agent wants to change an item* shows the field before → after; approve; check the risk screen refreshes
   and the risk's audit trail shows "<login> via agent <id>".
10. In the Agents window, as the `data_catalogue_manage` user, check the data-class list is editable (and, as another
    user, read-only). Catalogue a data record as sensitive personal data and link a risk to it; ask the agent for that
    risk: `withheld`; ask it to search risks: the risk is absent and `withheld_classified_items` counts it.
11. Let the lease run out (or wait for the one-minute warning and let it pass): the next call is refused
    `access_expired` and the status bar disappears.
12. Untick the server switch: the agent reports NetRisk is not reachable; the discovery file is gone. Tick it again, then
    sign out: the server closes.
13. Record the date, Claude Code version, GUI build and any deviation here under "Observed run", then tick T360.

## Implementation notes (M64)

Code: `src/GUIClient/Agents/Core/AgentDataClassification.cs` (classes, policy, item links, the classification source,
the entity classifier and the per-call `AgentClassificationScope`); the read tools (`AgentReadToolCatalog`,
`AgentReadMap`, `RiskOverviewAgentTools`), the edit tools (`AgentEditToolCatalog`), the envelope and error
(`AgentToolResults`, `AgentDataPolicy`), the wiring (`AgentToolCall`, `McpDispatcher`, `AgentCoworkRuntime`,
`AgentCoworkService`, `GeneralServicesBootstrapper`); the settings and disclosure in `Views/Agents/AgentsWindow`,
`AgentAccessDialog` and their view-models; `Model/DataCatalogue/DataRecordSummaryDto` and `DataCatalogueService` (server).
Evidence: `AgentDataClassExclusionTest` (85 cases), `DataCatalogueServiceInMemoryTest.TestC1b_…`, the opt-in
`AgentObservedRunTest`. Each new guard was removed once and its test failed (twelve mutations: visibility check, search
filter, uncatalogued record, edit check, entity references, unconfigured default, count before query, inheritance,
unreadable policy, flag 5, host link, change permission).

**T362 — data classes.** NetRisk keeps two classifications, both on the entity map: the S52 catalogue of a data record
(`organizationData` → `PersonalDataCategory` and the marks *children or adolescents*, *large volume*, *strategic
research*) and the S46 security classification (`securityClassification` → a `securityClassificationLevel` whose
`sensitive` is true) on data records, data groups and applications. The classes an organisation may withhold are exactly
those terms: `personal_data`, `sensitive_personal_data`, `children_data`, `large_volume`, `strategic_research`,
`sensitive_classification`. **Default withheld: `sensitive_personal_data`, `children_data`, `sensitive_classification`**
(the categories the LGPD treats specially — art. 11 and art. 14 — and anything marked sensitive in S46).

- **Fails closed — S52 D4, "absent is never compliant".** An item whose classification cannot be established holds *every*
  class: a data record not catalogued or with its category undeclared; any record when the catalogue cannot be read (a
  user without `RequireDataCatalogueRead` gets a 403, so every data record is withheld from their agent); a mark not
  declared (`involves_minors` null) or not sent by an older server (the new summary fields null) counts as set; a
  classification reference to a missing level or an unreadable `sensitive` value; a link to an entity that is not on the
  list, or a parent cycle; any failure to read the entity list, the risk flags, a risk's chain (or a chain whose host
  link is redacted for this user), the hosts or the assessments a call needs. A stored policy that does not parse —
  an unknown name, or an empty entry such as `","` — excludes every class; only `""` means "nothing beyond the floor".
  Duplicate catalogue rows for one record are united, never one picked. A dispatcher built without a classification gets
  `AgentClassificationOptions.Unconfigured` (default policy, a source that answers nothing), so forgetting the wiring
  withholds rather than leaks.
- **Floor (T359 review #2c).** `AgentExclusionPolicy.Floor` — sensitive personal data and children's data — is always
  excluded: every stored value and every choice in the window is the floor plus what it names, so editing the settings
  store cannot go below it (`TestAStoredPolicyCannotGoBelowTheFloor`, `TestAnEmptyStoredPolicyStillWithholdsSensitivePersonalData`).
  It is a constant: NetRisk has no server-side DPO setting for an organisation-wide minimum yet (follow-up). A policy
  with nothing excluded exists only for tests (`internal`).
- **What classifies an item (the strictest rule where an item has no classification of its own):** an entity — its own
  classes and every ancestor's, so a module under a sensitive application is withheld; a risk — its `EntityId` column,
  **its chain** (`GET /RiskChain/Risks/{id}`, read for every risk a call touches, eight at a time) and flag 5 (declared or
  derived), which does not say which of its four conditions holds and so counts as all four; mitigation and reviews —
  their risk (read first, while the policy is active); a finding — its entity, its host's entity and **every risk it is
  associated with** (`Vulnerability.Risks`, classified as risks are); an
  incident — its entity, impacted entity and reporting entity; a host — its entity; an assessment — its entity; a run —
  its entity, its host and its assessment's entity; answers and evidence — their run; a Cyber Risk Overview node keyed
  `e:<id>` — that entity (breadcrumb nodes and children are filtered too). An entity property typed `Definition(…)`
  (`AgentDataPolicy.EntityReferenceProperties`) that points at a withheld entity is withheld. **No classification
  source:** incident response plans (`search_response_plans`, `get_response_plan`) link to no entity and their executions
  name no incident, so they are returned as before; `whoami` carries no item.
- **Behaviour.** A search drops withheld items and reports `withheld_classified_items` in the envelope; asking by id
  answers `withheld` (no data, no class named); an edit tool refuses with `withheld` right after its fresh read, before the
  version or any validation, and nothing is written. For the searches NetRisk pages client-side (risks, incidents,
  assessments, runs, entities, relationships, overview children) the count is taken **before** the query is matched, so
  it is the same whatever the query and cannot test what a withheld item says.
- **Who changes it.** The policy is the per-user setting `AgentExcludedDataClasses` in the encrypted client store, read
  on every tool call. The Agents window edits it only for a user holding `data_catalogue_manage` (or the Admin role
  claim), never the third line (`AgentExclusionPolicy.ChangePermission`, checked again in `AgentCoworkService`); everyone
  else sees it read-only. No tool, argument or agent call reaches it.
- **Server change.** `GET /DataCatalogue/Records` now also returns `InvolvesMinors`, `LargeVolume` and `StrategicResearch`
  per record (nullable; null for a record not catalogued), so one read classifies every record. Backward compatible; an
  older server's missing fields read as "set".

Deviations and gaps (T362):

- **Withheld by id is distinguishable from absent.** `withheld` tells the agent an item with that id exists; rejected
  alternative: answering `not_found`, which would hide the policy from the agent and the user's activity panel. It never
  says which class.
- **Server-paged searches leak a count per filter.** `search_findings` and `search_hosts` page and filter on the server,
  so the page drops withheld items after the agent's filter: the page can be shorter than `page_size`, `total` still
  counts withheld items, and the withheld count depends on the filter — a crafted filter can test whether a withheld
  finding or host matches it (a count, never content). Closing it needs the exclusion applied server-side (follow-up).
- **Not re-checked at the write.** An edit is classified at prepare; a reclassification during the confirmation wait is
  not seen by the write (one more read set per write was judged not worth it; the window is the dialog's 45 s).
- **The policy is per desktop**, not an organisation-wide setting: a DPO's choice applies to agents on that DPO's client
  only; other users run under the default and cannot weaken it. Follow-up: a server-side setting the client reads.
- **Correction (T359 review, blocker 1).** The first M64 delivery classified a risk only by `Risk.EntityId`, which the GUI
  never sets: its "Entity" field (`EditRiskViewModel` → `AssociateEntityToRisk` → `PUT /Risks/{id}/Entity`) writes the
  `risk_to_entity` many-to-many, which the server mirrors into `risk_chain_links` as a Legacy link for every chain-type
  entity (S41 §5.4; `Data/88.sql` backfilled the old rows). A risk linked in the GUI to a sensitive data record was
  therefore returned whole — the earlier note "withheld through its own `EntityId` at once" was false for the GUI path,
  and the tests missed it because they seeded `EntityId`. A risk is now classified through its chain as well, and a
  finding through its risks; `TestAnItemLinkedOnlyThroughTheGuiRiskAssociationIsWithheld`,
  `TestASearchDropsAndCountsAnItemLinkedThroughTheGuiRiskAssociation`,
  `TestNoEditToolTouchesAnItemLinkedThroughTheGuiRiskAssociation` and `TestARiskWhoseChainCannotBeReadIsWithheld` seed
  the link the way the GUI writes it (no `EntityId`, a Legacy chain link). Non-chain types (organisation units, people…)
  stay only in `risk_to_entity`, which the client reads one risk and one entity at a time (`GET /Risks/{id}/Entity`); they
  carry no S52/S46 class, so they are not read. **Re-audit of every other source** against how the GUI writes it: an
  incident's three entity fields are set by `EditIncidentViewModel`; a finding's application by
  `EditVulnerabilitiesDialogViewModel` and its risks by `AssociateRisksAsync`; a run's entity and host by
  `AssessmentRunDialogViewModel`; a host's entity is set by the server (imports, scope), never by the GUI; the entity map
  by `Parent`. Response plans link to risks and incidents but describe procedures and stay unclassified.
- **Flag 5 is reconciled nightly** (and on refresh / Gate A); the chain itself is read live on every call, so flag 5 only
  adds the declared flag and the derivations the chain does not show.
- `list_assessment_evidence` now requires `assessment_id` (the evidence endpoint does not name the run's assessment).
- The entity `version` digest still covers the value of a withheld reference (an entity id); recovering it would take
  guessing ids against a hash — existence of an id, not content.
- `person` entities carry no S52 class: their contact fields stay withheld (M62) and their names are returned.
- Cost while active: per call, the entity list, the catalogue, flag 5, the hosts of the page and the assessments, each at
  most once and only when needed, **plus one chain read per risk** the call touches (a search over the register reads one
  per risk, eight at a time). No bulk endpoint returns every risk's chain; one would remove the per-risk reads
  (follow-up).
- A withheld item's read is counted in the local audit log as `ToolFailed`.

**Same-user agent (T359 review, blocker 2).** Every control above lives in the MCP layer of the desktop client. An agent
running as the same OS user with a shell or file access — Claude Code, the documented client, has Bash — can read the
client profile folder (`<ApplicationData>/NRGUIClient`, or `<ApplicationData>/<environment>/NRGUIClient` outside
production; `%APPDATA%` on Windows, `~/.config` on macOS and Linux), whose `configuration.db` is LiteDB encrypted with a
password built from `device_token.txt` and `device_id.txt` beside it, and which holds the user's JWT
(`AuthToken`), `AgentExcludedDataClasses`, `AgentMcpServerEnabled` and `AgentRegistrations`. Such an agent can call the API
as the user and change the settings: principle 2 holds only for an agent confined to MCP. Not re-architected in M64. What
was done: the assumption is stated in the principles, the threat table, both dialogs (`AgentAccessSandboxMSG`, the
first-enable disclosure) and the user guide, which gives Claude Code deny rules and sandbox guidance (not a guarantee);
and the exclusion policy got its constant floor. **Follow-ups:** key the store through `IMasterKeyProvider`
(keychain/DPAPI/TPM) instead of files beside it; server-issued, agent-scoped, short-lived tokens so an agent never needs
the user's JWT; OS-level sandboxing of the agent process.

**Other T359 review fixes.**

- **#3 Disclosure gate.** `AgentServerGate.MayRun` — the switch on *and* the current disclosure version accepted — decides
  `AgentCoworkService.IsEnabled`, so a switch set by M61–M63 or by editing the store shows off and starts nothing until
  the user accepts the disclosure; the start re-checks it under the runtime's lifecycle lock (`StartAsync(stillWanted:)`),
  closing the race with a switch turned off meanwhile. Tests: `AgentReviewFixesTest.TestTheServerRunsOnlyWhenOnAndTheDisclosureWasAccepted`,
  `TestAStartNoLongerWantedUnderTheLifecycleLockStartsNothing`.
- **#4 Invisible and look-alike characters.** `AgentJson.Neutralize` writes every format (bidi, zero-width), private-use,
  unassigned, separator and control character, and every non-ASCII character whose NFKC form contains `< > " & '`, as a
  visible `\uXXXX` — in the structured content and in the rendered text. Test: `AgentToolResultShapeTest.TestNoUnsafeCharacterReachesTheAgentRaw`.
- **#5 Write dialog.** The prompt's label is one cleaned line and its values lose invisible characters, with runs of blank
  lines collapsed into a counted marker (`AgentWriteDisplay`); the dialog shows each value's size and the changed span;
  an agent's text of more than 20 lines or more than 3 blank lines in a row is refused (`AgentEditLimits`). Tests:
  `TestTheWriteDialogShowsNoHiddenCharacterAndNoInjectedLine`, `TestTheChangedSpanShowsAOneSentenceChangeInsideALongText`,
  `TestAnAgentValueThatCouldHideTextBelowTheFoldIsRefused`.
- **#6 Write budget.** A lease without confirm-each-write may make 20 writes (`AgentWriteBudget`); every later write of
  that lease waits for the user, and the activity panel shows `confirmation_required` once. Test:
  `TestALeaseWithoutConfirmationMustConfirmEveryWriteAfterItsBudget`. Finding notes, incidents and assessment answers
  are still not in `audit_logs` (only the server log line, M63).
- **#7 Disclosure text.** The three dialog strings and the Agents window now say what is true: free text goes out as
  written — personal data or passwords typed in it included, only vault references redacted; only entity properties are
  screened by name; the links that classify an item are listed and response plans are unclassified; the list applies to
  this computer only.
- **#8 Harness.** The observation test kills the `uv` → `python` → bridge tree and deletes its folder in a `finally`, and
  checks the raw tool results the client received (`.raw.json`, key redacted) for leaks, not only the shortened transcript.
- **Recorded, not changed:** lock ends leases on Windows only; whole-list reads for single risks, incidents and entities;
  `IEntitiesService.GetEntity` null on a cold cache; the bridge is framework-dependent and needs `DOTNET_ROOT` from the
  client, and no installer ships it; an edit is not re-classified at the write.

**T361 — settings and disclosure.** The switch existed since M61 in the Agents window (**Application → AI agents…**);
NetRisk has no per-user settings window, so the Agents window is where agent co-work is configured. It is now a caution
card (`Border.formCard.caution`: turning it on weakens the default posture) with the LGPD disclosure, beside a card with
the data classes withheld. Switching on asks once — per disclosure version, `AgentMcpServerDisclosureAccepted = "1"` —
with the full disclosure; **No** leaves it off. Toggling starts or stops the server live (`SetEnabledAsync`), and sign-out
closes it (both M61). The access dialog shows the LGPD disclosure with the classes withheld at that moment, and the
same-user agent warning. Twenty-two new keys in all three `Localization*.resx` (pt-BR translated); `./build.sh LintUi` reports zero violations. Not done: the
registration dialog carries no LGPD text (registering grants no access; the access dialog is where data starts to
flow); the view-models are not unit-tested (Avalonia), only the computed class-label keys
(`TestEveryClassLabelResolvesIn`) and the existing localization/style-class guards cover them; UI tests not run (S54 §3).

**T360 — observation.** See [Observed run](#observed-run): the automated run used a real MCP client, the real bridge and
the real server with the stand-ins listed there. The harness is a `GUIClient.Tests` class skipped unless
`NETRISK_OBSERVE_MCP=1`, plus a Python script; it adds no dependency to the solution and fetches nothing (the `mcp`
package must already be in the `uv` cache). **T360 remains open** until the manual checklist has been run with the real
GUI and a real agent.

**Not in this delivery:** T359 (the independent security review, including the prompt-injection exercise), ROADMAP and
CHANGELOG updates, and M65 (one-click registration — the guide says it is not available).

## Implementation notes (T354/T356)

The remainder of T354 (creation, incident timeline) and of T356 (per-control highlight). Code: `AgentEditing.cs`
(`AgentEditPlan.NewItem`, `AgentCreatedItem`, `IAgentEditTool.CreatesItems`, `AgentEditLimits.ActionNoteLength`),
`McpDispatcher.EditAsync`, `AgentApprovals.cs` (`WritePrompt.NewItem`), `Core/Tools/AgentEditToolCatalog.cs` (the three new
edit tools), `AgentReadToolCatalog.cs` (the new list tool), `AgentToolAccess.cs` (inventory), `AgentActivity.cs` (unacknowledged
changes) and the new `AgentHighlights.cs`; GUI glue in `ViewModels/Agents` (`AgentItemWatcher`, `AgentEditGuard.Saved`,
`AgentWriteDialogViewModel`), `Converters/AgentHighlightConverter.cs`, `Styles/WindowStyles.axaml` (`agentChanged`), and the
views listed under T356. Server: `POST /Incidents/{id}/Timeline` (`IncidentsController.AddTimelineEntryAsync`),
`IIncidentsService.AddTimelineEntryAsync` (server and ClientServices), `Model/DTO/IncidentTimelineEntryDto.cs`.

Evidence: `AgentEditToolTest` (creation, timeline and note-bound cases), `AgentToolInventoryTest`, `AgentToolsNeverEscalateTest`,
`AgentDataClassExclusionTest` (`TestACreationTheServerFilesUnderAWithheldEntityReturnsNoIdOrVersion`,
`TestAnIncidentTimelineEntryIsNeverAppendedToAWithheldIncident`), `AgentScreenGuardTest` (the T356 cases); server
`IncidentTimelineInMemoryTest`; API `IncidentsControllerTest` (timeline region) plus the unchanged
`ControllerAuthorizationInventoryTest`, `AgentContextHeaderTest` and `ThirdLineReadOnlyInventoryTest`, which enumerate the new
action; ClientServices `IncidentsRestServiceTest` and `NonIdempotentWritesAreNotRetriedTest`. Each new guard was removed once
and its test failed (18 mutations: always-confirm, plan/tool mismatch, withheld-after-create, budget count, timeline
classification, note bound, subject and sequence re-checks, category list, highlight map, presence union, acknowledge,
view binding, saved hook; server scope, length and control checks; an anonymous or extra policy on the new action).

**New tools.** All `kind=edit` except the list tool; every rule mirrors each endpoint the tool calls, the read it makes first
and the write, plus "not the third line".

| Tool | ClientServices calls | Rule | May set | Never sets |
|---|---|---|---|---|
| `list_risk_categories_and_sources` (read) | `GetRiskCategoriesAsync`, `GetRiskSourcesAsync` | role Administrator or `riskmanagement` — stricter than the server (valid user), like the entity tools | — | — |
| `create_risk` | the two lists, `RiskSubjectExists`, `GetRiskScore(1, 1)`, `CreateRisk`, `CreateRiskScoring`, `GetAllRisksAsync` (read-back) | role Administrator or `riskmanagement`, claim `submit_risks` (the `update_risk` rule) | subject (required, unique), notes, the four scenario fields; a category and a source **from the server's lists** (ids; shown by name) | owner, manager, submitter (fixed, below), status, score, entity, chain, catalogs, mitigation, review, closure, flags |
| `create_incident` | `GetNextSequenceAsync` (prepare and write), `CreateAsync`, `GetAllAsync` (read-back) | `incident_management` | description (required), report, impact, cause, solution, recommendations, notes | name (generated), status, category, kind, assignee, reporter, reporting/impacted/owning entity, dates, duration, response plans (but see the server's own automation below) |
| `add_incident_timeline_entry` | `GetAllAsync`, `AddTimelineEntryAsync` (new) | `incident_management` | one appended text entry (≤ 255 characters), stamped now as the user by the server | existing entries, anything of the incident |

**What a new item gets** — every line is in the "new item" dialog (`WritePrompt.NewItem`; all values new, no "before"):
a risk gets subject and the given texts, the chosen category and source, status `New`, owner and submitter = the signed-in
user, submission date now, the screen's default score (classic, likelihood 1 × impact 1, `GetRiskScore(1, 1)`, its own
`risk_scoring` row); the agent sets no manager, entity, catalog, mitigation, IRP or review. An incident gets
`SI-<year>-<next number:0000>` (the screen's naming), description and the given texts, status `New` (server), category
`not_specified`, kind `Incident`, report date now, duration 1 h (the screen's defaults), `reported_by_id` and `created_by_id`
= the signed-in user; the agent sets no assignee, reporting or impacted entity, owning entity, start date or plan.
**What the server then does by itself (correction, T354 review #4):** `IncidentsService.CreateAsync` runs
`IrpAutomationService.TriggerForIncidentAsync` — an enabled automation template that matches can **start a response plan**
for the new incident — and publishes `incident.created` to notification subscribers; `RisksService.CreateRiskAsync`
publishes `risk.created`. The tool descriptions say so, and the confirmation dialog shows it as a separate line under the
item (`AgentEditPlan.SideEffectsKey` → `WritePrompt.SideEffectsKey`; keys `AgentCreateIncidentSideEffectsMSG`,
`AgentCreateRiskSideEffectsMSG` in all three `Localization*.resx`).

**Creation rules.** A tool declared `CreatesItems` (only `create_risk`, `create_incident`) **always** waits for the user,
whatever the lease says: the dispatcher adds it to the confirm condition and refuses (`tool_failed`, nothing written) a
`NewItem` plan from any other tool. A creation counts against the write budget like any write. It re-checks at the write
what it checked at prepare: the subject is still free (else `stale`), the incident number is still the one approved (else
`stale` — the name the user approved is the name written). It reads the new item back and classifies it (T362): the server
files a new item under a scoped user's single entity claim (`AuditableContext`, Track 2), which the client cannot see
before writing; if that entity is withheld, or the item cannot be read back while the policy is active, the agent gets
`created_withheld` with the field names only — no id, no version. A risk whose score row fails after the risk was created
is answered `created_incomplete` (id, version, `not_written: ["score"]`), never "nothing was written". Presence records the
new item under its id; the risk and incident screens add it to their list (`RefreshRiskAfterAgentChangeAsync`,
`RefreshIncidentAfterAgentChangeAsync`) and highlight it.

**Server.** `POST /Incidents/{id}/Timeline` takes `{ text }` only; the author and time come from the server. It inherits the
class's `[PermissionAuthorize("incident_management")]` exactly as `PUT /Incidents/{id}` (no FaceID, which guards only the list
read), finds the incident through the caller's entity scope (out of scope = 404), trims and refuses empty, control-laden or
over-255-character text (400), and appends an `nr_actions` row (`ObjectType = "Incident"`, `IncidentId`) without touching
the incident row, so its `version` does not move. **No migration:** `nr_actions.IncidentId` and `fk_inc_actions` exist since
`Structure/39.sql`. Attribution is the M63 mechanism as it stands: the middleware's `Agent-assisted request` log line and
the legacy JSON `audit` row (user only); `nr_actions` is not in `GovernanceAuditInterceptor.AuditedTypes` (vulnerability
imports write thousands), so no `audit_logs` row. The ClientServices call is sent once (not retried).

**Fix found on the way.** `nr_actions.Message` is `varchar(255)`, but `add_finding_note` accepted 2,000 characters: a longer
note reached the server only after the user approved it and failed there (strict mode) or was cut. Both note tools are now
bounded by `AgentEditLimits.ActionNoteLength` (255), refused before any dialog
(`TestAnActionNoteOverTheColumnBoundIsRefusedBeforeTheUserIsAsked`).

**Not delivered, and why.**
- **Risk comments.** No ClientServices call reads or the GUI shows a risk's comments; `POST /Comments` is gated on a valid
  user only (no risk permission) and, for any type but `Host`, takes the author's user id from the request body — a defect
  reported here, not fixed: a client can attribute a risk or fix-request comment to another user. An agent tool on it would
  write text nobody sees under an attribution the server does not check.
- **Mitigation drafts.** No draft exists in the model: `IMitigationService.Create` *plans* the mitigation — the server sets
  the risk to `Mitigation Planned` and requires an owner (a person), strategy, effort, cost and date. A draft needs new
  storage, i.e. a schema change (numbered SQL), which needs the coordinator's decision.
- **Finding analyst assignment.** The analyst is a person picked from the user list; per the M63 rule people and assignee
  fields stay human-set (and personal data stays out pending the DPO). Excluded.
- **Attachments.** Arbitrary file content is a new risk surface (malware, data exfiltration into the register); no
  ClientServices call limits type or content. Out.
- **The timeline entry is not visible in the GUI**: the incident screen has no timeline/actions view, and `GET /Incidents`
  does not include `Actions`, so `get_incident`'s timeline never lists recorded actions (an M62 gap this makes visible). The
  user sees the entry in the confirm dialog and the activity panel only. Follow-up: an incident timeline read and view.

**Deviations (decisions made).**
- **Category and source come from the agent.** The brief asked for category to stay at the screen's default; the risk screen
  has none (the user must pick one, and the server refuses a risk without both). Decision: the agent proposes ids from the
  server's lists (a new read tool exposes them), the tool refuses an id not on them, and every creation is approved on
  screen with both shown by name — the human decides. Rejected: not delivering `create_risk`, and inventing a default
  (the lowest id), which would file wrong data silently. `AgentToolInventoryTest` admits `category_id` on `create_risk` only,
  as an integer, on a creating tool.
- **Always confirming creations** overrides a lease without confirm-each-write, on purpose (a new register item is larger
  than a field edit and carries the agent's category).
- **The screen's own required fields are not filled.** The risk screen requires a manager and an entity, the incident screen
  a reporter, an impacted entity and an assignee — all people or entity links the agent may not set. The items are created
  without them; the user's next save on the screen requires them.
- **The reporter is `ReportedById`** (the system-user column, Track 6), which the incident screen does not show (it shows
  the free-text or person-entity reporter).
- **The duplicate-scenario warning** (S42 §7, a question the screen asks) is not run for an agent's risk; the subject check is.
- **A score of 0** (the REST client's answer when the score call fails) is refused (`no_answer`) rather than saved.
- **No atomicity** across the risk and its score row (the screen has the same two calls); reported as `created_incomplete`.
- **Entity scope is detected, not prevented**: the client cannot know the user's entity claims before writing.

**T356 — highlight per control.** `AgentPresence` now keeps, per item, the union of fields agents changed that the user has
neither marked as seen nor edited (`Unacknowledged`, `Acknowledge`, `UserEdited`; no time limit; cleared with presence on
sign-out or server stop). `AgentHighlightScreen` maps each field the edit tools write to a control identity per screen, or
declares it shown only in the notice; `AgentHighlightScreens` holds the six maps (risks, incidents, findings, hosts, entity
form, assessment run dialog). `AgentScreenGuardTest` runs every edit tool with every argument and fails on a field the
screen of its kind neither shows nor names; an unknown field is named, never dropped. The watcher exposes `Highlights`; each
mapped control binds `Classes.agentChanged` and `ToolTip.Tip` through `AgentHighlightConverter` with its identity; the notice
names every pending field and offers **Mark as seen** (`type2`). The style class (`TextBlock.agentChanged`: warning
background token + underline; `TextBox.agentChanged`: warning border, thicker left edge) is last in `WindowStyles.axaml`;
colour is never alone (underline, tooltip from `AgentChangedFieldTip`, notice text). The highlight ends when the user marks
it as seen, edits the field (entity form fields, the run dialog's comments) or saves the item (`AgentEditGuard.Saved()`
after the save in `EditRiskViewModel`, `EditIncidentViewModel`, `EditVulnerabilitiesDialogViewModel`,
`EditHostDialogViewModel`, `EntityFormViewModel`, `AssessmentRunDialogViewModel`). Four new keys in all three
`Localization*.resx`; `./build.sh LintUi` reports zero violations.

**Security review fixes (T354/T356 review).**
1. *Blocker — a timeline entry made its incident undeletable.* `fk_inc_actions` (`Structure/39.sql`) has no ON DELETE
   (RESTRICT) and the new endpoint is the first writer of `nr_actions.IncidentId`. `IncidentsService.DeleteByIdAsync` now
   deletes the incident's `nr_actions` rows in the same `SaveChanges` before the incident — the entries are the incident's
   own history (nothing else references them; the legacy `audit` table records each deleted row), so they go with it
   rather than linger unlinked. No schema change. Tests: `IncidentTimelineInMemoryTest.TestDeletingAnIncidentDeletesItsTimelineEntriesInTheSameSave`;
   FK-enforced on MariaDB 10.11 (Docker): `IncidentTimelineDeleteIntegrationTests` (`TestTheForeignKeyRestrictsDeletingAnIncidentAnEntryPointsAt`,
   `TestAnIncidentWithTimelineEntriesIsDeletedWithThem`). **Attachments — not changed, recorded:** `fk_inc_attachments`
   is RESTRICT too, and incident attachments (`nr_files.IncidentId`, written by the GUI's incident screen) already make an
   incident with a file undeletable (500) — pre-existing. Deleting the files with the incident destroys uploaded evidence,
   and nulling the link orphans them (`FileAccessAuthorizer` then grants no parent permission); both are product and
   retention decisions, not a cheap fix.
2. *False conflict after an append.* `AgentEditPlan.MovesVersion = false` on `add_incident_timeline_entry` and
   `add_finding_note`; `AgentPresence.RecordChange(…, movesVersion: false)` records presence, the notice and the highlight
   but not `ChangedSince`, so an open form's save is no longer refused over an append that left the record and version
   untouched. `answer_assessment_question` still conflicts with an open run dialog (conservative: not reviewed for it).
   Tests: `AgentScreenGuardTest.TestAnAppendIsShownButNeverMakesAnOpenFormsCopyStale`,
   `TestAnEditOfTheRecordItselfStillMakesAnOpenFormsCopyStale`.
3. *A save that failed silently ended the highlight.* `AgentSaveGuard.SaveAsync`/`Save` (Core) end the highlights and
   reset the guard only when the save reports success; an exception or `false` leaves both. The host dialog now awaits
   `UpdateHostAsync` (the `async void UpdateAsync` hid every failure); `IAssessmentsService.UpdateAssessmentRun` now returns
   whether the server took it (still logs, never throws), and the run dialog stays open with `ErrorSavingAssessmentRunMSG`
   when it did not — before, it closed as saved. Tests: `AgentScreenGuardTest.TestAFailedSaveKeepsTheAgentsHighlightAndTheGuard`,
   `TestTheHostAndRunDialogsEndTheHighlightOnlyAfterAConfirmedSave`; `AssessmentsRestServiceTest.TestUpdateAssessmentRun*`.
4. *"No response plan" was false* — corrected above. Tests: `AgentEditToolTest.TestANewItemsDialogStatesWhatNetRiskStartsAndNotifies`,
   `TestAnUpdateStatesNoSideEffectAndTheIncidentDescriptionNoLongerDeniesThem`, `TestTheSideEffectAndRunSaveMessagesResolveIn`,
   `TestTheWriteDialogShowsTheSideEffectLine`.

**Follow-ups recorded (not built).** Duplicate incident numbers for entity-scoped users: `GetNextSequenceAsync` takes the
maximum over the caller's scoped view, and nothing makes `(Year, Sequence)` unique (pre-existing; `create_incident`'s
re-check cannot see other entities' incidents). `nr_actions.DateTime` is written with `DateTime.Now`, not UTC — like the
older writers (finding notes, importers), a Track 6 temporal-convention drift. `CommentsController.CreateAsync` takes the
author from the body for non-host comments (pre-existing; filed separately by the reviewer).

Gaps (T356): nothing of this was seen on screen — UI tests not run (S54 §3), and the views changed (`RiskView`,
`IncidentsView`, `VulnerabilitiesView`, `HostsView`, `EntityForm`, `AssessmentRunDialog`) are covered by source checks
only; whether the theme's own TextBox states keep the `agentChanged` border is unverified. The findings screen has no
single status or note control: lifecycle changes light the Timeline header, notes the Actions header. Not shown on their
screens and named in the notice only: an incident's category, reporter and timeline entries; a run's analyst and answers.
The run dialog lights what was pending when it opened (a change while it is open is refused at save, as in M63); the run
viewer and the entity tree do not highlight. On the list screens "edits it" means saving the item in its dialog, not
typing in a field (the panels are read-only).

## Out of scope (first release)

Remote (non-loopback) agents; multiple simultaneous agents per user; agent-to-agent delegation;
server-side (API-hosted) MCP; unattended/scheduled agent runs; any agent decision on a gate.

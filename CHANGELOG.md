# Change Log
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

## [NEXT] - Unreleased

This release includes new features and improvements.

### Added

- **A root `Makefile` as the discoverable entry point for the everyday developer commands.** `make` with no target lists every available target with a one-line description, so the commands documented across CLAUDE.md (Nuke build, `dotnet run`/`test`, the EF migration wrappers) are reachable without first knowing which script or project path to type. `make gui` starts the Avalonia desktop client with the `--environment` flag it needs to boot (`ENV=dev` by default), and there are matching targets for the API, website, background jobs and console client, plus `build`, `test`, `coverage`, `db-update` and `migration-add`. Targets that need an argument fail with a usage line instead of invoking the underlying tool with an empty one. Every target delegates to the existing tooling — nothing about the build is reimplemented here.

### Changed

### Fixed

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


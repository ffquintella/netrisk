# NetRisk GUI — Interaction & Workflow Standard (UX Study)

> Status: **Adopted standard** · Companion to [ui-standard.md](ui-standard.md) (visual standard) · Remediation completed under [ROADMAP.md → Track 1, Milestone 1.5](../ROADMAP.md)
> Basis: full static interaction study of all 67 views under `src/GUIClient/Views/` and their ViewModels (July 2026).
> Remediation: Phases A–E applied August 2026. Part III's gap table is the **pre-remediation** record and is kept as history; Part V records what the remediation changed, the machinery it introduced, and where it deliberately deviates.

[ui-standard.md](ui-standard.md) governs how windows **look** (tokens, typography, button classes, layout skeletons). This document governs how windows **behave**: navigation, modality, workflow journeys, action placement, feedback, validation, state synchronization, and keyboard interaction. Part I summarizes what the study found; Part II is the normative standard; Part III is the window-by-window gap analysis; Part IV is the phased remediation plan.

New views MUST follow Part II. Existing views migrate per the priorities in Part III/IV — as with the visual standard, do not gate feature work on full migration, but do not add new deviations.

---

## Part I — Study findings (systemic)

### F1. Two dialog generations coexist — the root cause of most drift

- **Modern stack:** `DialogWindowBase<TResult>` ([DialogWindowBase.cs](../src/GUIClient/ViewModels/Dialogs/DialogWindowBase.cs)) + `DialogService` + `ISaveableDialog`. Gives for free: Esc = cancel, Ctrl/Cmd+S = save, centering over parent, main-window dim overlay, typed dialog results. Used by: CloseDialog (vuln), EditVulnerabilitiesDialog, FixRequestDialog, VulnerabilityFixChatDialog, EditHostDialog, EditEntityDialog, the three Assessment dialogs, AssessmentRunViewer, ChangePasswordDialog, CreateReportDialog, EditReportTemplate/ScheduleDialog, EditSingleStringDialog.
- **Legacy stack:** plain `Window` manually `new`-ed inside the parent VM, size assigned in C# (often overriding XAML), VM closes its own window by reference. Used by: EditRiskWindow, EditMitigationWindow, EditMgmtReview, CloseRiskWindow, VulnerabilityImportWindow, EditIncidentWindow, IncidentResponsePlanWindow (+Task), AssessmentQuestionView, AdminWindow, UserInfo, Settings, ReportsWindow, the two report manager windows, NotificationsWindow, AddFaceImage, LoginWindow.

Consequences measured: keyboard accelerators only exist in modern dialogs (EditMitigation/EditMgmtReview/CloseRisk have **no** Save/Esc accelerators); window sizes are declared in up to three competing places (EditRiskWindow's XAML says 1200×800, its launcher forces 1000×750 — `RiskViewModel.cs:1215`); close/result semantics differ (typed result vs. side effects + events).

Two latent defects compound this: `DialogWindowBase.LockSize()` forces Min=Max on open, so dialogs declaring `CanResize="True"` (AssessmentRunViewer, AssessmentRunDialog, AssessmentImportDialog, FixRequestDialog) are **non-resizable at runtime** — the XAML lies; and `DialogService` always parents to and dims **MainWindow**, so dialogs opened from secondary windows (report managers) center over and dim the wrong window.

### F2. The two flagship workflows are designed a generation apart

- **Vulnerability triage** ([VulnerabilitiesView.axaml](../src/GUIClient/Views/VulnerabilitiesView.axaml)) is the app's best interaction pattern: a toolbar that works as an explicit **state machine** (Verify → Prioritize → Reject → RequestFix → Close → Reopen, each button enabled per current status), a `TreeDataGrid`, a real status bar with counts and paging, permission-gated buttons, typed dialogs.
- **The risk lifecycle** (New → Mitigation Planned → Mgmt Reviewed → Closed) — the product's core — is the weakest: the stage actions are tiny icon buttons **buried inside the scrolling detail pane** (Plan-mitigation at `RiskView.axaml:234`, Add-review at `:283`); status transitions are applied by the *parent* after events + full list reload; the review dialog gives no success feedback; and the **Reopen button is dead** — `BtReOpen` (`RiskView.axaml:376`) has no command binding and no handler. Four modal windows, no next-step guidance, no wizard.

### F3. Feedback is Message-Box-driven and inconsistent in every dimension

- **Delete confirmation exists in four different button sets**: YesNo (incidents, entities, users, risks, hosts), OkCancel (assessments), OkAbort (runs, IRP tasks, incident files, file reports), YesNoAbort (teams/profiles) — and some destructive actions don't confirm at all (in-list file delete in RiskView `:344`).
- **Success feedback**: modal success box after save in EditRisk, CloseRisk, UsersView, ConfigurationView (which has *no* error handling at all — `ConfigurationViewModel.cs:84-107`); silent close in EditMitigation, EditMgmtReview, EditHost, EditVulnerabilities, the report managers (silent even on delete/test). A user cannot form a habit of what "saved" looks like.
- **Validation is invisible**: ReactiveUI `ValidationRule`s exist in ~8 VMs but only ever drive `SaveEnabled` — rule text is never rendered, so a greyed-out Save gives no reason. Worse, in EditIncidentWindow `SaveButtonEnabled` is computed but never bound to the button (inert), UsersView declares rules but `ExecuteSave` never checks them before `SelectedRole!` dereferences (NRE risk), and EntityForm shows an inline error yet lets you save anyway.
- **Busy indication** is ad-hoc: ProgressRing in RiskView/EditRiskWindow/ReportsWindow, ProgressBar in VulnerabilityImport/AssessmentBuilder, nothing anywhere else (entity tree full reloads, incident loads, admin operations).

### F4. Navigation is sound at the core, incoherent at the edges

The shell pattern — all module views instantiated once as overlapping siblings, toggled by `IsVisible` (`MainWindow.axaml:112-128`) — is actually good: state (selection, filters, scroll) survives navigation. But: the embedded-vs-window and modal-vs-modeless split has no rule (Reports/Notifications modeless-unparented, Admin/UserInfo modal); the **gear icon is labeled "Settings" but opens Administration**, while the window named `Settings` is a read-only About box only reachable from the About menu; VMs locate their windows by walking the visual tree (`$parent.Parent.Parent…` ×8 in `RiskView.axaml:90`) or by global search (`WindowsManager.AllWindows.Find(...)`); no window persists geometry; Ctrl+F means live-filter in Incidents but jump-to-match-on-button in Entities.

### F5. Layout archetypes exist but were never named, so they drift

The codebase converged organically on five layouts — master-detail rail (Risks/Incidents/Hosts/Entities), toolbar+grid register (Vulnerabilities), manager window (report templates/schedules), monolithic scroll form (EditIncident, IRP Task = 25 stacked rows), and paged wizard (AssessmentRunViewer). Because they're unnamed, siblings diverge: control bars are horizontal everywhere but vertical in AssessmentsRunsListView; action rows are left-aligned (incident/IRP), right-aligned (most editors), centered (EditEntityDialog, CloseDialog, VulnerabilityImport), or **mid-form** (FixRequestDialog's Send sits next to a combo, `FixRequestDialog.axaml:61-72`); the same "edit a question" job has two full UIs (inline builder cards *and* the modal AssessmentQuestionView); EntityForm builds its entire UI in C# with a lone Save and no Cancel.

---

## Part II — The Interaction Standard (normative)

Rules are numbered `IX-n` for reference from PRs and the gap table.

### IX-1 · Window taxonomy and modality

Every surface must be exactly one of:

| Type | Definition | Modality | Base |
|---|---|---|---|
| **Module view** | A domain area hosted in the shell (Risks, Vulnerabilities…) | embedded, `IsVisible`-switched | `UserControl` |
| **Editor dialog** | Create/edit one record; commits or cancels | **modal**, owner = launching window | `DialogWindowBase<TResult>` |
| **Utility dialog** | Confirmation, single-field prompt, picker | **modal** | `DialogWindowBase<TResult>` |
| **Auxiliary window** | Long-lived tool used *alongside* the shell (Reports, Notifications, Admin) | **modeless, parented, singleton** | plain `Window` |
| **Wizard** | Multi-step guided flow with progress (assessment runner) | modal | `DialogWindowBase<TResult>` |

Rules:
- All create/edit flows are **modal editor dialogs returning typed results** through `DialogService`. No VM may `new` a window, assign its size, or hold a reference to close it — that is the base class's job.
- Auxiliary windows are opened once (re-activated if already open), owned by MainWindow, and never block it. Nothing else is modeless. *(Fixes: IncidentResponsePlanWindow opened modeless while its task children are modal; unparented ReportsWindow/managers.)*
- `DialogService` must parent to and dim the **actual launching window**, not always MainWindow.
- Window size is declared **in XAML only**. Launchers never override it. Editor dialogs sized per [ui-standard.md §5.4](ui-standard.md); `LockSize` applies only to utility dialogs — editor dialogs and wizards honor `CanResize` truthfully with Min sizes.

### IX-2 · One dialog stack

`DialogWindowBase<TResult>` + `ISaveableDialog` + `DialogService` is the **only** dialog machinery. Migration obligations for legacy windows are listed in Part III. Contract:
- Any VM exposing a save action implements `ISaveableDialog.SaveCommand` (this is what wires Ctrl+S — a `SaveCommand` without the interface is a silent accelerator gap, as in EditReportTemplate/ScheduleDialog today).
- Dialogs close by raising the typed result; the **caller** reacts (update collection in place, show follow-up). Child dialogs never mutate parent state directly.
- Command handlers are `async Task`, never `async void` (`RiskViewModel.cs:1208/1221` are the counterexamples).

### IX-3 · Action row

One canonical action row for every editor/utility dialog, per [ui-standard.md §4.4](ui-standard.md): **Save (`dialog1`) then Cancel (`dialog2`), horizontally centered, `Margin="10 10 0 0"`**, `IsDefault` on the primary of single-field dialogs, `IsCancel` on Cancel.
- Exactly **one** primary and **one** dismiss action. No Save / Save&Close / Close triples (EditIncidentWindow); no Close *and* Cancel side by side (VulnerabilityFixChatDialog); primary buttons never sit mid-form (FixRequestDialog).
- **Save commits and closes.** The refreshed parent list is the confirmation. Editors that must stay open for repeated operations (chat/comments) use a domain verb (Send) as primary and a single Close as dismiss.
- View-mode variants show a single centered Close (`dialog2`).
- If the team prefers right-aligned action rows, change ui-standard §4.4 first — one rule, one place; this document follows it.

### IX-4 · Feedback

| Event | Standard | Never |
|---|---|---|
| Save success | Dialog closes; parent updates in place. Optional transient toast/status-bar note. | Modal success MessageBox for routine saves |
| Save failure | Error MessageBox with the actual reason; dialog stays open with input intact | Silent failure; success box with no try/catch (ConfigurationView) |
| Delete / irreversible action | **One** confirmation pattern app-wide: Yes/No box, item name interpolated, consequence spelled out (cascade to children, etc.) | OkCancel / OkAbort / YesNoAbort variants; unconfirmed destructive actions |
| Reversible status change (Verify, Prioritize) | Immediate, no confirmation — but the reverse action must exist and be visible | Irreversible one-click mutations |
| Long operation (>300 ms) | Window-level `ProgressRing` per ui-standard §6.3 + inputs disabled; determinate `ProgressBar` when progress is known (import) | No indication (entity tree reload, incident load) |
| Blocked action | Button visible but disabled **with a `ToolTip.Tip` stating why** (permission, state, validation) | Hidden buttons; unexplained disabled states |

**Validation:** rules gate `SaveEnabled` *and* surface their message — inline error `TextBlock.warning` under the field (or Avalonia `DataValidationErrors`), plus tooltip on the disabled Save summarizing what's missing. Save handlers re-check `IsValid` before executing (never rely on button state alone). A validation rule that exists but is never bound (EditIncidentWindow) or never enforced (UsersView, EntityForm) is a defect, not a style choice.

### IX-5 · Module-view archetypes

Name the archetype in the view's header comment; follow its skeleton.

- **A · List + Detail** (Risks, Incidents, Hosts, Entities): left rail = header, list/tree, collapsible search row, **horizontal** control bar; `GridSplitter`; right = read-only detail. Control-bar order is fixed: `Add · Edit | Search · Reload | domain actions | Delete` — Delete always last, visually separated. Detail panes are read-only; editing goes through editor dialogs.
- **B · Register** (Vulnerabilities — canonical; Devices should adopt): toolbar (CRUD group · **lifecycle state-machine group** · filter group · IO group) + `TreeDataGrid`/`DataGrid` + status bar (count, context, paging). Row actions live in the toolbar acting on selection, not as per-row buttons.
- **C · Manager window** (report templates/schedules): list + control bar + read-only detail + modal editor dialog. Build once as a shared component; the two existing managers are near-duplicates.
- **D · Form pane** (Users, Configuration): inline editing is allowed **only** for administrative single-pane forms, and then with Save+Reset, enforced validation, and dirty-state tracking. Everything else edits via dialogs.
- **E · Wizard** (AssessmentRunViewer — canonical): page rail with completion ticks, progress bar, review gate before submit, auto-save drafts, Previous/Next/Submit footer. Use for any flow with ≥3 dependent steps.
- One job, one UI: never two coexisting editors for the same object (assessment questions currently have both the builder cards and the modal AssessmentQuestionView).

### IX-6 · Workflow design (lifecycle entities)

- Every lifecycle entity (risk, vulnerability, incident, assessment run) gets a **state-driven action toolbar** on its module view: all stage actions visible, enabled per current state, disabled-with-reason otherwise. The vulnerability toolbar is the reference. The risk lifecycle (Plan mitigation, Add review, Close, **Reopen**) must move out of the detail pane into such a toolbar.
- **Next-step affordance:** after a stage commits, offer the next stage in the same interaction (e.g., after creating a risk: "Plan mitigation now?" — after a review with next step "Accept": open the acceptance flow). The `NextStep` combo in EditMgmtReview is data; the UI should act on it.
- **No dead ends:** every referenced object in a detail pane is navigable (the incident view's activated-IRP list must open the plan); every state has a visible exit (dead Reopen button = defect); panels that can't be used yet in create mode (attachments-before-save) are replaced by draft support or hidden with an explanatory hint — never shown enabled-looking but inert.
- **State sync:** parents update via typed results/events with in-place collection updates preserving selection. Full-reload-and-reselect (entity tree) is a fallback, not the norm; when unavoidable, preserve expansion + selection (EntitiesViewModel already does — keep that bar).
- Multi-window chains (run metadata → runner) are labeled as steps ("Step 1 of 2") or merged into one wizard.

### IX-7 · Navigation & shell

- Keep the `IsVisible`-stack (state preservation is a feature). Route exclusively through `NavigateTo(AvaliableViews)`; expose it via a navigation service injected into VMs. **No VM walks the visual tree** (`$parent.Parent…×8`) **or greps `WindowsManager.AllWindows`** to find its window — the owning window is passed by the dialog/navigation service.
- Naming must match function: the gear is **Administration** (icon + tooltip); the read-only `Settings` window becomes **About**; editable configuration stays in Administration → Configuration.
- MainWindow and auxiliary windows persist and restore geometry (size/position/monitor, clamped to visible bounds).
- Feature buttons the user lacks permission for stay visible-disabled with a tooltip (current behavior — keep), consistently across nav bar and toolbars.

### IX-8 · Keyboard & focus

- **Esc closes / Ctrl(Cmd)+S saves in every dialog** — via the one base class, not per-window KeyBindings (EditRiskWindow re-implements manually what the base gives; legacy windows have nothing).
- **Ctrl+F = one semantic**: reveal the search row, focus it, **live-filter as you type** on list views; tree views additionally expand-and-highlight matches. (Today Incidents live-filters, Entities jumps on button press.) Wire it on every module view with a list/grid, not just Incidents/Entities.
- Every dialog sets `IsDefault`/`IsCancel` and an explicit `TabIndex` chain (EditEntityDialog is the model; extend everywhere).
- Enter commits single-field dialogs; multiline boxes use Ctrl+Enter conventionally.

### IX-9 · MVVM interaction contract (extends ui-standard §9)

- No UI built in code-behind (EntityForm's entire form is imperative C# — rebuild with XAML + DataTemplates over property definitions).
- Views never self-instantiate their VMs inconsistently (AssessmentView news its own VM; NavigationBar news its own) — composition root/DI decides.
- Events for created/updated/deleted records update `ObservableCollection`s in place.
- Remove dead surfaces promptly: orphaned RisksPanelView (referenced only by commented-out Dashboard code), the duplicate `StrThreatSources` block (`VulnerabilitiesView.axaml:267-272`), the dead `btn_SettingsOnClick` path (`MainWindow.axaml.cs:143`).

---

## Part III — Window-by-window gap analysis

Verdicts: ✅ aligned (minor or no gaps) · 🟡 partial (violates specific rules) · 🔴 major (needs redesign/migration). Rules cited as IX-n.

### Shell & navigation

| Window | Verdict | Gaps → fix |
|---|---|---|
| MainWindow | 🟡 | Sound view-stack; but window lookup via `WindowsManager.Find` + `$parent` params (IX-7); dead `btn_SettingsOnClick` (IX-9); no geometry persistence (IX-7). |
| NavigationBar | 🟡 | Self-built VM (IX-9); gear labeled Settings → opens Admin (IX-7 naming); 10 s notification polling is fine; permission-disabled buttons ✅. |
| DashboardView | ✅ | Static chart grid; no interaction gaps. |
| NotificationsWindow | 🟡 | Correctly modeless but unparented, not singleton, no Esc (IX-1, IX-8). |
| UpgradeWindow | ✅ | Purpose-built progress window; fine. |
| LoginWindow | 🟡 | `IsDefault`/`IsCancel` ✅; fake incremental progress bar should become indeterminate; device/acceptance MsgBox chain acceptable. |
| LoadConfigurationWindow | 🔴 | Hardcoded English incl. typo "Well-come" (ui-standard §3.2); no sizing; no Esc/Enter; single Save with no validation (IX-4, IX-8). Small window — full rebuild is cheap. |
| Settings | 🟡 | Read-only info box misnamed "Settings" → rename About (IX-7); modal is acceptable for About. |
| UserInfo | 🟡 | Fine as modal account panel; no Esc (IX-8). |

### Risk cluster

| Window | Verdict | Gaps → fix |
|---|---|---|
| RiskView | 🔴 | Core-product workflow buried: Plan-mitigation/Add-review are 22 px icons deep in the detail scroll (`:234`, `:283`); **dead Reopen button** (`:376`); lifecycle must become a state-driven toolbar (IX-6, archetype B behaviors on an A layout); `$parent`×8 command params (IX-7); `async void` add/edit (IX-2); unconfirmed in-list file delete (`:344`, IX-4). |
| EditRiskWindow | 🟡 | Best validation+feedback of the legacy set, but: legacy stack (IX-2); XAML size overridden by launcher (IX-1); manual KeyBindings instead of base (IX-8); success MsgBox on save (IX-4 — drop when toast lands); right-aligned action row (IX-3). |
| EditMitigationWindow | 🟡 | Legacy stack, **no Esc/Ctrl+S at all** (IX-2/8); silent success ✅ per IX-4 but inconsistent with siblings until they converge; file delete confirms ✅. |
| EditMgmtReview | 🔴 | Legacy stack; no size in XAML (launcher-only, IX-1); no validation rules (IX-4); no success feedback *and* the status flip happens invisibly in the parent (IX-6); asymmetric CommandParameter on Save vs Cancel; `NextStep` combo captured but never acted on (IX-6 next-step affordance). |
| CloseRiskWindow | 🟡 | Legacy stack; duplicates the verb of vuln CloseDialog on a different architecture (IX-2); success MsgBox (IX-4); fixed-size forced by launcher (IX-1). Merge onto DialogWindowBase; consider one parameterized close dialog. |
| RisksPanelView | 🔴 | Orphaned/unreachable; literal-string unclassed buttons. Delete or finish (IX-9). |

### Vulnerability cluster

| Window | Verdict | Gaps → fix |
|---|---|---|
| VulnerabilitiesView | ✅ | Canonical archetype B (state toolbar, TreeDataGrid, status bar, permission gating). Residual: duplicate `StrThreatSources` block (`:267-272`); Verify/Prioritize one-click is fine (reversible) but Reopen must stay visible (IX-4). |
| CloseDialog (vuln) | ✅ | Modern stack, ISaveableDialog, centered action row — reference utility dialog. |
| EditVulnerabilitiesDialog | 🟡 | Modern stack ✅, 6 validation rules gate Save but messages invisible (IX-4); action row right-ish not centered (IX-3); commented-out alternate launch path to delete. |
| FixRequestDialog | 🔴 | **Primary Send/Cancel sit mid-form beside a combo** (IX-3); commands named `Send`/`Cancel` off-contract (ui-standard §9); title resource says `SendEmailDialog`. Relayout footer; rename. |
| VulnerabilityFixChatDialog | 🟡 | **Close + Cancel side-by-side** (IX-3) — keep Send + one Close; otherwise sound. |
| VulnerabilityImportWindow | 🟡 | Legacy stack (IX-2); has the app's only determinate ProgressBar ✅ (keep as IX-4 reference); centered action row ✅. |
| HostsView | 🟡 | Archetype A ✅, hybrid modern child dialog ✅; hardcoded labels "Id:", "IP:"… (ui-standard §3.2); no Ctrl+F (IX-8). |
| EditHostDialog | ✅ | Modern stack; right-aligned row → center per IX-3. |
| DeviceView | 🟡 | Only view with per-row action buttons (IX-5 B: move Approve/Reject/Delete to a selection toolbar); no header/filter/status bar; approve/reject one-click with result box is acceptable. |

### Incident & IRP cluster

| Window | Verdict | Gaps → fix |
|---|---|---|
| IncidentsView | 🟡 | Archetype A ✅ with live Ctrl+F ✅ and event-driven refresh ✅; activated-IRP list is a **dead end** — make items open the plan (IX-6); Delete uses YesNo ✅ (adopt as the app-wide pattern). |
| EditIncidentWindow | 🔴 | Monolithic ~15-field single-scroll form stacking 7×120 px text boxes (IX-5 — needs sections/tabs per ui-standard §5.3.1); **three-button Save/Save&Close/Close, unclassed** (IX-3); validation computed but **never bound to the button** (IX-4 defect); attachments visible-but-inert in create mode (IX-6); silently flips to Edit after create with no cue. Legacy stack (IX-2). |
| IncidentResponsePlanWindow | 🔴 | **Modeless while its children are modal** (IX-1); legacy stack; task-child sizes hardcoded 900×900 in C# overriding XAML (IX-1); left-aligned action row (IX-3); double-Close in code-behind. |
| IncidentResponsePlanTaskWindow | 🔴 | 25 stacked rows in one scroll (IX-5 — group into sections); attachments-before-save dead end (IX-6); legacy stack; left-aligned actions. |

### Assessment & entity cluster

| Window | Verdict | Gaps → fix |
|---|---|---|
| AssessmentView | 🟡 | Tab switch **nulls the selected assessment** (state-loss, IX-6); inline add/edit name bar is a one-off pattern (IX-5); self-built VM (IX-9). |
| AssessmentBuilderView | ✅ | Modern inline card editor with the app's only inline busy bar; keep as archetype reference for inline expansion. |
| AssessmentQuestionView | 🔴 | Duplicate second editor for the same object as the builder (IX-5 "one job, one UI"); heavy custom chrome; `operation`-class buttons in a form. Retire in favor of the builder. |
| AssessmentImportDialog | ✅ | Best validation UX in the app (dry-run error/warning lists, gated Import) — the IX-4 validation reference. Hex literals for error colors should become classes (ui-standard §2.6). |
| AssessmentRunDialog | 🟡 | Fine; part of a 2-window chain → label "Step 1 of 2" (IX-6); `CanResize=True` neutered by LockSize (IX-1 defect). |
| AssessmentRunViewer | ✅ | Canonical wizard (page rail, progress, review gate, debounced auto-save, submit→vulnerability creation). Fix the LockSize/CanResize contradiction (IX-1). |
| AssessmentsRunsListView | 🟡 | **Vertical** button rail — only one in the app (IX-5: horizontal control bar); submitted-run lockdown ✅; OkAbort delete → YesNo (IX-4). |
| EntitiesView | 🟡 | Archetype A ✅ with expansion-preserving refresh ✅; Ctrl+F semantics differ from Incidents (jump vs live-filter — converge per IX-8); heavy full-reload after every edit (IX-6); cascade delete confirms ✅ but should spell out the cascade (IX-4). |
| EntityForm | 🔴 | Entire UI built imperatively in C#; lone Save, no Cancel/dirty tracking; inline validation shown but **not enforced** on save (IX-4/9). Rebuild as XAML DataTemplates over property definitions. |
| EditEntityDialog | ✅ | Modern stack, explicit TabIndex + IsDefault — the IX-8 keyboard reference; center-aligned row ✅. |

### Admin, auth & reports cluster

| Window | Verdict | Gaps → fix |
|---|---|---|
| AdminWindow | 🟡 | Reuses the shell's view-stack pattern ✅; modal-blocking the whole shell is debatable → modeless singleton (IX-1); active tab shown only by disabled state; no Esc (IX-8). |
| UsersView | 🟡 | Rich inline form pane (allowed as archetype D) but validation rules declared and **never enforced** before `SelectedRole!` deref (IX-4 defect, NRE risk); success MsgBox per mutation (IX-4); three delete-confirm variants inside one view (YesNo vs YesNoAbort). |
| Admin/PluginsView | ✅ | Read-only grid + inline toggle; toggle handled in code-behind → command (IX-9). |
| ConfigurationView | 🔴 | Save always reports success, **no error handling** (`ConfigurationViewModel.cs:84-107`, IX-4); no dirty tracking/Reset (IX-5 D). |
| Admin/AddFaceImage | 🟡 | Opened via `WindowsManager.Find(AdminWindow)` (IX-7); centered Save/Cancel ✅; window size two-way-bound to VM is unusual but functional. |
| ChangePasswordDialog | ✅ | Modern stack, live validation gating Save ✅; wire `ISaveableDialog` so Ctrl+S works (IX-2). |
| VerifyFaceID | ✅ | Purpose-built liveness window; self-closing; fine. |
| ReportsWindow | 🟡 | Modeless ✅ but unparented + not singleton (IX-1); report-type stack via `IntEqualConverter` works; ProgressRing ✅; hardcoded report-type list acceptable. |
| Reports views (RiskReview, EntitiesRisks, RisksVsCosts, ImpactVsProbability, VulnerabilitiesByTime, FileReports) | 🟡 | Consistent Generate+Export skeleton ✅; FileReports delete = OkAbort → YesNo (IX-4); silent create success (fine once toast exists, IX-4); Graphs/* ✅ presentation-only. |
| ReportTemplateManagerWindow / ReportScheduleManagerWindow | 🟡 | Near-duplicate archetype C windows → shared component (IX-5); dialogs they open dim/center on **MainWindow** not them (IX-1 DialogService defect); **no feedback at all** on save/delete/test (IX-4); hardcoded English headers/columns (ui-standard §3.2); no Esc (IX-8). |
| CreateReportDialog / EditReportScheduleDialog / EditReportTemplateDialog | 🟡 | Modern stack ✅; `SaveCommand` present but **`ISaveableDialog` not implemented** → Ctrl+S dead (IX-2); template editor's section labels hardcoded (§3.2). |
| EditSingleStringDialog | ✅ | The reference utility dialog: DialogWindowBase + ISaveableDialog + typed result. |

---

## Part IV — Standardization plan (phased)

**Phase A — defects & one-line divergences (days).** Dead Reopen button on RiskView; bind EditIncidentWindow's `SaveButtonEnabled`; enforce UsersView validation before save; try/catch + error box in ConfigurationView; delete duplicate `StrThreatSources`; remove RisksPanelView and `btn_SettingsOnClick`; make IRP list in IncidentsView open the plan; unify every delete confirmation on YesNo-with-name; fix `CanResize` vs `LockSize` (honor XAML); rename gear → Administration and Settings window → About; localize LoadConfigurationWindow and the report manager strings.

**Phase B — one dialog stack (per-window migrations).** Move EditRisk, EditMitigation, EditMgmtReview, CloseRisk, VulnerabilityImport, EditIncident, IRP + IRP Task, AddFaceImage onto `DialogWindowBase<TResult>`/`DialogService`; implement `ISaveableDialog` wherever `SaveCommand` exists (report dialogs, ChangePassword); fix `DialogService` owner-parenting; delete all launcher-side size overrides; retire AssessmentQuestionView.

**Phase C — feedback standard.** Introduce the toast/status-bar notifier; remove routine success MessageBoxes; surface validation messages inline + on disabled-Save tooltips; add busy indication to every async view (entity reload, admin ops); disabled-with-reason tooltips on all gated toolbar buttons.

**Phase D — workflow convergence.** Risk lifecycle toolbar on RiskView (Plan mitigation / Review / Close / **Reopen**, state-enabled — the vulnerability pattern) with next-step prompts after each stage; EditIncidentWindow re-sectioned (tabs or header2 sections) with a single Save/Cancel row; IRP window made modal and its task form sectioned; Devices moved to a selection toolbar; assessment tab-switch state loss fixed; EntityForm rebuilt declaratively; shared manager-window component; Ctrl+F everywhere with one semantic.

**Phase E — shell polish.** Navigation service (no visual-tree walking / WindowsManager greps); auxiliary windows parented + singleton; window-geometry persistence; Esc on all plain windows; TabIndex sweep using EditEntityDialog as the model.

Acceptance criteria per phase live in the Track 1 milestone; each migrated window must check every IX rule it previously violated in the table above.

---

## Part V — Remediation record (August 2026)

Phases A–E of Milestone 1.5 are applied. Part III above is the pre-remediation snapshot; this
section is the current state. Anything not listed here was fixed exactly as Part III described.

### Machinery introduced

These are the pieces the standard now leans on. New views and dialogs should use them rather than
re-deriving the behaviour:

| Concern | Use | Rule |
|---|---|---|
| Validation | `GUIClient.Validation.ValidationContext` + `this.ValidationRule(...)` / `this.IsValid()`, on `ViewModelBase` | IX-4 |
| Transient feedback | `ViewModelBase.Toasts` (`INotificationService`), rendered by `Views/NotificationHost` | IX-4 |
| Busy indication | `ViewModelBase.IsBusy` / `WithBusyAsync(...)`, bound to a `ProgressRing` | IX-4 |
| Destructive confirmation | `Tools.ConfirmationDialog.ConfirmDeleteAsync` / `ConfirmAsync` | IX-4 |
| Disabled-with-reason tooltips | `Converters.ActionTooltipConverter` (`permission` / `status`) | IX-4 |
| Shell routing & auxiliary windows | `Navigation.INavigationService` | IX-1, IX-7 |
| File pickers from a view-model | `Tools.StorageProviderAccessor.Current` | IX-7 |
| Plain (non-dialog) windows | `Views.AuxiliaryWindowBase` — Esc + geometry persistence | IX-7, IX-8 |
| Manager windows | `Controls.ManagerShell` | IX-5 C |
| Search-row focus | `Behaviors.FocusOnVisible` | IX-8 |

### What the study understated

**Validation was not "invisible" — it was absent.** F3 reported that `ValidationRule`s "only ever
drive `SaveEnabled`". In fact `GUIClient/Validation/ValidationExtensions.cs` was a stub: every
`ValidationRule` returned `Disposable.Empty` and `IsValid()` returned `Observable.Return(true)`.
`ReactiveUI.Validation` had been dropped in commit `4c4abaa5` (February 2026) during the Avalonia /
ReactiveUI startup rework and replaced with no-ops to keep the tree compiling, so for six months no
declared rule in any of the 15 validating view-models gated anything. The rules are now enforced by
an in-tree `ValidationContext`; the consequence is that dialogs whose rules genuinely fail will now
disable Save where they previously did not, **including on legacy records that do not satisfy the
rules** (an existing host with no FQDN, for instance). That is the intended behaviour, but it is a
visible behaviour change rather than a pure refactor.

### Deliberate deviations

- **IX-8 live filtering on VulnerabilitiesView.** The vulnerability register filters *server-side*
  and pages, so filtering per keystroke would issue a REST round-trip per character. Ctrl+F reveals
  and focuses the filter box as the rule requires, and Enter applies it. List views that filter
  client-side (Entities, Incidents, Risks, Hosts) do live-filter as you type.
- **IX-3 on the IRP plan editor.** It keeps Save-without-closing, because a plan has to exist before
  tasks can be added to it. IX-3 allows this for "editors that must stay open for repeated
  operations"; the dialog records what it committed and reports it in its typed result on close.
- **IX-6 next-step mapping.** Only two of the four seeded `next_step` values imply an in-app stage
  ("Consider for Project" → plan mitigation, "Reject" → close). "Accept until Next Review" is
  deliberately unmapped pending the Track 8.1 acceptance flow, and "Submit as a Production Issue"
  has no NetRisk stage. See `RiskHelper.GetNextStepAction` and its tests.

### Known remainders

- **`docs/ui-standard.md` lint debt.** `./build.sh LintUi` still reports pre-existing violations
  outside this milestone's scope: 16 R1 (hard-coded hex), 4 R4 (named brushes), 26 R5 (hard-coded
  strings) and 116 R6 (unclassed buttons — many are false positives, since the linter matches per
  line and a `<Button` whose `Classes` sits on the next line is flagged). These belong to
  UI-STD-001, not to IX-1…IX-9.
- **Limited automated coverage of the GUI.** A `GUIClient.Tests` project now covers the
  validation layer (13 tests) and `RiskHelper.GetNextStepAction` is covered in
  `ServerServices.Tests/Track1`. The view-models themselves remain untested — they depend on
  Avalonia and on a live REST client, so covering them needs the mock-service scaffolding the
  other tiers have, which is separate work.
- **Not exercised at runtime in the remediation session.** The changes build clean with no
  warnings and the test suite passes, but the GUI could not be launched to click through
  (macOS refuses window-server access to a non-interactive shell: Avalonia fails with
  `RenderTimer ... -6661`). A manual pass over the migrated dialogs is worth doing before release.

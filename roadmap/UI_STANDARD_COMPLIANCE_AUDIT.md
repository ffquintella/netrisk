# UI Standard Compliance Audit — closing evidence for UI-STD-001

Date: 2026-09-10
Standard: [`docs/ui-standard.md`](../docs/ui-standard.md)
Scope: `src/GUIClient/Views/**/*.axaml` — **80 files**
Method: `./build.sh LintUi`, whose rule engine is
[`build/NetRisk.Packaging/UiStandardLinter.cs`](../build/NetRisk.Packaging/UiStandardLinter.cs) and
whose rules are unit-tested in
[`src/Packaging.Tests/UiStandardLinterTest.cs`](../src/Packaging.Tests/UiStandardLinterTest.cs).

This is the follow-up audit the UI-STD-001 acceptance criteria ask for. It supersedes the
pass/fail summary in [`UI_STANDARD_AUDIT.md`](UI_STANDARD_AUDIT.md) (2026-04-29) and records the
end state of the plan in [`UI_STANDARD_COMPLIANCE_PLAN.md`](UI_STANDARD_COMPLIANCE_PLAN.md).

## Result

**Overall status: PASS.** `./build.sh LintUi` reports **0 violations across 80 views**, and the
same check now runs in CI ([`.github/workflows/ui-compliance.yml`](../.github/workflows/ui-compliance.yml))
where any violation fails the build.

```
UI Standards Audit: 80 view(s) scanned, 0 violation(s) (none).
```

## What the linter checks, and what changed about it

The previous linter matched **line by line**. A `<Button` whose `Classes` attribute sat on the
following line — the dominant formatting in this codebase — was therefore reported as an unclassed
button. The engine was rewritten to scan **whole start tags**, so an attribute is found wherever it
is wrapped. Two consequences, both measured rather than assumed:

- The old R6 count of 58 was mostly noise: 11 of those reports were multi-line false positives
  (`AssessmentView`, `EditRiskWindow`, `EditMgmtReview`, `EditVulnerabilitiesDialog`,
  `AddFaceImage`, `FileReports`, `AssessmentView`'s three toolbar buttons). Those views were
  already compliant.
- The rewrite also made R1 and R5 see attributes they previously missed, so the true baseline was
  *higher* than the old total in three of the four rules. R5 in particular reported **0** before —
  its regex only matched a `<TextBlock … Text="…">` written on one line — while 45 genuine
  unlocalized strings were present, including 20 window titles that showed a class name
  (`Title="AdminWindow"`, `Title="CloseDialog"`, `Title="VulnerabilityFixChatDialog"`) in the
  title bar.

| Rule | Standard | What it flags |
| --- | --- | --- |
| R1 | §2.6 | Hex literal in `Background` / `Foreground` / `BorderBrush` |
| R4 | §2.6 | Named status brush (`Red`, `Green`, `Orange`, `Azure`, …) |
| R5 | §3.2 | Literal user-facing copy in `Text`, `Content`, `Title`, `Header`, `ToolTip.Tip`, `Watermark` |
| R6 | §4 | `Button` with no style class (or explicit `Theme`) |
| R0 | — | A `ui-lint-waive` comment with no written reason |

## Baseline and remediation

Measured with the rewritten engine on the pre-remediation tree:

| Rule | Baseline | After | Fixed | Waived |
| --- | ---: | ---: | ---: | ---: |
| R1 — hard-coded hex colors | 12 | 0 | 12 | 0 |
| R4 — named brushes | 2 | 0 | 2 | 0 |
| R5 — unlocalized strings | 45 | 0 | 41 | 4 |
| R6 — unclassed buttons | 47 | 0 | 47 | 0 |
| **Total** | **106** | **0** | **102** | **4** |

35 of the 80 views carried at least one violation; 45 were already clean.

### How each rule was closed

- **R1 / R4 — colors.** No color literal was moved to another literal. Four new classes were added
  to [`Styles/WindowStyles.axaml`](../src/GUIClient/Styles/WindowStyles.axaml) —
  `TextBlock.errorText`, `TextBlock.warningText`, `avalonia|MaterialIcon.warning`, `Border.outline`
  and `Border.badge` — each reusing a hex already in that file (the validation-summary red
  `#ff8a80`, the toast amber `#ff9800`, the card border `#4a4a4a`, the brand accent `#51496b`)
  rather than introducing a new token. The three assessment views and
  `Reports/EditReportTemplateDialog` now reference those classes. `Foreground="Green"` on a
  completion tick became the existing `MaterialIcon.success` class; `Foreground="Red"` on a status
  line became `TextBlock.errorText`.
- **R5 — strings.** 17 keys were added to **all three** `Localization*.resx` files (invariant,
  `en-US`, `pt-BR`), and 20 window titles plus 21 inline labels/tooltips were bound to `Str*`
  view-model properties. Titles that previously read as class names now read as the window's
  purpose.
- **R6 — buttons.** Every remaining button was given a class from the §4.1 taxonomy, chosen with
  the §4.2 decision matrix: `subButton` for the attachment-row download/delete/add micro-actions
  (six views), `operation` for the vulnerability pager, `type2`/`type3` for the report
  generate/export/panel actions, `dialog1`/`dialog2` for the two report dialogs' commit rows,
  `nav-base` for the assessment page rail, `type2` for the account-panel logout. Where a button
  also carried inline colors (`Background="DarkSlateBlue" Foreground="White"`), the literals were
  dropped in favour of the class.

## Waivers

Waivers are declared in the markup and validated by the linter: a waiver must name its rules and
carry a reason, and a waiver with no reason is itself reported (R0) and still fails the build. A
waiver applies to the single element that follows it.

| View | Element | Rule | Reason |
| --- | --- | --- | --- |
| `MainWindow.axaml` | `NativeMenuItem Header="IRP - Create"` | R5 | Debug-only menu behind `IsDebug` — the exception `docs/ui-standard.md` §3.2 states explicitly; unreachable in a release build |
| `MainWindow.axaml` | `NativeMenuItem Header="IRP Task - Create"` | R5 | Same debug menu (macOS native menu bar) |
| `MainWindow.axaml` | `MenuItem Header="IRP - Create"` | R5 | Same debug menu (in-window menu, non-macOS) |
| `MainWindow.axaml` | `MenuItem Header="IRP Task - Create"` | R5 | Same debug menu (in-window menu, non-macOS) |

All four are the same two debug commands, duplicated because the menu exists twice — once as a
macOS `NativeMenu` and once as an in-window `Menu`. `NativeMenuItem` was **added** to the R5
element list while closing this item, precisely so the macOS copy could not hide behind a gap in
the rule; the honest consequence is two more waivers rather than two fewer findings.

No rule was loosened and no assertion was removed to reach zero.

## Defects found while auditing, and not fixed — now closed

The linter proves a button *has* a class. It cannot prove the class *exists* — Avalonia silently
ignores a style selector that matches nothing, so a misspelled or never-written class compiles,
runs, and renders an unstyled control. Auditing for that found **four dangling class references
across four views**, all pre-existing. **All four are now fixed**, and the allowlist that recorded
them is empty:

| View | Reference | Effect | Resolution |
| --- | --- | --- | --- |
| `EditMgmtReview.axaml:21` | `Panel Classes="EditTitle"` | No `Panel.EditTitle` style exists anywhere in `Styles/*.axaml`; the title row renders as a bare panel — no band, no padding | Panel replaced by the documented `TextBlock.header` band (§3.1) |
| `EditMitigationWindow.axaml:36` | `Panel Classes="EditTitle"` | Same | Same |
| `RiskGovernanceWindow.axaml:26` | `Panel Classes="EditTitle"` | Same | Same, keeping `TextWrapping="Wrap"` for the long risk subject |
| `VulnerabilityImportWindow.axaml:66` | `TextBlock Classes="subHeader"` | No `TextBlock.subHeader` style exists; the warnings caption renders as plain body text instead of a sub-header | Re-classed to `header3` (§3.1 — bold italic, no background) |

The decision each needed was whether to *define* the missing classes or to adopt the documented
ones. Each of the three `EditTitle` panels wrapped exactly one `TextBlock`, and the two comparable
edit windows — [`EditRiskWindow`](../src/GUIClient/Views/EditRiskWindow.axaml) and
[`EditIncidentWindow`](../src/GUIClient/Views/EditIncidentWindow.axaml) — already put a plain
`TextBlock Classes="header"` on row 0, which is exactly what §2.5 prescribes for a full window
header. So the panels were collapsed into that same one-line form rather than a `Panel.EditTitle`
style being invented: **no new class and no new hex** were added to `WindowStyles.axaml`. For the
warnings caption, the surrounding markup is a caption above a monospace warning list inside a
scroller, which wants an inline heading with no background band — `header3`.

- `GUIClient.Tests/Views/StyleClassReferenceTest` fails on **any** dangling class reference. Its
  allowlist — the same convention as `LocalizationCoverageTest`'s pre-existing list — is now empty,
  so the guard is unconditional. Two further tests fail if an allowlist entry becomes stale (the
  class gets defined, or stops being referenced) so the list cannot rot. Reverting the four view
  edits reproduces the failure, naming all four references.

## Per-file record

`Baseline` is what the rewritten linter found before remediation; every file's current status is
PASS.

| View | Status | Baseline findings | Disposition |
| --- | --- | --- | --- |
| `AboutWindow.axaml` | PASS | R5×1 | remediated |
| `Admin/AddFaceImage.axaml` | PASS | — | clean at baseline |
| `Admin/ApiTokensView.axaml` | PASS | — | clean at baseline |
| `Admin/EntityAccessView.axaml` | PASS | — | clean at baseline |
| `Admin/FindingsAdminView.axaml` | PASS | — | clean at baseline |
| `Admin/GovernanceAdminView.axaml` | PASS | — | clean at baseline |
| `Admin/IntegrationsView.axaml` | PASS | — | clean at baseline |
| `Admin/IrpTemplatesView.axaml` | PASS | — | clean at baseline |
| `Admin/JiraIntegrationView.axaml` | PASS | — | clean at baseline |
| `Admin/PluginsView.axaml` | PASS | — | clean at baseline |
| `AdminWindow.axaml` | PASS | R5×1 | remediated |
| `AssessmentView.axaml` | PASS | — | clean at baseline |
| `Assessments/AssessmentBuilderView.axaml` | PASS | R1×6, R6×1 | remediated |
| `Assessments/AssessmentImportDialog.axaml` | PASS | R1×4, R5×1 | remediated |
| `Assessments/AssessmentRunDialog.axaml` | PASS | R5×1 | remediated |
| `Assessments/AssessmentRunViewer.axaml` | PASS | R1×2, R4×1, R5×1, R6×1 | remediated |
| `Assessments/AssessmentsRunsListView.axaml` | PASS | — | clean at baseline |
| `ChangePasswordDialog.axaml` | PASS | R5×1 | remediated |
| `CloseDialog.axaml` | PASS | R5×1 | remediated |
| `CloseRiskWindow.axaml` | PASS | — | clean at baseline |
| `ConfigurationView.axaml` | PASS | — | clean at baseline |
| `DashboardView.axaml` | PASS | — | clean at baseline |
| `DeviceView.axaml` | PASS | — | clean at baseline |
| `EditEntityDialog.axaml` | PASS | R5×1 | remediated |
| `EditHostDialog.axaml` | PASS | R5×3 | remediated |
| `EditIncidentWindow.axaml` | PASS | R6×3 | remediated |
| `EditMgmtReview.axaml` | PASS | — | clean at baseline |
| `EditMitigationWindow.axaml` | PASS | R6×3 | remediated |
| `EditRiskWindow.axaml` | PASS | R5×1 | remediated |
| `EditSingleStringDialog.axaml` | PASS | — | clean at baseline |
| `EditVulnerabilitiesDialog.axaml` | PASS | — | clean at baseline |
| `EntitiesView.axaml` | PASS | — | clean at baseline |
| `EntityForm.axaml` | PASS | — | clean at baseline |
| `FindingStatusDialog.axaml` | PASS | — | clean at baseline |
| `FixRequestDialog.axaml` | PASS | R5×1 | remediated |
| `HostsView.axaml` | PASS | R5×2 | remediated |
| `IncidentResponsePlanTaskWindow.axaml` | PASS | — | clean at baseline |
| `IncidentResponsePlanWindow.axaml` | PASS | R6×3 | remediated |
| `IncidentsView.axaml` | PASS | R6×1 | remediated |
| `IrpGanttWindow.axaml` | PASS | — | clean at baseline |
| `LoadConfigurationWindow.axaml` | PASS | — | clean at baseline |
| `LoginWindow.axaml` | PASS | R5×2 | remediated |
| `MainWindow.axaml` | PASS | R5×5 | 1 remediated, 4 waived |
| `MasterDashboardView.axaml` | PASS | — | clean at baseline |
| `NavigationBar.axaml` | PASS | — | clean at baseline |
| `NotificationHost.axaml` | PASS | — | clean at baseline |
| `NotificationsWindow.axaml` | PASS | R5×1, R6×2 | remediated |
| `Reports/CreateReportDialog.axaml` | PASS | R5×1 | remediated |
| `Reports/EditReportScheduleDialog.axaml` | PASS | R5×3, R6×4 | remediated |
| `Reports/EditReportTemplateDialog.axaml` | PASS | R4×1, R5×3, R6×11 | remediated |
| `Reports/EntitiesRisks.axaml` | PASS | R6×2 | remediated |
| `Reports/FileReports.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/BusinessProcessRisks.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/RisksGroups.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/RisksNumbers.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/RisksStats.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/SlaCompliance.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/VulnerabilitiesDistribution.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/VulnerabilitiesStats.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/VulnerabilitiesVerified.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/VulnerabilityImportSources.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/VulnerabilityImports.axaml` | PASS | — | clean at baseline |
| `Reports/Graphs/VulnerabilityNumbers.axaml` | PASS | — | clean at baseline |
| `Reports/ReportScheduleManagerWindow.axaml` | PASS | — | clean at baseline |
| `Reports/ReportTemplateManagerWindow.axaml` | PASS | — | clean at baseline |
| `Reports/RiskReview.axaml` | PASS | R6×2 | remediated |
| `Reports/RisksImpactVsProbability.axaml` | PASS | R6×2 | remediated |
| `Reports/RisksVsCosts.axaml` | PASS | R6×2 | remediated |
| `Reports/VulnerabilitiesByTime.axaml` | PASS | R6×2 | remediated |
| `ReportsWindow.axaml` | PASS | R5×3, R6×2 | remediated |
| `RiskGovernanceWindow.axaml` | PASS | — | clean at baseline |
| `RiskView.axaml` | PASS | R5×3, R6×5 | remediated |
| `SecretVaultPickerDialog.axaml` | PASS | — | clean at baseline |
| `UpgradeWindow.axaml` | PASS | R5×1 | remediated |
| `UserInfo.axaml` | PASS | R5×2, R6×1 | remediated |
| `UsersView.axaml` | PASS | — | clean at baseline |
| `VerifyFaceID.axaml` | PASS | R5×1 | remediated |
| `VulnerabilitiesView.axaml` | PASS | R5×4 | remediated |
| `VulnerabilityFixChatDialog.axaml` | PASS | R5×1 | remediated |
| `VulnerabilityImportWindow.axaml` | PASS | — | clean at baseline |

## Runtime verification

AXAML resource-key and binding mistakes compile cleanly and fail at runtime, so a green build is
not evidence. Three layers of verification were used, in increasing strength:

1. **Compile.** Compiled bindings (`AvaloniaUseCompiledBindingsByDefault=true`) reject a
   `{Binding StrX}` whose property does not exist — that is how the missing
   `VulnerabilitiesViewModel.StrExport` was caught. Every new binding in this change is therefore
   proved to resolve to a real view-model member.
2. **Test.** `GUIClient.Tests/Resources/LocalizationCoverageTest` asserts every
   `Localizer["Key"]` in the client resolves in **all three** shipped resource files, so the 17 new
   keys cannot render as their own key name. `StyleClassReferenceTest` (added here) asserts every
   `Classes="…"` token is defined by some style.
3. **Run.** The GUI was launched from `src/GUIClient` with `-- --environment=dev` and both windows
   captured with `screencapture -l <windowId>`. The window-server window list read:

   ```
   num=26194 pid=95641 owner=GUIClient title=Entrar   bounds=400x250
   num=26189 pid=95641 owner=GUIClient title=NetRisk  bounds=2111x1218
   ```

   Both titles are the newly bound ones resolving through the localizer at run time under a pt-BR
   culture: the login window reads **Entrar** where it used to read the hard-coded `Login`, and the
   shell reads **NetRisk** from the new `NetRiskApplication` key. The login window rendered
   correctly — localized labels, the SSO icon button with its new localized tooltip, and the
   `dialog1`/`dialog2` action pair — and the shell rendered its dashboard, header bands and
   navigation icons intact.

**Limit of this evidence, stated plainly.** The login screen cannot be driven without the user's
password, so the views behind authentication (`RiskView`, `VulnerabilitiesView`, the two report
dialogs, the attachment rows) were **not** observed on screen. For those, verification rests on
layers 1 and 2 above plus the fact that every change to them was a class addition or a literal →
binding swap, neither of which can fail at runtime once the binding compiles and the class is
proved to exist. Anyone with a working login should re-check those four screens visually.

## Standing gate

- `./build.sh LintUi` — fails on any violation. `Compile` depends on it, so a local build catches
  a new deviation before it is pushed.
- [`.github/workflows/ui-compliance.yml`](../.github/workflows/ui-compliance.yml) — runs the linter
  and the linter's own unit tests on every push and PR.
- [`.github/pull_request_template.md`](../.github/pull_request_template.md) — the **UI compliance**
  checklist section, which asks for the linter result, the taxonomy/localization checks, any
  waiver, and confirmation the window was actually looked at.
- `Packaging.Tests.UiStandardLinterTest.EveryGuiClientView_IsCompliantWithTheUiStandard` — the same
  zero-violation assertion from `dotnet test`, so the gate holds even if the Nuke target is
  bypassed.

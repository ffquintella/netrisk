# UI Standard Compliance Plan

Date: 2026-04-29
Standard: `docs/ui-standard.md`
Audit baseline: `roadmap/UI_STANDARD_AUDIT.md`
Scope: `src/GUIClient/Views/**/*.axaml`, related styles/resources, view-model title/string contracts, and supporting localization resources

## Goal

Bring the Avalonia UI into practical compliance with `docs/ui-standard.md` by removing legacy markup drift, standardizing shared patterns, and re-auditing the full GUI view set after remediation.

## Approach

The work should be executed in layered passes so shared infrastructure is fixed before individual screens are normalized:

1. Stabilize shared styles, classes, and localization patterns.
2. Fix the highest-risk windows and dialogs that violate multiple core rules.
3. Normalize list/panel views and navigation surfaces.
4. Finish remaining edit/detail views and reports.
5. Re-run the audit and close any remaining deviations.

## Workstreams

### 1. Shared foundation

- Review `GUIClient` style resources and confirm the standard taxonomy exists and is sufficient.
- Add missing reusable classes in style files instead of introducing new inline colors, font settings, or button variants in views.
- Identify missing `WindowTitle` bindings and `Str*` properties that must be exposed by view-models.
- Map any missing localized strings that must be added across `.resx` files.

### 2. High-risk window and dialog remediation

Prioritize the views with the densest violations and the greatest pattern reuse:

- `LoginWindow.axaml`
- `CloseDialog.axaml`
- `FixRequestDialog.axaml`
- `EditRiskWindow.axaml`
- `AssessmentQuestionView.axaml`
- `teste.axaml` (remove or explicitly replace)

Focus areas:

- Replace hard-coded window titles with `WindowTitle`.
- Replace inline colors and typography with shared classes/resources.
- Replace fixed-width controls with responsive layouts using `Grid`, `Auto`, `*`, and `MinWidth`.
- Convert action rows to canonical button classes and ordering.
- Preserve acrylic shell and dialog layout requirements from the standard.

### 3. Navigation and list/panel normalization

Primary targets:

- `NavigationBar.axaml`
- `UsersView.axaml`
- `VulnerabilitiesView.axaml`
- `RiskView.axaml`
- Other list-heavy views with toolbar/button drift

Focus areas:

- Convert icon-only and toolbar buttons to the standard button taxonomy.
- Remove hard-coded notification/status colors from markup where a shared style or canonical status icon is required.
- Normalize headers, details panes, and filter rows.
- Bring list/detail layouts into the standard panel structure.

### 4. Remaining form, edit, and report views

- Sweep the remaining edit/detail windows, admin screens, assessment screens, and report views.
- Remove remaining literal user-facing strings from `.axaml`.
- Normalize form labels, spacing, and responsive sizing.
- Check DataGrid declarations for explicit headers, interaction flags, and standard layout expectations.

### 5. Verification and closure

- Re-run the UI audit across all 62 scoped views.
- Record per-rule and per-file status in a refreshed audit.
- Track intentional exceptions explicitly if any legacy deviation must remain temporarily.
- Mark `UI-STD-001` complete only after the audit passes or all remaining deviations are documented and approved.

## Execution Order

1. Shared styles/resources and localization contract updates
2. Remove `teste.axaml`
3. Login and core dialogs/windows
4. Navigation and major list/detail views
5. Remaining edit/report views
6. Full re-audit and cleanup

## Proposed Todos

1. **ui-foundation-pass** - review styles, missing classes, title bindings, and localization gaps
2. **ui-remove-teste-view** - remove or replace `GUIClient/Views/teste.axaml`
3. **ui-dialog-window-pass** - fix `LoginWindow`, `CloseDialog`, `FixRequestDialog`, `EditRiskWindow`, `AssessmentQuestionView`
4. **ui-navigation-list-pass** - normalize `NavigationBar`, `UsersView`, `VulnerabilitiesView`, `RiskView`, and similar list views
5. **ui-remaining-views-pass** - sweep all remaining scoped views for strings, colors, sizing, and button taxonomy
6. **ui-final-audit-pass** - re-run verification and update the audit artifact

## Dependencies

- `ui-remove-teste-view` depends on `ui-foundation-pass`
- `ui-dialog-window-pass` depends on `ui-foundation-pass`
- `ui-navigation-list-pass` depends on `ui-foundation-pass`
- `ui-remaining-views-pass` depends on `ui-dialog-window-pass` and `ui-navigation-list-pass`
- `ui-final-audit-pass` depends on all prior remediation todos

## Notes

- The audit counts are static and conservative; expect more issues to surface during manual remediation.
- Localization work may span multiple resource files and should be batched to avoid partial coverage.
- Shared style additions should be minimal and justified by repeated use; the default fix is reuse, not expansion.
- Any runtime-only behavior not visible in AXAML should be checked while touching the affected screen, especially title binding, command naming, and progress/loading behavior.

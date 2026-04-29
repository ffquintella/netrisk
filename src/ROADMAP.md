# Roadmap

## UI Standards

### UI-STD-001 - Bring all GUI interfaces into compliance with `docs/ui-standard.md`

- Status: Planned
- Priority: High
- Scope: `GUIClient/Views/**/*.axaml` (62 files)
- Source audit: `roadmap/UI_STANDARD_AUDIT.md`
- Implementation plan: `roadmap/UI_STANDARD_COMPLIANCE_PLAN.md`

#### Why

The source standard now exists at `docs/ui-standard.md`. A refreshed static audit of `GUIClient/Views/**/*.axaml` on 2026-04-29 confirmed broad divergence from it: 23 files with hard-coded color literals, 39 with literal user-facing strings, 29 with hard-coded window titles, 28 button-class deviations, and 18 fixed-width input/layout violations.

#### Deliverables

- Use `docs/ui-standard.md` as the source of truth for remediation.
- Produce a complete pass/fail matrix for all 62 view files against each rule.
- Refactor all non-compliant views to use standard tokens/styles/components.
- Remove or explicitly deprecate legacy test view `GUIClient/Views/teste.axaml`.
- Add a UI compliance checklist to PR template (or CI validation step for XAML style rules).

#### Acceptance Criteria

- Every file under `GUIClient/Views/**/*.axaml` has a recorded compliance status.
- No unapproved hard-coded colors, spacing, or typography values remain.
- Reusable style classes/resources are used where required by `ui-standard.md`.
- Verification evidence is attached in a follow-up audit document.

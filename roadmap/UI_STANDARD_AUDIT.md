# UI Standard Audit

Date: 2026-04-29
Standard: `docs/ui-standard.md`
Scope: `src/GUIClient/Views/**/*.axaml` (62 files)
Method: static AXAML-only verification against rules that can be measured directly from markup

## Audit Result

**Overall status: FAIL.** The previous audit was blocked by a bad path assumption; the standard is present under `docs/ui-standard.md`, and the refreshed verification shows widespread drift across the current GUI views.

## Rule Summary

| Rule | Standard reference | Result | Violating files |
| --- | --- | --- | ---: |
| No hard-coded color literals in view markup (`Background`, `Foreground`, `BorderBrush`, `Fill`, `Stroke`, `TintColor`) | §§2.6, 10 | FAIL | 23 |
| No literal user-facing strings in `.axaml`; strings must come from `Str*` resource bindings | §§3.2, 10 | FAIL | 39 |
| Window titles should be bound via `Title="{Binding WindowTitle}"` | §§9, 11 | FAIL | 29 |
| Button-bearing views should use the canonical button class taxonomy (`dialog1`, `dialog2`, `type2`, `type3`, `operation`, `subButton`, `detailButton`, `filterButton`, `navigation`, `link`) | §4 | FAIL | 28 of 43 button-bearing files |
| Forms should prefer responsive sizing (`MinWidth` + flexible columns) over fixed-width inputs | §§5.2, 5.8, 6.2 | FAIL | 18 |

## Highest-Risk Files

- `src/GUIClient/Views/AssessmentQuestionView.axaml` - hard-coded title, repeated `#222222`, fixed-width inputs, unclassed action buttons, literal DataGrid headers (`ID`, `Score`)
- `src/GUIClient/Views/UsersView.axaml` - repeated `#222222`, `Azure`, `Green`, `Red`; literal `FaceID`; several fixed-width combos; unclassed content buttons
- `src/GUIClient/Views/VulnerabilitiesView.axaml` - many operation-style buttons without canonical classes; fixed-width filter/input controls; literal details labels
- `src/GUIClient/Views/RiskView.axaml` - hard-coded `#666666`, `CornflowerBlue`, literal labels (`ID:`, `Ctrl #:`, `Dt.`, `IRP`, `IRP Dt.`), `FontWeight="Bold"`, unclassed IRP action buttons
- `src/GUIClient/Views/LoginWindow.axaml` - hard-coded `Title="Login"`, fixed-width inputs, unclassed buttons, spacer rectangles called out by the standard backlog
- `src/GUIClient/Views/CloseDialog.axaml` - hard-coded `Title="CloseDialog"`, fixed-width combo, literal `Save`/`Cancel`, unclassed dialog buttons
- `src/GUIClient/Views/FixRequestDialog.axaml` - hard-coded `Title="SendEmailDialog"`, `Background="Black"`, several fixed-width controls, unclassed send/cancel buttons
- `src/GUIClient/Views/EditRiskWindow.axaml` - hard-coded `Title="EditRisk"`, `Background="DimGray"`, `Foreground="CornflowerBlue"`, literal `ID:` / `Ctrl #:`, inline `FontSize="28"`, fixed-width scoring combos
- `src/GUIClient/Views/NavigationBar.axaml` - hard-coded colors (`#181918`, `OrangeRed`, `WhiteSmoke`), inline `FontSize`, icon-only buttons without canonical classes
- `src/GUIClient/Views/teste.axaml` - scratch window explicitly listed in the standard backlog for removal

## What This Verification Confirms

- The audit is no longer blocked; `docs/ui-standard.md` is available and usable.
- The dominant remediation themes are localization, tokenized colors, button normalization, and responsive sizing.
- The standard's existing migration backlog is still accurate: `LoginWindow.axaml` and `teste.axaml` remain known deviations, and older dialogs still miss canonical button classes.

## Limitations

- This was a static AXAML review only. It does not score code-behind behavior, resource completeness across `.resx` files, or runtime shell behavior.
- Some semantic rules from the standard still need manual review, including acrylic shell continuity, canonical dialog action ordering, DataGrid interaction flags, and icon/action consistency.
- Literal text embedded as element content is harder to detect mechanically than attribute values, so the string-violation count is conservative.

## Recommended Next Remediation Order

1. Remove or replace `teste.axaml`.
2. Normalize shared shells and titles in windows/dialogs (`LoginWindow`, `CloseDialog`, `FixRequestDialog`, `EditRiskWindow`).
3. Replace hard-coded colors and typography with existing classes/resources.
4. Convert legacy buttons to the canonical class taxonomy.
5. Replace fixed-width form controls with responsive layouts.

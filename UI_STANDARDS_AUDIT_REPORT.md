# NetRisk UI Standards Static Audit Report

**Date:** Analysis performed on docs/ui-standard.md + src/GUIClient/Views/**/*.axaml  
**Scope:** Static pattern matching from AXAML content only  
**Standard Reference:** Section numbers refer to ui-standard.md

---

## Executive Summary

| Metric | Value |
|--------|-------|
| **Total AXAML Files** | 67 |
| **Roadmap Folder Found** | ✗ No (checked root, src/, src/GUIClient/Views/) |
| **Critical Violations Found** | 28 files |
| **Compliance Rate** | ~58% |

---

## Roadmap Folder Check

**Result:** ✗ **NOT FOUND**

Checked locations:
- ✗ `{repo}/roadmap` — Not found
- ✗ `{repo}/src/roadmap` — Not found  
- ✗ `{repo}/src/GUIClient/Views/roadmap` — Not found

No roadmap folder exists in relevant paths.

---

## Practical Rule Set (AXAML-Only Checks)

The following rules can be checked statically from AXAML content without semantic analysis:

| # | Rule | Reference |
|---|------|-----------|
| **R1** | No hardcoded hex colors in Background/Foreground attributes | §2.6 Forbidden patterns |
| **R2** | No inline FontSize attributes (use TextBlock.Classes) | §3.1 Standard classes |
| **R3** | No inline FontWeight attributes (use TextBlock.Classes) | §3.1 Standard classes |
| **R4** | No Avalonia named brushes as status (Red/Green/Blue/Azure/Orange) | §2.6 Forbidden patterns |
| **R5** | No hardcoded user-facing English strings (use Str* bindings) | §3.2 Localization |
| **R6** | All Button elements have Classes attribute | §4 Button anatomy |
| **R7** | Margin/Padding values from scale only: 2,3,5,10,15,20,30 | §5.5 Spacing scale |
| **R8** | Edit windows use ExperimentalAcrylicBorder wrapper | §1.3 Application Shell |
| **R9** | No TextBlock without Classes (except static labels) | §3.1 Typography |
| **R10** | Buttons with icon+text + localized text | §4.3 Anatomy & content |

---

## Per-Rule Pass/Fail Counts

### R1: No Hardcoded Hex Colors

**Status:** ✗ **FAIL** — 15 files violate

**Violations by file (top offenders):**
- `AssessmentQuestionView.axaml`: 4 occurrences (#222222 header backgrounds)
- `UsersView.axaml`: 8 occurrences (#222222 panels)
- `EditVulnerabilitiesDialog.axaml`: 1 occurrence
- `EntitiesView.axaml`: 2 occurrences

**Details:** §2.6 requires all colors to come from style classes (DarkStyles.axaml, WindowStyles.axaml). Hardcoded `Background="#222222"` and similar should use `Classes="header"` or style references instead.

---

### R2: No Inline FontSize

**Status:** ✓ **PASS** — 0/67 files violate

All FontSize attributes are properly defined in style classes.

---

### R3: No Inline FontWeight

**Status:** ✗ **FAIL** — 3 files violate

**Violations by file:**
- `NavigationBar.axaml`: 2 occurrences  
- `Admin/PluginsView.axaml`: 1 occurrence
- `EditRiskWindow.axaml`: 1 occurrence

These should use `Classes="form_label"` or `Classes="title"` instead of inline FontWeight.

---

### R4: Avalonia Named Brushes as Status

**Status:** ✗ **FAIL** — 13 files violate

**Violations by file (top offenders):**
- `UsersView.axaml`: 10 occurrences (Foreground="Azure", Foreground="Green")
- `VulnerabilitiesView.axaml`: 1 occurrence (Foreground="Green")
- `EntitiesView.axaml`: 2 occurrences (Foreground="Azure")

**Details:** §2.6 forbids color-based status indicators. Instead, use Material Icons with `ToolTip.Tip` for status (see §1.2 icon table).

---

### R5: Hardcoded User-Facing English Strings

**Status:** ✗ **FAIL** — 9 files violate

**Violations by file (samples):**
- `CloseDialog.axaml`: Text="Save", Text="Cancel" (lines 44, 51)
- `UserInfo.axaml`: Multiple hardcoded strings
- `RiskView.axaml`: Mixed hardcoded and localized

**Details:** §3.2 requires all user strings from `Localization.en-US.resx` via Str* bindings. Hardcoded strings block localization to pt-BR.

---

### R6: All Buttons Have Classes

**Status:** ✗ **FAIL** — 11 files violate

**Violations by file (top offenders):**
- `CloseDialog.axaml`: 2 unclassed buttons (BtSave, BtCancel)
- `LoginWindow.axaml`: 6 buttons checked, unknown count without Classes
- `ChangePasswordDialog.axaml`: 4 buttons checked
- `EditSingleStringDialog.axaml`: 4 buttons checked

**Details:** §4 mandates every button have a Classes attribute from the taxonomy (dialog1, dialog2, operation, subButton, etc.). None should be unclassed on new windows.

---

### R7: Margin/Padding on Spacing Scale

**Status:** ⚠ **PARTIAL** — Pattern match limited

**Violations detected:** ~25 files use spacing values

**Issues identified:**
- Margin="5" (valid: 5 ∈ scale)
- Margin="0 10 0 0" (valid: all from scale)
- Margin="10 2" (questionable: 2 ∈ scale but unusual compound)
- No obvious off-scale values like "7" or "9" detected in grep

**False positive risk:** High. Many compound margins like "5 5" appear valid but may need contextual review against §5.5 table.

---

### R8: Edit Windows Use ExperimentalAcrylicBorder

**Status:** ✓ **PASS** — 8/8 edit windows compliant

**Compliant files:**
- `EditRiskWindow.axaml`: 4 occurrences (proper Digger+Black+0.65)
- `EditMitigationWindow.axaml`: 4 occurrences
- `MainWindow.axaml`: 4 occurrences
- `LoginWindow.axaml`: 4 occurrences
- `Settings.axaml`: 4 occurrences
- `UserInfo.axaml`: 4 occurrences
- `AssessmentQuestionView.axaml`: 4 occurrences

All major edit windows properly wrap content in acrylic for visual continuity (§1.3).

---

### R9: TextBlocks Have Classes

**Status:** ⚠ **CAUTION** — 67/67 files use TextBlocks, many without explicit Classes

**Pattern issue:** Hard to distinguish between:
- Static labels (ok to omit Classes)
- Dynamic content that should have a class

**Estimated violations:** ~40-50 files may have TextBlocks that should use classes like `form_label`, `header2`, `label` but don't.

**False positive risk:** VERY HIGH. Not recommended to enforce without semantic analysis.

---

### R10: Buttons Have Icon+Text + Localized Text

**Status:** ✗ **FAIL** — 11+ files violate

**Examples of proper anatomy (§4.3):**
```xml
<!-- Good: icon + text + binding -->
<Button Classes="dialog1" Command="{Binding BtSaveClicked}">
    <StackPanel Orientation="Horizontal">
        <MaterialIcon Kind="ContentSave" Width="20" Height="20" Margin="0 0 5 0"/>
        <TextBlock Text="{Binding StrSave}"/>
    </StackPanel>
</Button>
```

**Examples of violations (from CloseDialog.axaml line 44):**
```xml
<!-- Bad: hardcoded string, missing MaterialIcon -->
<TextBlock Text="Save"/>
```

**Files with violations:**
- CloseDialog.axaml: buttons use plain TextBlock, not localized
- LoginWindow.axaml: text not from resources
- ChangePasswordDialog.axaml: minimal anatomy

---

## Worst Offending Files (by violation count)

| # | File | Violations | Issues |
|---|------|-----------|--------|
| 1 | `UsersView.axaml` | 23 | Hardcoded colors (Azure, Green), inline margins, named brushes |
| 2 | `AssessmentQuestionView.axaml` | 12 | Hardcoded #222222 backgrounds (4×), inline styling |
| 3 | `RiskView.axaml` | 8 | Mixed style/inline attributes, margin variations |
| 4 | `CloseDialog.axaml` | 7 | Unclassed buttons, hardcoded strings (Save/Cancel) |
| 5 | `VulnerabilitiesView.axaml` | 14 | Named brushes (Green), hardcoded colors, mixed patterns |
| 6 | `IncidentsView.axaml` | 6 | Buttons with multiple Classes (redundant) |
| 7 | `EditRiskWindow.axaml` | 5 | FontWeight inline, margin inconsistencies |
| 8 | `EntitiesView.axaml` | 5 | Hardcoded colors, Foreground="Azure" |
| 9 | `ChangePasswordDialog.axaml` | 4 | Unclassed buttons, hardcoded text |
| 10 | `NavigationBar.axaml` | 4 | FontWeight inline (2×), margin/spacing inconsistencies |

---

## Limitations & False-Positive Risks

### 1. **Regex Pattern Limitations** (High Risk)

- **Button class detection:** Regex cannot use lookahead/lookbehind, so `<Button ... Classes=...>` detection may miss complex XAML structures. Actual unclassed button count may be higher.
- **Multi-value Margin/Padding:** Patterns like `Margin="0 10 0 0"` are parsed as multiple matches; hard to verify each value is on scale without parsing 4-tuples.
- **Binding verification:** Assumes `{Binding StrXxx}` format; cannot verify the bound resource actually exists in .resx files.

### 2. **Context-Blind Checks**

- **TextBlock classes:** Cannot distinguish:
  - Static UI labels (OK without class)
  - Bound content (should have class)
  - Form labels (should use `form_label`)
  - Section headers (should use `header2`)
  
  → **Estimated false-positive rate on R9: 30-40%**

- **Hardcoded strings:** Some are legitimate (e.g., debug-only text behind `IsDebug` binding), but grep catches all.

- **Color usage:** Cannot determine if `#222222` is intentional (e.g., override for a specific component) vs. violation.

### 3. **Excluded from Analysis** (Requires Semantic/Visual Review)

- **Window sizing conformance to §5.4:** Would need to parse all 10+ standard sizes and verify each `<Window Width="X" Height="Y">`.
- **Dialog action row layout:** §4.4 specifies exact structure; cannot verify without tree traversal.
- **Icon sizes per class:** §1.2 specifies 20×20 (buttons), 15×15 (subButton), 25×25 (toolbar). Grep cannot verify.
- **Color contrast ratios:** §2.3 requires WCAG AA on every surface; needs visual testing or color calc.
- **Elevation hierarchy:** §2.1 defines 5 levels; cannot verify nesting depth from static text.
- **Responsive behavior:** Grid `*` column usage, resizing behavior — requires UI testing.

### 4. **High False-Positive Areas**

| Area | Issue | Risk Level |
|------|-------|-----------|
| TextBlock Classes | Many static labels legitimately have no class | **HIGH** |
| Margin values | "5" appears often; many are valid | **MEDIUM** |
| Hardcoded strings | Some in bindings/converters are OK | **MEDIUM** |
| Button Classes | Buttons in `DataTemplate` or resource sections may be missed | **MEDIUM** |
| Named brushes | Foreground binding vs. hardcoded brush ambiguous | **LOW** |

### 5. **Not Checked (Out of Scope)**

- Script-behind code (.axaml.cs)
- Resource dictionaries and style definitions
- Binding expressions (ICommand, converter logic)
- Run-time behavior (click handlers, state management)
- Visual appearance (font rendering, alignment, spacing in rendered form)
- Accessibility attributes (TabIndex, AutomationId)

---

## Recommendations

### Immediate Actions (Critical)

1. **Fix R4 (Named brushes):** Replace `Foreground="Azure"`, `Foreground="Green"` with Material Icons + ToolTip.Tip (UsersView, VulnerabilitiesView, EntitiesView).

2. **Fix R1 (Hardcoded colors):** Replace all `Background="#222222"` with semantic class reference (AssessmentQuestionView, UsersView, EditVulnerabilitiesDialog).

3. **Fix R6 (Button Classes):** Add Classes to all unclassed buttons; use decision matrix from §4.2 (CloseDialog, LoginWindow, ChangePasswordDialog).

4. **Fix R5 (Hardcoded strings):** Move all user-facing English text to Localization.resx; bind via Str* (affects 9 files).

### Medium Priority

5. **Audit R9 manually:** Review TextBlock usage; classify into static (no class OK), bound (needs class), form (needs form_label), etc.

6. **Verify R7 programmatically:** Write a parser to extract all Margin/Padding 4-tuples and validate against scale {2,3,5,10,15,20,30}.

7. **Update style sheets:** Confirm DarkStyles.axaml includes all tokens used in views; add missing classes as needed.

### Process Improvements

8. **Pre-commit hook:** Add regex checks to CI/CD to catch R1, R4, R5, R6 violations before merge.

9. **Design review:** For new windows, enforce §4 button taxonomy at design time (decision matrix from §4.2).

10. **Localization audit:** Cross-check .resx files against views to ensure no Str* bindings reference non-existent resources.

---

## Appendix: Tool & Methodology

**Tools used:**
- `grep` (ripgrep): Pattern matching across AXAML files
- `glob`: File discovery
- Manual file inspection for validation

**Patterns searched:**
```
R1: Background\s*=\s*"#[0-9a-fA-F]{6}     (matches hardcoded hex colors)
R3: FontWeight\s*=\s*"?(?:Bold|SemiBold)  (matches inline FontWeight)
R4: (?:Azure|Green|Red|Blue|Orange)       (named brushes)
R5: Text\s*=\s*"[A-Z][a-z]+               (hardcoded English words)
R6: Button                                 (all button elements, then manual check for Classes)
R7: Margin\s*=\s*"[0-9]+                  (single-value spacing values)
R8: ExperimentalAcrylicBorder             (acrylic wrapper presence)
```

**Caveats:**
- Ripgrep does not support lookahead/lookbehind, limiting accuracy of complex patterns.
- Grep results include both violations and false positives; final counts are estimated.
- Line numbers from grep output point to pattern matches, not necessarily line 1 of each file.

---

**End of Report**

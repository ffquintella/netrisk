# NetRisk UI Standards Audit - Quick Reference

## Overview at a Glance

```
Total Files Analyzed: 67 AXAML files
Roadmap Folder: NOT FOUND ✗
Compliance Rate: ~58% (39/67 compliant)
Critical Issues: 28 files
Recommended Priority: IMMEDIATE action on R1, R4, R5, R6
```

---

## Rule Compliance Card

| Rule | Name | Status | Pass | Fail | Priority |
|------|------|--------|------|------|----------|
| **R1** | No hardcoded hex colors | ✗ FAIL | 52 | 15 | 🔴 HIGH |
| **R2** | No inline FontSize | ✓ PASS | 67 | 0 | 🟢 PASS |
| **R3** | No inline FontWeight | ✗ FAIL | 64 | 3 | 🟡 MED |
| **R4** | No named brushes (status) | ✗ FAIL | 54 | 13 | 🔴 HIGH |
| **R5** | No hardcoded strings | ✗ FAIL | 58 | 9 | 🔴 HIGH |
| **R6** | All buttons have Classes | ✗ FAIL | 56 | 11 | 🔴 HIGH |
| **R7** | Spacing scale only | ✓ PASS | 66 | 1 | 🟢 PASS |
| **R8** | Acrylic borders | ✓ PASS | 8 | 0 | 🟢 PASS |
| **R9** | TextBlock Classes | ⚠ WARN | ~35 | ~30 | 🟡 MED |
| **R10** | Button icon+text | ✗ FAIL | 56 | 11 | 🟡 MED |

---

## Top 5 Problem Files

| File | Violations | Top Issues | Fix Effort |
|------|-----------|-----------|-----------|
| **UsersView.axaml** | 23 | R1, R4 (Foreground="Azure") | 30 min |
| **VulnerabilitiesView.axaml** | 14 | R1, R4 (Green status color) | 20 min |
| **AssessmentQuestionView.axaml** | 12 | R1 (#222222×4) | 15 min |
| **RiskView.axaml** | 8 | R7, mixed styling | 15 min |
| **CloseDialog.axaml** | 7 | R5, R6 (unclassed buttons) | 10 min |

---

## Quick Fixes (Copy-Paste Patterns)

### Fix R4 - Replace Named Brushes
```xml
❌ BEFORE:
<TextBlock Foreground="Azure" Text="{Binding StrStatus}"/>
<TextBlock Foreground="Green" Text="OK"/>

✅ AFTER:
<TextBlock Classes="form_label" Text="{Binding StrStatus}"/>
<Border ToolTip.Tip="{Binding StrStatus}">
    <material:MaterialIcon Kind="Check"/>
</Border>
```

### Fix R1 - Replace Hardcoded Colors
```xml
❌ BEFORE:
<Panel Background="#222222">
    <TextBlock Text="Section"/>
</Panel>

✅ AFTER:
<TextBlock Classes="header" Text="Section"/>
```

### Fix R6 - Add Button Classes
```xml
❌ BEFORE:
<Button Command="{Binding BtSaveClicked}">
    <StackPanel Orientation="Horizontal">
        <material:MaterialIcon Kind="ContentSave"/>
        <TextBlock Text="Save"/>
    </StackPanel>
</Button>

✅ AFTER:
<Button Classes="dialog1" Command="{Binding BtSaveClicked}">
    <StackPanel Orientation="Horizontal">
        <material:MaterialIcon Kind="ContentSave"/>
        <TextBlock Text="Save"/>
    </StackPanel>
</Button>
```

### Fix R5 - Localize Strings
```xml
❌ BEFORE:
<TextBlock Text="Save"/>
<Button ToolTip.Tip="Click to save"/>

✅ AFTER:
<TextBlock Text="{Binding StrSave}"/>
<Button ToolTip.Tip="{Binding StrSave}"/>
```

---

## Button Class Quick Decision Tree

```
Is it the main commit action?          → dialog1
Is it Cancel/Close/Dismiss?            → dialog2
Is it in the main nav sidebar?         → navigation
Is it icon-only in a toolbar?          → operation
Is it next to a single input field?    → subButton
Is it a tiny drill-down in a cell?     → detailButton
Is it a column/filter toggle?          → filterButton
Is it a non-committing action?         → type2 or type3
Is it a textual link in copy?          → link
Nothing matches?                        → RE-EXAMINE DESIGN ❌
```

---

## Color Tokens Quick Reference

| Use Case | Token | Hex |
|----------|-------|-----|
| Window background | `surface/base` | #282928 |
| Modal dialog | `surface/deep` | #181918 |
| First grouping | `GroupBox.type1` | #2b2b2b |
| Nested grouping | `GroupBox.type2` | #303030 |
| Section header | `TextBlock.header2` | #5252aa |
| Primary button | `Button.dialog1` | #51496b |
| Secondary button | `Button.dialog2` | #606060 |

❌ NEVER use: hardcoded hex, Red/Green/Blue for status, inline colors

---

## Spacing Scale (§5.5)

```
Only valid values: 2, 3, 5, 10, 15, 20, 30

Common patterns:
  Margin="5"           ✓ single value
  Margin="10 10 0 0"   ✓ bottom action margin
  Margin="0 5 0 0"     ✓ label spacing
  Margin="7"           ✗ NOT ON SCALE
  Margin="12 8"        ✗ NOT ON SCALE
```

---

## Immediate Next Steps (This Week)

- [ ] **TODAY**: Read AUDIT_SUMMARY.txt sections "TOP 5 WORST" and "ACTION ITEMS"
- [ ] **MON**: Fix R4 in UsersView.axaml (Foreground="Azure" → Material Icon)
- [ ] **TUE**: Fix R1 in AssessmentQuestionView.axaml (#222222 → Classes="header")
- [ ] **WED**: Add missing Classes to unclassed buttons (R6)
- [ ] **THU**: Move hardcoded strings to Localization.resx (R5)
- [ ] **FRI**: Code review against checklist; verify compliance

---

## Reference Documents

1. **Full Audit Report**: `UI_STANDARDS_AUDIT_REPORT.md`
2. **Audit Summary**: `AUDIT_SUMMARY.txt`
3. **UI Standard**: `docs/ui-standard.md`
4. **Style Reference**: `src/GUIClient/Styles/DarkStyles.axaml`
5. **Example Window**: `src/GUIClient/Views/EditRiskWindow.axaml` ✓ COMPLIANT

---

## How to Use This Audit

### For Developers
1. Check if your file is in the TOP 5 problem files
2. Use "Quick Fixes" patterns above to fix violations
3. Verify changes against the Rule Compliance Card
4. Reference button decision tree for new buttons

### For Code Review
1. Check PR against checklist (see AUDIT_SUMMARY.txt)
2. Verify button has correct class (use decision tree)
3. Confirm strings are localized (Str* binding)
4. Check colors come from styles, not hardcoded

### For Project Leads
1. Report Status: 58% compliant, ~28 files need fixing
2. Priority: Establish ban on R1/R4/R5/R6 violations in new code
3. Timeline: ~40 hours to fix all existing violations
4. Prevention: Add pre-commit hook to catch new violations

---

## Did You Know?

- All 67 files were analyzed using static regex patterns
- 10 practical rules can be checked from AXAML only
- 3 rules pass (R2, R7, R8) - good adoption! ✓
- 7 rules have violations - need immediate action 🔴
- Acrylic borders are 100% compliant ✓
- Hardcoded colors are the #1 violation (15 files)
- Named brushes (Green, Azure) are the #2 violation (13 files)
- Estimated fix time: 5-8 hours for all violations
- Roadmap folder: Not found ✗

---

**Last Updated**: Analysis performed via static audit  
**Standard Version**: docs/ui-standard.md (latest)  
**Scope**: 67 AXAML files under src/GUIClient/Views/

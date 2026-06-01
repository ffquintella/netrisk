# NetRisk Visual Theme & Component Specification (The "Abyssal Purple" Theme)

## 1. Unified Theme Philosophy & Aesthetics

The NetRisk user interface is built on a single, high-fidelity dark-mode design system called the **Abyssal Purple** theme. It is designed to look premium, modern, and cohesive across Windows, macOS, and Linux. The theme avoids flat, generic colors, relying instead on a precise multi-level depth model, semantic colors, and smooth glassmorphic backdrops.

### 1.1 The Elevation & Surface Model
To establish a clear visual hierarchy, elements are placed on five distinct elevation planes. Lower planes represent backgrounds that recede from the user; higher planes represent interactive surfaces that catch the light:

```
  ┌────────────────────────────────────────────────────────┐
  │  Level 4: High Focus Strip (Tabs/Accent panels)        │  #4f4f52  ████
  │  ┌──────────────────────────────────────────────────┐  │
  │  │  Level 3: Raised Panel (Popups/Tooltips)         │  │  #303030  ███
  │  │  ┌────────────────────────────────────────────┐  │  │
  │  │  │  Level 2: Standard Card (GroupBoxes)      │  │  │  #2b2b2b  ███
  │  │  │  ┌──────────────────────────────────────┐  │  │  │
  │  │  │  │  Level 1: Baseline Canvas (Main view)│  │  │  │  #282928  ██
  │  │  │  │  ┌────────────────────────────────┐  │  │  │  │
  │  │  │  │  │ Level 0: Deep Modal (Dialogs)  │  │  │  │  │  #181918  █
  │  │  │  │  └────────────────────────────────┘  │  │  │  │
  │  │  │  └──────────────────────────────────────┘  │  │  │
  │  │  └────────────────────────────────────────────┘  │  │
  │  └──────────────────────────────────────────────────┘  │
  └────────────────────────────────────────────────────────┘
```

*   **Level 0 — Deep Abyssal Wells (`#181918` / `#151519`):** Reserved for modal inputs, focus containers, and deep backgrounds. Provides maximum contrast for interactive text.
*   **Level 1 — Baseline Canvas (`#282928`):** The default background for the primary window workspace and standard pages.
*   **Level 2 — Card Panels (`#2b2b2b`):** Standard container cards (`GroupBox.type1`) that separate different functional forms.
*   **Level 3 — Raised Surfaces (`#303030`):** Used for nested containers (`GroupBox.type2`), flyout tooltips, and floating panels.
*   **Level 4 — High Chrome (`#4f4f52`):** Used for tab bars, global headers, and focus chrome that must catch the eye immediately.

### 1.2 Accent & Brand Typography
*   **Primary Brand Accent:** Purple (`#51496b`) — used for main buttons, focus states, and key navigational paths.
*   **Alt Accents:** SlateBlue (`#6A5ACD`) and DarkCyan (`#008B8B`) — used for secondary and tertiary action categories.
*   **Typography Scale:**
    *   `title` (Bold, 14pt, `#F0F0F0`): Localized section header titles.
    *   `header` (Bold, Centered, `#bbbbbb` on `#222222`): Full-width window banner.
    *   `header2` (Bold, `#bbbbbb` on `#5252aa`): Group section divider strip.
    *   `form_label` (Bold, `#F0F0F0`): Left-aligned field label.
    *   `form_text` (Regular, `#bbbbbb`): Input/Output read-only values.

---

## 2. Core Components Specification

To maintain perfect uniformity, views must construct layouts using this strict, predefined component taxonomy. Developers must use these classes rather than writing inline styles.

### 2.1 Window Shell & Modals
*   **Anatomy:**
    *   Must wrap content inside a `Panel` containing `ExperimentalAcrylicBorder` to enable native OS glass composition.
    *   The title must be bound via `Title="{Binding WindowTitle}"`.
    *   Row layout must separate the header, main content area, and action footer clearly.
*   **Usage:**
    *   `MainWindow` and large editors use a standard responsive size (e.g., `1200×800` or `1000×700` with `MinHeight` limiters).
    *   Standard dialogs must fix their width to `500px` or `400px` and scale automatically using `SizeToContent="Height"`.

### 2.2 Buttons & Iconography
Every button in NetRisk belongs to one of seven taxonomy classes. They must contain a vector `MaterialIcon` (size 20×20 px, or 15×15 px inside subButtons) and a localized `TextBlock` using standard margins.

| Button Class | Role | Background | Margin Separations |
|---|---|---|---|
| `dialog1` | **Primary Action:** Committing edits, Save, Confirm. | Purple (`#51496b`) | Baseline. |
| `dialog2` | **Secondary Action:** Cancel, Close, Dismiss. | Grey (`#606060`) | `10 0 0 0` (placed to the right of `dialog1`). |
| `type2` | **Secondary Tool:** Refresh lists, select-all, download. | SlateBlue (`#6A5ACD`) | Toolbar spaces. |
| `type3` | **Information Utility:** Search, filters, preview. | DarkCyan (`#008B8B`) | Container headers. |
| `operation` | **Toolbar Action:** Icon-only Add, Edit, Delete. | Transparent | `Spacing="5"` on toolbar strip. |
| `subButton` | **Field Action:** Micro-buttons next to inputs. | DarkSlateBlue (`#483D8B`) | `5 5 5 0` from trailing fields. |
| `detailButton` | **Cell Drill-down:** Grid row click action. | Deep Slate (`#152e2e`) | Embedded inside grid rows. |

### 2.3 Forms & Layout Spacing
Forms are built as grid matrices using the standard **Spacing Scale**. No random padding or margins (like 7px or 12px) are permitted:

```
  xxs (2px) ── xs (3px) ── sm (5px) ── md (10px) ── lg (15px) ── xl (20px) ── xxl (30px)
```

*   **Responsive Rows:** Form components are placed inside a `Grid` or `SpacedGrid` using `ColumnDefinitions="Auto, *, Auto, *"`. Labels use `Auto` columns (adapting to word expansions in other languages); inputs use flexible `*` columns with explicit `MinWidth` (never hardcoded `Width`).
*   **Row Spacing:** Rows inside a form use standard `lg` spacing (15px) to give components adequate breathing room.

### 2.4 Data Grids
*   **Configuration:**
    *   `AutoGenerateColumns="False"` always.
    *   Interaction flags enabled: `CanUserReorderColumns="True"`, `CanUserResizeColumns="True"`, `CanUserSortColumns="True"`.
    *   Headers are bound to `Str*` resources; text is read-only.
    *   Status states are drawn as shapes or Material Icons with explicit tooltips rather than raw color words.

---

## 3. UI Usage & Alignment Guidelines

When constructing or refactoring a view, developers must adhere to these clear usage rules:

1.  **Never Use Inline Color/Typography Attributes:** Do not write `Background="#..."`, `Foreground="#..."`, `FontSize="..."`, or `FontWeight="Bold"` on elements. Use style classes (like `Classes="form_label"`, `Classes="dialog1"`) which draw values from `WindowStyles.axaml`.
2.  **Order Button Groups Consistent to Layout:** In all action bars and footer menus, the positive/primary action (`dialog1`) MUST sit on the **left**, and the dismissive/secondary action (`dialog2`) MUST sit on the **right** (e.g., `[ Save ]  [ Cancel ]`).
3.  **Ensure Explicit Scrolling Boundaries:** Main panels must wrap dense content in `ScrollViewer` elements with `VerticalScrollBarVisibility="Auto"` and `HorizontalScrollBarVisibility="Disabled"`. Never wrap a `DataGrid` in a `ScrollViewer` (this breaks column header rendering and virtualization).
4.  **Always Provide Status Tooltips:** Status badges and icons must carry `ToolTip.Tip="{Binding StrStatusDescription}"` and have a uniform `Border.tooltip` class containing a `1000ms` hover delay.

---

## 4. Visual Standardization Migration Plan

The existing NetRisk interface exhibits visual and functional drift due to legacy view implementations. Below is the official roadmap plan to systematically align all 67 views of the desktop client (`GUIClient`) with this visual theme.

```
┌────────────────────────────────────────────────────────┐
│ MIGRATION PIPELINE:                                    │
│                                                        │
│  Phase 1: Shell & Frame Core                           │
│     │                                                  │
│  Phase 2: High-Usage Dialogs & Forms                   │
│     │                                                  │
│  Phase 3: Navigation and Status normalization          │
│     │                                                  │
│  Phase 4: Sweep of Remaining Complex Panels & Reports  │
│     │                                                  │
│  Phase 5: Automated Regression Audit                   │
└────────────────────────────────────────────────────────┘
```

### Phase A: Shell & Frame Core (Short Term)
*   **Objective:** Stabilize the app shell, global borders, and baseline canvas elevations.
*   **Target Files:** `MainWindow.axaml`, `App.axaml`, `Styles/WindowStyles.axaml`.
*   **Remediation Action:**
    *   Introduce `ExperimentalAcrylicBorder` to the base of `MainWindow.axaml` to wrap content, painting the baseline backdrop properly.
    *   Consolidate background surface definitions across `WindowStyles.axaml` into the 5-plane depth tokens.
    *   Ensure all child windows inherit the baseline custom window style.

### Phase B: High-Usage Dialogs & Core Forms (Short/Medium Term)
*   **Objective:** Bring high-traffic data entry views into full compliance with layout, spacing, and buttons.
*   **Target Files:** `LoginWindow.axaml`, `CloseDialog.axaml`, `FixRequestDialog.axaml`, `EditRiskWindow.axaml`, `AssessmentQuestionView.axaml`.
*   **Remediation Action:**
    *   Remove hardcoded widths from login and dialog input text boxes, mapping them to responsive grids.
    *   Extract plain-text strings into `.resx` localized bundles; bind title bars using `WindowTitle`.
    *   Apply standard margins (`10 10 0 0` on action footers, `15px` row-spacing) to restore proper density.
    *   Refactor legacy buttons to use `Classes="dialog1"` and `Classes="dialog2"` with vector iconography.

### Phase C: Navigation & List Panels (Medium Term)
*   **Objective:** Standardize main dashboards, navigation, and list-heavy operations.
*   **Target Files:** `NavigationBar.axaml`, `UsersView.axaml`, `VulnerabilitiesView.axaml`, `RiskView.axaml`.
*   **Remediation Action:**
    *   Standardize the `NavigationBar.axaml` background and replace hardcoded button colors.
    *   Clean up `UsersView.axaml` and `VulnerabilitiesView.axaml` (the highest-offense views) by removing named status colors (Azure, Green, Red) from markup, replacing them with standard Material Icon glyphs.
    *   Map column headers dynamically to localized strings.

### Phase D: Complex Panels & Report Editors (Medium/Long Term)
*   **Objective:** Bring administrative views, assessment builders, and reporting pages into compliance.
*   **Target Files:** Remaining ~40 smaller form panels and detail boxes.
*   **Remediation Action:**
    *   Run a global grep sweep to catch any remaining inline `Background`, `Foreground`, or `FontWeight` attributes, translating them to `WindowStyles.axaml` classes.
    *   Normalize `DataGrid` declarations across all views to use explicit header bindings, column resizing, and reordering.

### Phase E: Automated Theme Regression Audits (Long Term)
*   **Objective:** Protect the standardized visual theme against future design drift.
*   **Remediation Action:**
    *   Incorporate the static audit patterns (from `UI_STANDARDS_AUDIT_REPORT.md`) into a custom CI check running in `build/Build.cs` or as a pre-commit git hook.
    *   Reject pull requests that introduce inline hex colors (`#`), inline font sizes, or unclassed `Button` tags in `src/GUIClient/Views/`.

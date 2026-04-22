# NetRisk GUI — UI/UX Standard

This document defines the unified look, feel, and layout conventions for the NetRisk desktop GUI ([`GUIClient`](../src/GUIClient), Avalonia + ReactiveUI). It consolidates patterns already in use across the 85+ views under [`src/GUIClient/Views/`](../src/GUIClient/Views) and [`src/GUIClient/Styles/`](../src/GUIClient/Styles) and promotes them to mandatory standards so every window has a consistent appearance.

New views MUST follow this standard. Existing views that deviate SHOULD be migrated opportunistically — do not gate feature work on full migration, but do not add new deviations.

---

## 1. Foundation

### 1.1 Theme

- **Theme variant:** `Dark` only. Set once in [`App.axaml`](../src/GUIClient/App.axaml) via `RequestedThemeVariant="Dark"`. Do not override per-window.
- **Base theme:** Avalonia `FluentTheme` + `Aura.UI.FluentTheme` + `MaterialIconStyles`.
- **Style load order** (do not change): `Icons → DarkStyles → WindowStyles → ComponentStyles → FluentTheme → DataGrid Fluent → Aura.UI`.

### 1.2 Iconography

- **Library:** [Material.Icons.Avalonia](https://github.com/SKProCH/Material.Icons.Avalonia). No bitmaps for UI glyphs.
- **Canonical kinds** (use these names for these actions, consistently):

| Action | `MaterialIcon Kind` |
|--------|---------------------|
| Save | `ContentSave` |
| Save all | `ContentSaveAll` |
| Cancel | `Cancel` |
| Close | `Close` |
| Add / Create | `Add` |
| Delete | `Delete` |
| Edit | `Edit` |
| Download / Export | `Download` |
| Import | `Import` |
| Reload / Refresh | `Reload` |
| Search | `Magnify` |
| Select | `SelectSearch` / `SelectAll` |
| Review (mgmt) | `RateReview` |
| New item badge | `NewReleases` |
| Done / resolved | `Check` / `CheckCircle` / `Done` |
| Notifications | `Bell` |
| Settings | `Settings` |

- **Default icon size:** 20×20 px inside buttons, 15×15 px inside `Button.subButton`, 25×25 px for toolbar/operation buttons.
- Do not invent new icons for actions already in the table above.

### 1.3 Application Shell

- The root window is [`MainWindow`](../src/GUIClient/Views/MainWindow.axaml):
  - `ExtendClientAreaToDecorationsHint="True"` (custom titlebar look).
  - Row layout: `30, *` — menu bar (30px) + content.
  - Content wrapped in `ExperimentalAcrylicBorder` with `BackgroundSource="Digger"`, `TintColor="Black"`, `TintOpacity="1"`, `MaterialOpacity="0.65"`.
  - Top-level navigation via [`NavigationBar`](../src/GUIClient/Views/NavigationBar.axaml).
- Child "edit" windows (e.g. [`EditRiskWindow`](../src/GUIClient/Views/EditRiskWindow.axaml), [`LoginWindow`](../src/GUIClient/Views/LoginWindow.axaml)) repeat the acrylic wrapper for visual continuity.

---

## 2. Color Palette

All colors are defined or used in [`WindowStyles.axaml`](../src/GUIClient/Styles/WindowStyles.axaml) and [`DarkStyles.axaml`](../src/GUIClient/Styles/DarkStyles.axaml). **Do not introduce new hard-coded colors in views** — add a style class and reference it.

### 2.1 Elevation model

NetRisk uses a simple 5-level elevation system. Lower = further from the user, darker. Use the table below to choose the right surface for a given UI element; do not mix freely.

```
 Level 4 ─ Tab strip / chrome    #4f4f52  ████  (lighter, catches eye)
 Level 3 ─ Raised panel          #303030  ███
 Level 2 ─ Panel                 #2b2b2b  ███
 Level 1 ─ Window default        #282928  ██    (baseline)
 Level 0 ─ Deep / focus          #181918  █     (dialogs, focus wells)
         ─ Abyss (level -1)      #151519  ▓     (GroupBox.type3 only)
```

Rule of thumb: every nested container goes **up** one level from its parent so hierarchy is perceptible without borders.

### 2.2 Surface tokens

| Token | Hex | Preview | Usage |
|-------|-----|---------|-------|
| `surface/base` | `#282928` | ![](https://via.placeholder.com/16/282928/282928.png) | Default window & `TabControl` background |
| `surface/deep` | `#181918` | ![](https://via.placeholder.com/16/181918/181918.png) | `Window.dark` — modal focus contexts |
| `surface/panel-1` | `#2b2b2b` | ![](https://via.placeholder.com/16/2b2b2b/2b2b2b.png) | `GroupBox.type1` — first level of grouping |
| `surface/panel-2` | `#303030` | ![](https://via.placeholder.com/16/303030/303030.png) | `GroupBox.type2`, tooltips |
| `surface/panel-3` | `#151519` | ![](https://via.placeholder.com/16/151519/151519.png) | `GroupBox.type3` — use sparingly for deep wells |
| `surface/tab` | `#4f4f52` | ![](https://via.placeholder.com/16/4f4f52/4f4f52.png) | `TabItem`, tab strip |
| `surface/status` | `#5b5b5b` | ![](https://via.placeholder.com/16/5b5b5b/5b5b5b.png) | `Grid.statusBar` |
| `surface/footer-bar` | `#202020` | ![](https://via.placeholder.com/16/202020/202020.png) | `Border.footer` |
| `surface/footer-text` | `#323232` | ![](https://via.placeholder.com/16/323232/323232.png) | `TextBlock.footer` |
| `surface/graph` | `#2e2d2d` | ![](https://via.placeholder.com/16/2e2d2d/2e2d2d.png) | Chart / graph backgrounds |
| `surface/splitter` | `#393939` | ![](https://via.placeholder.com/16/393939/393939.png) | `GridSplitter.horizontalSplitter` |
| `surface/scrim` | `#090909` @ 0.5 | ![](https://via.placeholder.com/16/090909/090909.png) | `Grid#OverlayGridCtrl` blocking overlay |

### 2.3 Text tokens

| Token | Hex | Contrast target | Usage |
|-------|-----|-----------------|-------|
| `text/primary` | `#F0F0F0` | ≥ 11:1 on surface/base | Default `TextBlock` text |
| `text/secondary` | `#bbbbbb` | ≥ 6:1 on surface/base | `header`, `header_detail`, captions |
| `text/tertiary` | `#ababab` | ≥ 5:1 | `footer` text |
| `text/on-light` | `#090909` / `#050505` | On light backgrounds | `riskHeader`, `statusBar` text |
| `text/accent` | `#C469EE` | — | Reserved; available as a `semantic/link-inline` token |

All text colors are pre-checked for WCAG AA on the intended surface — do not introduce new text colors without re-verifying contrast.

### 2.4 Accent & semantic tokens

| Token | Hex | Swatch | Semantic |
|-------|-----|--------|----------|
| `accent/primary` | `#51496b` | ![](https://via.placeholder.com/16/51496b/51496b.png) | **Brand purple.** Primary action, navigation, filter. |
| `accent/primary-hover` | `#505050` | ![](https://via.placeholder.com/16/505050/505050.png) | Button pointer-over state |
| `accent/neutral` | `#606060` | ![](https://via.placeholder.com/16/606060/606060.png) | Secondary / cancel button |
| `accent/neutral-base` | `#707070` | ![](https://via.placeholder.com/16/707070/707070.png) | Default button (no class) |
| `accent/slate` | `SlateBlue` | ![](https://via.placeholder.com/16/6A5ACD/6A5ACD.png) | `type2` buttons (alt action) |
| `accent/cyan` | `DarkCyan` | ![](https://via.placeholder.com/16/008B8B/008B8B.png) | `type3` buttons (alt action) |
| `accent/subButton` | `DarkSlateBlue` | ![](https://via.placeholder.com/16/483D8B/483D8B.png) | Inline micro-action |
| `accent/detail` | `#152e2e` | ![](https://via.placeholder.com/16/152e2e/152e2e.png) | `detailButton` (very small drill-down) |
| `semantic/info-bg` | `#5252aa` | ![](https://via.placeholder.com/16/5252aa/5252aa.png) | `TextBlock.header2` section band |
| `semantic/header-bg` | `#222222` | ![](https://via.placeholder.com/16/222222/222222.png) | `TextBlock.header` top band |
| `semantic/warning` | `#880000` | ![](https://via.placeholder.com/16/880000/880000.png) | Soft warning (`warning` class) |
| `semantic/warning-strong` | `#aa0000` | ![](https://via.placeholder.com/16/aa0000/aa0000.png) | Strong warning (`warning2` class) |
| `semantic/link-inline` | `#C469EE` | ![](https://via.placeholder.com/16/C469EE/C469EE.png) | Reserved for inline hyperlink text (add a `Run.hyperlink` style if needed) |
| `semantic/link-button` | `Blue` | ![](https://via.placeholder.com/16/0000FF/0000FF.png) | `Button.link` |

### 2.5 Choosing a color — decision guide

| Situation | Use |
|-----------|-----|
| Main window background | `surface/base` (automatic) |
| Modal dialog background | `surface/deep` via `Window.dark` class |
| Wrapping a group of fields | `GroupBox.type1` (first level), `type2` (nested) — never `type3` unless you need maximum visual recession |
| Section band inside a window | `TextBlock.header2` — never repaint a `Grid` with `#5252aa` directly |
| Full window header | `TextBlock.header` on row 0 |
| Primary action (Save/Confirm) | `Button.dialog1` |
| Cancel / dismiss | `Button.dialog2` |
| Destructive action (Delete) | `Button.dialog1` + confirmation dialog — **do not** invent a red button; confirm via a follow-up dialog that calls out the destructive nature |
| Informational note | `TextBlock.header_detail` or normal text — never invent a colored note box |
| Validation / error banner | `TextBlock.warning` (field-level) or `warning2` (window-level) |
| Chart / graph | `Grid.Graph` background |

### 2.6 Forbidden patterns

- ❌ Inline `Background="#..."` or `Foreground="#..."` anywhere in a view. Add a class or reuse an existing one.
- ❌ Named Avalonia brushes like `Red`, `Green`, `Orange` as status indicators — use a Material Icon with a `ToolTip.Tip` instead (see §1.2). NetRisk encodes status by **shape**, not color.
- ❌ Introducing a new hex that is within ΔE 5 of an existing token — reuse the existing one.
- ❌ Transparent `Background` on a container whose child contains bound user data (breaks acrylic contrast).

> **Process:** to add a new semantic state (success/info/neutral), open an issue proposing (a) the hex, (b) the contrast check against every surface it may appear on, (c) the style class name, and (d) the view that needs it. Only then add it to `WindowStyles.axaml`.

---

## 3. Typography

Typography is class-driven via `TextBlock.Classes=""`. Do not set `FontSize`, `FontWeight`, or `Foreground` ad-hoc on `TextBlock`s — pick a class.

### 3.1 Standard classes

| Class | Role | Visual | Used on |
|-------|------|--------|---------|
| `title` | Window-local section title | Bold, 14pt, margin 3 5 | Dialog/section titles |
| `header` | Full-width page/window header | Bold, centered, `#222` bg, stretches | Top of windows, above content |
| `header2` | Sub-section header | `#5252aa` background strip | Grouped field blocks |
| `header3` | In-line section header | Bold italic, no background | Minor groupings |
| `header_detail` | Metadata line under `header` | Secondary text, small margin | Op/operation-type under a header |
| `label` | Inline field label | Bold, margin-right | Horizontal label+value rows |
| `label-nm` | Label without margin | Bold | Compact rows |
| `form_label` | Form-grid label | Bold, 3 5 0 0 margin | Inside `SpacedGrid` forms |
| `form_text` | Form-grid value | 0 5 1 0 margin | Read-only values in forms |
| `form_text2` | Form-grid value (tighter) | 0 6 0 0 margin | |
| `form_long_text` | Form value, wrapping | `TextWrapping=Wrap` | Long text fields |
| `formData` | Generic form data, wrapping | 0 5 0 0 margin | Mixed layouts |
| `detailBlock` | Compact detail row | Dark text on light bg | Dense grids |
| `footer` | Window footer text | `#323232` bg, right-aligned | Status/version line |
| `statusBar` | Status bar caption | Dark text | Over `Grid.statusBar` |
| `riskHeader` | Inline label over a light/colored swatch | Dark text (`#090909`) | Risk summary rows (e.g. vulnerability→risk link) |
| `warning` / `warning2` | Warning message | Red background variants | Blocking/notice banners |

### 3.2 Localization

- **Every user-facing string MUST come from resources** via a `Str*` binding. Pattern:
  ```xml
  <TextBlock Text="{Binding StrSubject}" Classes="form_label"/>
  ```
  The view-model exposes `StrSubject` backed by [`Localization.en-US.resx`](../src/GUIClient/Resources/Localization.en-US.resx) / [`pt-BR`](../src/GUIClient/Resources/Localization.pt-BR.resx).
- **No hard-coded English strings** in `.axaml`, not even in buttons. Exception: debug-only menus behind `IsDebug`.
- New strings are added to **every** `.resx` locale; missing translations default to the invariant resource but should not be committed intentionally.

---

## 4. Buttons

Buttons are the single highest-impact element for a consistent UI. Every button in NetRisk MUST:

1. Carry a **class** (no unclassed buttons on new windows).
2. Contain an **icon + text** combination using a `MaterialIcon` (see §1.2) and a localized `TextBlock`.
3. Bind to a `Bt<Action>Clicked` command on the view-model.
4. Gate enablement via `IsEnabled="{Binding Is<Action>Enabled}"` — never in code-behind.

### 4.1 Class taxonomy (complete)

| Class | Role | Size (W×H) | Radius | Fill | Foreground | Typical icon | Where it lives |
|-------|------|------------|--------|------|------------|--------------|----------------|
| `dialog1` | **Primary** action (Save, OK, Confirm) | auto | default | `#51496b` | `#FFFFFF` | `ContentSave`, `Check` | Dialog action row (left) |
| `dialog2` | **Secondary** action (Cancel, Close, Back) | auto | default | `#606060` | `#FFFFFF` | `Cancel`, `Close` | Dialog action row (right) |
| `type2` | Alt primary (non-blocking action) | auto | default | `SlateBlue` | inherit | `Reload`, `SelectSearch` | Inside a panel toolbar |
| `type3` | Alt primary (information/query) | auto | default | `DarkCyan` | inherit | `Eye`, `Magnify` | Lookup / preview actions |
| `operation` | Toolbar operation (icon-only) | 25×25 | default | inherit | `#FFFFFF` | `Add`, `Delete`, `Edit` | `DataGrid` toolbars |
| `navigation` | Sidebar/nav button | 20×20 | default | `#51496b` | `#FFFFFF` | section icons | `NavigationBar` |
| `filterButton` | Filter toggle (pill) | 25×25 | **15** (round) | `#51496b` | inherit | `FilterVariant` | Column filter headers |
| `subButton` | Inline sub-action next to a field | 25×25 | 5 | `DarkSlateBlue` | inherit | `Add`, `Delete` (15×15) | Right of a `TextBox`/`ComboBox` |
| `detailButton` | Tiny drill-down | 20×20 | 5 | `#152e2e` | inherit | `ChevronRight` | Inside a `DataGrid` cell |
| `link` | Link-styled inline button | auto | — | transparent | `Blue` + underline | — | Inline in rich text |
| _(none)_ | Generic button — **discouraged for new work** | auto | default | `#707070` | `#FFFFFF` | — | Legacy |

Pointer-over is handled globally in [`DarkStyles.axaml`](../src/GUIClient/Styles/DarkStyles.axaml): `Background → #505050` on hover.

### 4.2 Decision matrix — which class?

Start at the top and pick the first row that matches:

| Question | If yes → use |
|----------|--------------|
| Is it the **primary commit** of a dialog/edit window? | `dialog1` |
| Is it **Cancel / Close / Dismiss** of a dialog? | `dialog2` |
| Is it in the main nav sidebar? | `navigation` |
| Is it **icon-only** in a toolbar above a table/list? | `operation` |
| Is it inline next to a single input (one field)? | `subButton` |
| Is it a drill-down inside a table cell? | `detailButton` |
| Is it a column/filter toggle? | `filterButton` |
| Is it a **non-committing** action in a toolbar (Refresh, Search)? | `type2` or `type3` |
| Is it a textual link in running copy? | `link` |

If none match, the design is probably wrong — re-examine the window layout before inventing a new class.

### 4.3 Anatomy & content

**Full-size button (dialog/toolbar):**

```xml
<Button Name="BtSave"
        Classes="dialog1"
        Command="{Binding BtSaveClicked}"
        IsEnabled="{Binding IsSaveEnabled}">
    <StackPanel Orientation="Horizontal">
        <avalonia:MaterialIcon Kind="ContentSave" Width="20" Height="20" Margin="0 0 5 0"/>
        <TextBlock Text="{Binding StrSave}"/>
    </StackPanel>
</Button>
```

**Icon-only button (operation/subButton/detailButton):** no `TextBlock`, `ToolTip.Tip` is mandatory.

```xml
<Button Classes="operation"
        Command="{Binding BtAddClicked}"
        ToolTip.Tip="{Binding StrAdd}">
    <avalonia:MaterialIcon Kind="Add" Width="20" Height="20"/>
</Button>
```

For `subButton`, the icon is 15×15 (the class scales `MaterialIcon.subButton` automatically):

```xml
<Button Classes="subButton" Command="{Binding BtClearClicked}" ToolTip.Tip="{Binding StrClear}">
    <avalonia:MaterialIcon Classes="subButton" Kind="Close"/>
</Button>
```

**Link button** (rare, use sparingly):

```xml
<Button Classes="link" Command="{Binding BtOpenDetailsClicked}">
    <TextBlock Text="{Binding StrViewDetails}"/>
</Button>
```

### 4.4 Dialog action row — canonical layout

Every edit window and dialog MUST end with this row. Primary on the **left**, secondary 10 px to its right, whole block centered horizontally, with `10 10 0 0` margin from the content above.

```
┌──────────────────────────── Content ───────────────────────────┐
│                                                                │
│                                                                │
│                 [ 💾 Save ]   [ ✕ Cancel ]                     │   ← Margin 10 10 0 0
└────────────────────────────────────────────────────────────────┘
```

```xml
<StackPanel Orientation="Horizontal"
            HorizontalAlignment="Center"
            Margin="10 10 0 0">
    <Button Name="BtSave"
            Classes="dialog1"
            Command="{Binding BtSaveClicked}"
            IsEnabled="{Binding IsSaveEnabled}">
        <StackPanel Orientation="Horizontal">
            <avalonia:MaterialIcon Kind="ContentSave" Width="20" Height="20" Margin="0 0 5 0"/>
            <TextBlock Text="{Binding StrSave}"/>
        </StackPanel>
    </Button>
    <Button Name="BtCancel"
            Classes="dialog2"
            Command="{Binding BtCancelClicked}"
            Margin="10 0 0 0">
        <StackPanel Orientation="Horizontal">
            <avalonia:MaterialIcon Kind="Cancel" Width="20" Height="20" Margin="0 0 5 0"/>
            <TextBlock Text="{Binding StrCancel}"/>
        </StackPanel>
    </Button>
</StackPanel>
```

Reference: [`CloseDialog.axaml`](../src/GUIClient/Views/CloseDialog.axaml).

### 4.5 Toolbar (above a DataGrid)

Icon-only `Button.operation` in a horizontal `StackPanel` at the top of the panel, left-aligned, 5 px between buttons.

```
┌ [➕ Add] [✏ Edit] [🗑 Delete]    [🔄 Reload] [🔍 Filter] ──────┐
│                                                                 │
│   DataGrid                                                      │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

```xml
<StackPanel Orientation="Horizontal" Margin="5" Spacing="5">
    <Button Classes="operation" Command="{Binding BtAddClicked}" ToolTip.Tip="{Binding StrAdd}">
        <avalonia:MaterialIcon Kind="Add"/>
    </Button>
    <Button Classes="operation" Command="{Binding BtEditClicked}"
            IsEnabled="{Binding IsEditEnabled}" ToolTip.Tip="{Binding StrEdit}">
        <avalonia:MaterialIcon Kind="Edit"/>
    </Button>
    <Button Classes="operation" Command="{Binding BtDeleteClicked}"
            IsEnabled="{Binding IsDeleteEnabled}" ToolTip.Tip="{Binding StrDelete}">
        <avalonia:MaterialIcon Kind="Delete"/>
    </Button>
</StackPanel>
```

### 4.6 Destructive actions

NetRisk does not use a red "danger button" style. Destructive actions (Delete, Purge, Reject) use the **same `dialog1` visual** but always route through a confirmation dialog that spells out the consequence. The confirmation dialog uses the standard §4.4 action row.

**Pattern:** first button triggers confirmation → confirmation dialog uses `TextBlock.warning`/`warning2` + a `dialog1` "Confirm delete" / `dialog2` "Cancel".

### 4.7 States

| State | Visual | Trigger |
|-------|--------|---------|
| Default | Class-defined background | — |
| Hover | `#505050` | `IsPointerOver=true` (global) |
| Disabled | Avalonia default reduced opacity | `IsEnabled="{Binding Is*Enabled}"` |
| Pressed | Avalonia default | — |
| Loading | Hide button, show [ProgressRing](#63-feedback) overlay on the containing panel | `Loading` flag |

Do not attempt a per-button spinner — loading is a window-level concern.

### 4.8 Sizing rules

- **Auto-sized buttons** (`dialog1`/`dialog2`/`type2`/`type3`/`link`) expand to fit their content; do not set `Width`/`Height`.
- **Fixed-sized buttons** (`operation`/`navigation`/`filterButton`/`subButton`/`detailButton`) inherit their size from the class; do not override.
- Two buttons in a row are always separated by `Margin="10 0 0 0"` on the second one — never change this gap.
- A row of icon-only `operation` buttons uses `Spacing="5"` on the parent `StackPanel`.

---

## 5. Layout

### 5.1 Layout primitives — when to use which

| Container | Use for | Don't use for |
|-----------|---------|---------------|
| `Grid` | Window skeletons, 2-axis layouts with explicit rows/cols | Long flat lists of similar items |
| `SpacedGrid` | Forms with many rows/cols that need consistent gaps (row 15, col 10) | Simple 2-row skeletons |
| `StackPanel` | Horizontal button/icon rows, inline `label + value` pairs | Forms with ≥ 3 fields |
| `DockPanel` | Window chrome (menu top, footer bottom, fill center) | Anything else |
| `ScrollViewer` | Wrapping a long-form content area | Wrapping a `DataGrid` |
| `Panel` | Overlays (acrylic, progress ring, scrim) | Normal content layout |

**Rule of thumb:** if your layout has more than two nested `StackPanel`s, it belongs in a `Grid` or `SpacedGrid`.

### 5.2 Responsive sizing rules

- **Columns:** mix `Auto` (labels, fixed-width controls) and `*` / `2*` (inputs that should grow). Never hard-code pixel widths on columns.
- **Inputs:** set `MinWidth` (e.g. `MinWidth="150"` for combos, `300` for names) and let the column `*` do the rest. Never set `Width` on a `TextBox` that is the primary field of its row.
- **Windows:** fix `MinWidth`/`MinHeight` so the layout doesn't collapse, but let users resize freely. `Width`/`Height` is the *opening* size only.
- **DataGrid columns:** see §6.1 — fixed for ID/icon/status, star for subject, `*` with min/max for secondary data.

### 5.3 Window skeletons

#### 5.3.1 Full edit window

Use for Risk, Incident, IRP, Assessment editors.

```
Window (1200×800, min 900×650, ExtendClientAreaToDecorationsHint=True)
└── Panel                                        ◄── enables acrylic overlay
    ├── ExperimentalAcrylicBorder (Digger/Black/0.65)
    └── Grid RowDefinitions="Auto, *, Auto"
        ├── [Row 0] ───────────────────────────────────────────────────
        │           TextBlock.header  "{Binding WindowTitle}"
        │           TextBlock.header_detail (optional metadata)
        ├── [Row 1] ───────────────────────────────────────────────────
        │           ScrollViewer (V:Auto, H:Disabled)
        │           └── SpacedGrid (RowSpacing=15, ColumnSpacing=10)
        │               ├── TextBlock.header2  "Section A"
        │               ├── SpacedGrid (form fields)
        │               ├── TextBlock.header2  "Section B"
        │               └── SpacedGrid (form fields)
        └── [Row 2] ───────────────────────────────────────────────────
                    StackPanel Horizontal, centered
                    [ dialog1 Save ]  [ dialog2 Cancel ]
        (ProgressRing overlay via ZIndex=1000, IsActive={Binding Loading})
```

Minimal skeleton:

```xml
<Window Width="1200" Height="800" MinWidth="900" MinHeight="650"
        Title="{Binding WindowTitle}">
    <Panel>
        <ExperimentalAcrylicBorder IsHitTestVisible="False">
            <ExperimentalAcrylicBorder.Material>
                <ExperimentalAcrylicMaterial BackgroundSource="Digger"
                                             TintColor="Black" TintOpacity="1"
                                             MaterialOpacity="0.65"/>
            </ExperimentalAcrylicBorder.Material>
        </ExperimentalAcrylicBorder>
        <Grid RowDefinitions="Auto, *, Auto">
            <TextBlock Grid.Row="0" Classes="header" Text="{Binding WindowTitle}"/>
            <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto"
                                       HorizontalScrollBarVisibility="Disabled">
                <!-- SpacedGrid with sections here -->
            </ScrollViewer>
            <!-- dialog action row (see §4.4) -->
        </Grid>
        <!-- ProgressRing overlay -->
    </Panel>
</Window>
```

Reference: [`EditRiskWindow`](../src/GUIClient/Views/EditRiskWindow.axaml), [`EditIncidentWindow`](../src/GUIClient/Views/EditIncidentWindow.axaml).

#### 5.3.2 Dialog

Smaller focused input collector.

```
Window (Width=500, SizeToContent="Height", CenterScreen|CenterOwner)
└── Grid ColumnDefinitions="*, Auto, *" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto"
    ┌───────────────────────────────────────────────┐
    │ Row 0 │   TextBlock.header (span all 3 cols)  │
    │ Row 1 │        │ TextBlock.header2 │          │
    │ Row 2 │        │   Input control   │          │   ◄── centered
    │ Row 3 │        │ TextBlock.header2 │          │       in column 1
    │ Row 4 │        │   Input control   │          │
    │ Row 5 │        │  [Save] [Cancel]  │          │
    └───────────────────────────────────────────────┘
```

- Column layout `*, Auto, *` centers the content column.
- Header spans all three columns.
- Fields and action row live in column 1 only.

Reference: [`CloseDialog`](../src/GUIClient/Views/CloseDialog.axaml).

#### 5.3.3 List / panel view

Embedded user control inside `MainWindow` (via `IsVisible` swap).

```
UserControl
└── Grid RowDefinitions="Auto, *, Auto"
    ├── [Row 0] Toolbar: StackPanel Horizontal
    │           [➕] [✏] [🗑]  ····  [🔄] [🔍]
    ├── [Row 1] DataGrid (fills, handles own scroll)
    └── [Row 2] Border.footer (optional) — summary text, right-aligned
```

Reference: [`RisksPanelView`](../src/GUIClient/Views/RisksPanelView.axaml).

#### 5.3.4 Form section inside a window

A single logical group of fields inside an edit window.

```
TextBlock.header2  "Identification"
SpacedGrid ColumnDefinitions="Auto, *, Auto, *"  RowSpacing=15, ColumnSpacing=10
 ┌──────────┬──────────────────┬──────────┬──────────────────┐
 │ label    │ input (MinW 150) │ label    │ input (MinW 150) │
 ├──────────┼──────────────────┼──────────┼──────────────────┤
 │ label    │ input            │ label    │ input            │
 └──────────┴──────────────────┴──────────┴──────────────────┘
```

Use `Auto, *` pairs — one `Auto` for the label, one `*` for the input — repeated for 2-up or 3-up forms.

### 5.4 Window sizing table

| Window type | Width × Height | Min | `SizeToContent` | Startup location |
|-------------|----------------|-----|-----------------|------------------|
| `MainWindow` | platform | — | — | OS default |
| Full edit window (Risk, Incident, IRP, Assessment) | `1200 × 800` | `900 × 650` | — | inherits |
| Large edit window (Vulnerability Import, Reports) | `1000 × 700` | `800 × 600` | — | `CenterOwner` |
| Standard dialog | `500 × auto` | — | `Height` | `CenterScreen` |
| Narrow dialog (confirm, single field) | `400 × auto` | — | `Height` | `CenterOwner` |
| Login | `400 × 250` | — | — | `CenterOwner` |

Rules:
- Modals launched from another window use `CenterOwner`.
- Top-level windows (login, main) use `CenterScreen` or OS default.
- Dialogs fix the width and let height grow via `SizeToContent="Height"`.

### 5.5 Spacing scale

The **only** acceptable `Margin`/`Padding` values are from this scale. Values are a single number (symmetric) or a 4-tuple `"left top right bottom"` built from these tokens.

| Token | Value | Where |
|-------|-------|-------|
| `xxs` | `2` | `header`/`footer` padding, tight chrome |
| `xs`  | `3` | `form_label` left offset |
| `sm`  | `5` | Default field margin, inner cell padding |
| `md`  | `10` | Content padding, space between button groups, `ColumnSpacing` in forms |
| `lg`  | `15` | `SpacedGrid.RowSpacing`, form row spacing |
| `xl`  | `20` | `Design.PreviewWith` padding, outer container margin |
| `xxl` | `30` | Landing screens, outer dialog margin |

Visual scale:

```
xxs  ▎                (2 px)
xs   ▍                (3 px)
sm   ▊                (5 px)
md   ██               (10 px)
lg   ███              (15 px)
xl   ████             (20 px)
xxl  ██████           (30 px)
```

**Common compounds** (do not invent others):

| Margin | Meaning |
|--------|---------|
| `5` | Default single-value margin for inputs inside a form row |
| `10` | Content padding inside a scroll view or panel |
| `0 5 0 0` | Gap below a label row |
| `0 5 5 0` | Inline label trailing space |
| `10 0 0 0` | Button separation in a dialog action row |
| `10 10 0 0` | Standard action-row margin from content above |
| `5 5 5 0` | `subButton` margin next to a field |
| `0 10 10 10` | Window inner content inset |

### 5.6 Scrolling

| Container | Scroll? | Rules |
|-----------|---------|-------|
| Window root | ❌ | Never scroll the whole window |
| Edit window Row 1 | ✅ vertical | `VerticalScrollBarVisibility="Auto"`, `HorizontalScrollBarVisibility="Disabled"` |
| Dialog | ❌ | Dialogs must fit — `SizeToContent="Height"`; if it doesn't fit, split into a full edit window |
| `DataGrid` | ✅ both | Built-in, do **not** wrap in a `ScrollViewer` |
| Long text field | ✅ via `TextBox` | `AcceptsReturn="True"`, `TextWrapping="Wrap"`, fixed `Height` |

### 5.7 Before / after — responsive form row

**❌ Before** — fixed widths, `StackPanel`-nested, doesn't scale:

```xml
<StackPanel Orientation="Horizontal">
    <TextBlock Text="Subject:" Width="80"/>
    <TextBox Text="{Binding Subject}" Width="400"/>
    <TextBlock Text="Category:" Width="80" Margin="20,0,0,0"/>
    <ComboBox Width="200" ItemsSource="{Binding Categories}"/>
</StackPanel>
```

**✅ After** — responsive grid, class-driven labels, localized strings:

```xml
<SpacedGrid ColumnDefinitions="Auto, *, Auto, *"
            RowSpacing="15" ColumnSpacing="10">
    <TextBlock Grid.Column="0" Classes="form_label" Text="{Binding StrSubject}"/>
    <TextBox   Grid.Column="1" Text="{Binding Subject}" MinWidth="300" Margin="5"/>
    <TextBlock Grid.Column="2" Classes="form_label" Text="{Binding StrCategory}"/>
    <ComboBox  Grid.Column="3" ItemsSource="{Binding Categories}"
               SelectedItem="{Binding SelectedCategory}" MinWidth="150" Margin="5"/>
</SpacedGrid>
```

### 5.8 Anti-patterns

- ❌ `Width="400"` on a form input (use `MinWidth` and a `*` column).
- ❌ More than two nested `StackPanel`s (promote to `Grid`/`SpacedGrid`).
- ❌ `Margin="7 3 9 4"` or any value off the spacing scale.
- ❌ Wrapping a `DataGrid` in a `ScrollViewer`.
- ❌ Hard-coded `Height` on a window's content row (use `*`).
- ❌ Adding content without a `ScrollViewer` in Row 1 of an edit window — small screens WILL clip it.

---

## 6. Data Display

### 6.1 DataGrid

Canonical configuration (see [`RisksPanelView.axaml`](../src/GUIClient/Views/RisksPanelView.axaml)):

```xml
<DataGrid ItemsSource="{Binding Items, Mode=TwoWay}"
          AutoGenerateColumns="False"
          IsReadOnly="True"
          CanUserReorderColumns="True"
          CanUserResizeColumns="True"
          CanUserSortColumns="True"
          GridLinesVisibility="Horizontal"
          HorizontalScrollBarVisibility="Auto"
          VerticalScrollBarVisibility="Auto"/>
```

Standards:

- `AutoGenerateColumns="False"` always. Define columns explicitly with bound `Header="{Binding Str...}"`.
- `IsReadOnly="True"` by default — edit via a separate window/dialog, not inline.
- Column sizing:
  - **ID / icon / status columns:** fixed `Width` with `MinWidth` and `MaxWidth` (e.g. `80` / `60` / `120`).
  - **Primary subject column:** star-sized (`2*`) with a `MinWidth`.
  - **Secondary data columns:** `*` with `MinWidth` and a reasonable `MaxWidth`.
- User MUST be able to reorder, resize, and sort (three `CanUser*=True` flags).
- Status values render as `MaterialIcon` with `ToolTip.Tip` describing the state (see icon table §1.2).

### 6.2 Forms

- Label left, input right. Label uses `Classes="form_label"`, input has `Margin="5"` and `MinWidth` appropriate to the field (120 for codes, 300 for names, 150 for combos).
- Combo-boxes use an explicit `ItemTemplate` with a `TextBlock` bound to the display property.
- Date fields: `DatePicker` for date-only, `NumericUpDown` for integers.

### 6.3 Feedback

- **Loading:** overlay an `AvaloniaProgressRing:ProgressRing` centered over the content grid, `IsActive="{Binding Loading}"`, `ZIndex="1000"`, 100×100, `Foreground="CornflowerBlue"`.
- **Overlay scrim:** use `Grid#OverlayGridCtrl` for modal-blocking effects (already styled).
- **Warnings:** `TextBlock.warning` (soft) or `TextBlock.warning2` (strong).
- **Tooltips:** wrap icon/badge in a `Border` with `Classes="tooltip"` (1 s show delay) and set `ToolTip.Tip="{Binding Str...}"`.

---

## 7. Structural Patterns

### 7.1 Edit Window pattern

```
Window (1200×800, min 900×650)
└── Panel
    ├── ExperimentalAcrylicBorder (Digger, Black, 0.65)
    └── Grid RowDefinitions="Auto, *, Auto"
        ├── [0] TextBlock Classes="header" Text="{Binding WindowTitle}"
        ├── [1] ScrollViewer
        │       └── SpacedGrid (RowSpacing=15)
        │           ├── Section: TextBlock Classes="header2"
        │           ├── Fields: SpacedGrid with form_label / form_text
        │           └── …
        └── [2] Dialog action row (Save/Cancel) — see §4.2
```

Mandatory elements: acrylic border, header row, dialog action row, progress-ring overlay, localized strings.

### 7.2 Dialog pattern

Smaller focused variant — single column, no scroll, auto-height:

```
Window (500×auto, SizeToContent=Height, CenterScreen)
└── Grid ColumnDefinitions="*, Auto, *" RowDefinitions="Auto,Auto,…"
    ├── Row 0: header (spans 3 cols)
    ├── Middle rows: labeled inputs centered in col 1
    └── Last row: action row (Save/Cancel) centered in col 1
```

Reference: [`CloseDialog.axaml`](../src/GUIClient/Views/CloseDialog.axaml).

### 7.3 List/Panel View pattern

User controls embedded in `MainWindow` (e.g. [`RisksPanelView`](../src/GUIClient/Views/RisksPanelView.axaml)):

- Root: `UserControl` with `Grid RowDefinitions="*"` for single-table views, or `Auto, *, Auto` for toolbar + table + footer.
- Toolbar (when present): horizontal `StackPanel` of `Button.operation` or `Button.filterButton` icons.
- Main area: `DataGrid` per §6.1.
- Footer (when present): `Border.footer` with right-aligned summary text.

---

## 8. Navigation & Menus

- Top menu: `Menu` with single-click `MenuItem`s bound to commands; headers are `Str*` bindings.
- Debug items live under a `IsVisible="{Binding IsDebug}"` submenu and are the only place raw English strings are acceptable.
- Main navigation is the [`NavigationBar`](../src/GUIClient/Views/NavigationBar.axaml) user control, not a custom layout.
- Switching between content views in `MainWindow` is done via `IsVisible` bindings on sibling user controls, **not** by swapping children — this preserves state.

---

## 9. MVVM Contract

These conventions are not strictly visual but make the standards enforceable.

- **Strings:** every bound string property is prefixed `Str*` and sourced from resources.
- **Commands:** every button command is named `Bt<Action>Clicked`.
- **Enable flags:** every button gating flag is `Is<Action>Enabled`.
- **Loading flag:** `Loading` (bool) drives the ProgressRing.
- **Titles:** set `Title="{Binding WindowTitle}"` on the `Window`; expose `WindowTitle` on the view-model rather than hard-coding.
- **Code-behind:** minimal — event handlers only when required by Avalonia (`Opened`, custom hit-testing). No business logic.

---

## 10. Do / Don't Quick Reference

**Do**
- Use existing style classes; add a new class (in [`WindowStyles.axaml`](../src/GUIClient/Styles/WindowStyles.axaml)) before adding a new color or font setting to a view.
- Bind every string through `Str*` + resources.
- Use `Grid` / `SpacedGrid` with `*` / `Auto` sizing for responsiveness.
- Put primary dialog action on the **left**, secondary on the right.
- Use Material Icons with the canonical `Kind` from §1.2.
- Keep window min-size wide enough that no horizontal scroll is needed at default density.

**Don't**
- Don't hard-code colors, font sizes, or font weights in views.
- Don't write English strings directly in `.axaml` (except debug menus).
- Don't size form inputs with fixed `Width` when `MinWidth` will do.
- Don't create new button visual variants — extend the class taxonomy instead.
- Don't duplicate status icons; reuse the Material Icon already used elsewhere for that state.
- Don't put business logic in code-behind.

---

## 11. Adding a New Window — Checklist

Before opening a PR with a new window or dialog:

- [ ] Window wraps content in `ExperimentalAcrylicBorder` (for full windows) OR is a dialog with `CenterScreen` + `SizeToContent="Height"`.
- [ ] Title bound via `Title="{Binding WindowTitle}"`.
- [ ] All user-visible strings are `Str*` bindings and present in **every** locale `.resx`.
- [ ] Layout uses `Grid` / `SpacedGrid` with responsive sizing — no fixed widths on text inputs unless there's a genuine domain reason.
- [ ] Header uses `TextBlock.header`; section headers use `header2` / `header3`.
- [ ] Buttons use `dialog1` / `dialog2` (or another taxonomy class); commands named `Bt<Action>Clicked`.
- [ ] `ProgressRing` overlay is wired to a `Loading` boolean.
- [ ] Material Icons use the canonical `Kind` from §1.2.
- [ ] No hard-coded hex colors in the `.axaml`.
- [ ] Window min-size set so the layout is usable without horizontal scrolling.
- [ ] Code-behind is empty or contains only framework-required event handlers.

---

## 12. Known Deviations (migration backlog)

These are acknowledged deviations to clean up over time — do not use them as precedent:

- [`LoginWindow.axaml`](../src/GUIClient/Views/LoginWindow.axaml) — uses `Rectangle` spacers instead of `Margin`; layout should migrate to `Grid` with column definitions.
- [`teste.axaml`](../src/GUIClient/Views/teste.axaml) — scratch file; should be removed.
- Some older dialogs do not apply `Classes="dialog1"` / `dialog2` on their buttons — migrate when touching them.
- Inline button `Margin=" 0 0 10 0 "` with irregular spacing in several edit windows — normalize to the scale in §5.3.

Track these under the documentation / UX milestone in [ROADMAP.md](../ROADMAP.md).

# Settings-form vocabulary rollout — UI-STD-002

Date: 2026-09-24
Standard: [`docs/ui-standard.md`](../docs/ui-standard.md) §2, §6.2
Enforcement: [`src/GUIClient.Tests/Views/SettingsFormVocabularyTest.cs`](../src/GUIClient.Tests/Views/SettingsFormVocabularyTest.cs)
Status: **complete** — `detailBlock` is deleted; all five phases landed

## Why

Every settings pane in the GUI was the same triplet repeated — label, control, explanation — and
two style classes rendered the two supporting elements as the loudest things on screen.

| Class | Definition | Effect |
|---|---|---|
| `TextBlock.detailBlock` | `Foreground=Black`, `Background=DarkGray` | No padding, no radius, stretched to the panel. Black on `#A9A9A9` is the highest-contrast element in a dark window, so a field's explanation outranked the field. **56 uses.** |
| `TextBlock.header2` | `Foreground=#bbbbbb`, `Background=#5252aa` | §2.4 documents `#5252aa` (`semantic/info-bg`) as a **section band**. Used as a per-field label it inverted the hierarchy. **169 uses.** |

Layout compounded it: labels stretched, `TextBox`es sat at `MinWidth="240"` and hints at
`MaxWidth="360"`, so three different right edges stacked down each column.

## The vocabulary

All in [`WindowStyles.axaml`](../src/GUIClient/Styles/WindowStyles.axaml). Every colour is an
existing token, so §2.6's "no new hex within ΔE 5" holds by construction — no new palette entry and
no process issue to open.

| Class | Use | Token reused |
|---|---|---|
| `TextBlock.hint` | Field help. **No background.** | `#9a9a9a` from `metric_detail` |
| `TextBlock.notice` | A statement about state | `#F0F0F0` text/primary |
| `TextBlock.fieldLabel` | Label for one field | `#bbbbbb` text/secondary |
| `TextBlock.sectionCaption` | Names a `formCard` | `#9a9a9a` |
| `Border.formCard` | Groups a run of fields | the `card` surface |
| `Border.formCard.caution` | A setting that *weakens* a control | `#ff9800` from `toast.warning` (outline, heavier left edge) |
| `MaterialIcon.{caution,success,error}Icon` | Severity by shape | the `toast` severity hues |

`TextBlock.formData` already existed and is the fourth destination: a record's **value** in a
read-only detail panel, as distinct from help (`hint`) or a state report (`notice`).

### Rules

1. `detailBlock` is deleted. Help is `hint`, a state report is `notice`, a record's value is
   `formData`.
2. `header2` keeps its documented job as a band over a whole section. A label for one field uses
   `fieldLabel`.
3. Severity rides on an icon's shape with a matching hue, never on text colour alone (§2.6).
4. **Inputs inside a `formCard` stretch, so they may not sit in a horizontal `StackPanel`** — that
   panel measures a stretched child with infinite width, so the box grows without bound and any
   wrapped text beside it never wraps. Use `Grid` with `ColumnDefinitions`. This is a test, not a
   convention: `NoFormCardPutsAnInputInAHorizontalStackPanel`.
5. Sizing a card column: `MaxWidth` + `HorizontalAlignment="Left"` sizes the panel to its
   *content*, so a card of short inputs collapses to a sliver; `MaxWidth` + `Stretch` centres it in
   the column. Only an explicit `Width` + `Left` both fills the card and keeps it left-aligned.
6. An icon style selector must name `MaterialIcon`. A bare `.class` selector has no target type and
   compiled XAML fails with `AVLN2200`.

### Rejected alternatives

- **Take FluentAvalonia for `InfoBar` / `SettingsExpander`.** Those controls are not in core
  Avalonia, and FluentAvalonia's Fluent v2 resource keys (`TextFillColorSecondaryBrush`,
  `CardBackgroundFillColorDefaultBrush`) are **not shipped by Avalonia's built-in `FluentTheme`** —
  checked against `Accents/BaseResources.xaml` and `BaseColorsPalette.xaml`, which carry only the
  WinUI v1 `SystemControl*Brush` keys. Using them would silently resolve to nothing. `Border.card`
  plus the core `Expander` already cover the need.
- **Bind to the built-in `SystemControl*Brush` resources instead of literal hexes.** §2 makes
  literal tokens in `WindowStyles.axaml` the single source of colour; two colour systems in one
  stylesheet is worse than either. Revisit only as a whole-palette migration.
- **Extract the vault-picker row into a `UserControl`.** It repeats six times, but each instance
  binds a different field-state object and passes a different `CommandParameter`, so the control
  would need five styled properties plus command forwarding — on the credential path. Deferred as
  its own refactor; see *Still open* below.

## What landed

| Phase | Scope | Result |
|---|---|---|
| 1 | Secret Vaults tab | 3 cards + caution card + last-test status card |
| 2 | The other six Integrations tabs, then `JiraIntegrationView` | 22 cards, 52 field labels |
| 3 | `ConfigurationView`, `ApiTokensView`; audited `UsersView`, `EntitiesView` | 2 cards; the 8 `UsersView` bands were genuine and were left alone |
| 4 | `GovernanceAdminView`, `FindingsAdminView`, `RiskGovernanceWindow`, `SecretVaultPickerDialog` | 13 field labels, 5 hints, 6 notices |
| 5 | `VulnerabilitiesView`, then delete the class | 22 `detailBlock` → `formData`, 17 field labels |

Final census: **`detailBlock` 0** (was 56, style deleted), **`header2` 74** (was 169) — every
survivor is a section band doing its documented job.

Defects found and fixed while doing the work, none of which a unit test could have predicted:

- Two icon-plus-text rows used a horizontal `StackPanel`, so the caution hint on *Ignore SSL
  errors* ran off the card instead of wrapping. Now rule 4 above, with a test.
- Cards collapsed to a sliver on panes whose fields are all short inputs, then centred when that
  was "fixed" with `Stretch`. Now rule 5.
- The posture tab's editor row was on an equal star share with two logs and a progress trail;
  cards are taller than the bare form was, so the editor showed two fields and a scrollbar. Row
  is now `2*`.

## How it stays fixed

`./build.sh LintUi` cannot see any of this — `detailBlock` was a defined class used in a valid
way — so `SettingsFormVocabularyTest` carries the guards:

- **`NoViewReferencesARetiredClass`** — no view may reference `detailBlock` again.
- **`NoStyleDefinesARetiredClass`** — re-adding the style would silently re-legalise every old call
  site, so redefining it fails too.
- **`TheSettingsFormVocabulary_IsDefinedByAStyle`** — Avalonia never reports an unmatched selector,
  so a deleted style would render every migrated view unstyled and silently.
- **`EverySettingsView_UsesFieldLabels`** — a screen rewritten back to bare `header2` labels fails
  rather than drifting.
- **`NoFormCardPutsAnInputInAHorizontalStackPanel`** — rule 4, which was a real defect twice.

Both directions of the original ratchet, and the StackPanel guard, were verified against the real
tree by temporarily breaking it before the guard was trusted.

## Still open

- **The vault-picker row** repeats six times across four tabs (`DisplayText` + "will be saved" +
  two buttons). A `UserControl` would remove ~120 lines, but it touches the credential path and
  needs its own tests; worth doing as a standalone change.
- **`RiskGovernanceWindow`'s three score chips** (inherent / residual / delta) still use `header2`
  to render a *number* as a purple chip. That is neither a band nor a field label, but it is
  deliberate emphasis on the headline figures of a non-settings screen, so it was left alone rather
  than flattened. Decide on a `metric`-style treatment if that screen is revisited.
- **The remaining 74 `header2`** are section bands. The band itself — a full-bleed `#5252aa` strip —
  is still a heavy treatment for a section heading; whether it should become a caption-plus-rule is
  a separate design question, not part of this rollout.

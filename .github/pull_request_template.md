<!--
  Keep this short. Delete any section that does not apply, but do not delete a checklist item to
  make it pass — say why it does not apply instead.
-->

## What this changes

<!-- One or two sentences. What behaviour is different after this PR? -->

## Why

<!-- The problem, the ticket, or the roadmap item this closes. -->

## How it was verified

<!-- Commands run, tests added, and — for GUI work — what was actually observed on screen.
     "It builds" is not verification: AXAML resource-key mistakes compile fine and fail at
     runtime. -->

---

## Checklist

### Tests

- [ ] New behaviour lands with tests for the happy path **and** each error/guard branch
      ([src/AI_TESTING_INSTRUCTIONS.md](../src/AI_TESTING_INSTRUCTIONS.md)).
- [ ] A bug fix lands with a regression test that fails on the pre-fix code.
- [ ] No assertion was weakened, skipped or deleted to get a green run. Any defect found but not
      fixed is reported in the PR description.
- [ ] `dotnet test src/netrisk.sln` passes (integration tests need Docker; say so if skipped).

### Database

- [ ] Schema change authored as an EF migration **and** split into the next numbered
      `src/ConsoleClient/DB/Structure/{n}.sql` + `DB/Data/{n}.sql`, with `targetVersion` bumped in
      `DB/DatabaseInformation.yaml`.
- [ ] Every `Structure` statement is guarded so the script is safe to apply twice; `Data` is pure
      DML inside a transaction.
- [ ] New entities are born compliant with the Track 6 conventions (snake_case plural tables,
      `fk_`/`idx_`/`uq_` names, `created_at`/`updated_at`, `tinyint(1)` booleans,
      `varchar(n)` — never `char(n)` for a `string`).

### Security

- [ ] Every new API action carries `[Authorize]` or `[PermissionAuthorize]`; any new
      `[AllowAnonymous]` is on the justified allowlist.
- [ ] Credentials are consumed through `ISecretResolver`, secrets stored through `ISecretProtector`,
      randomness from `Tools.RandomGenerator`, outbound HTTP through `IOutboundHttpClient`.
- [ ] No credential, key or certificate is added to the repository.
- [ ] Every security claim in the description names the code or the test that establishes it.

### UI compliance (desktop GUI)

Applies to any change touching `src/GUIClient/Views/**/*.axaml`. The standard is
[docs/ui-standard.md](../docs/ui-standard.md); the gate is `./build.sh LintUi`, which CI runs in
[.github/workflows/ui-compliance.yml](workflows/ui-compliance.yml) and which fails on any
violation.

- [ ] `./build.sh LintUi` reports **0 violations**.
- [ ] No hard-coded `Background`/`Foreground`/`BorderBrush` hex literal and no named status brush
      (`Red`, `Green`, `Orange`, …) in a view — colours come from a style class in
      `Styles/WindowStyles.axaml` (§2.6). A genuinely new semantic colour was added to that file
      with its contrast check, not inlined in the view.
- [ ] Every user-facing string — including window `Title`, `Content`, `Header`, `ToolTip.Tip` and
      `Watermark` — is bound to a `Str*` resource property, and the key was added to **all three**
      `Localization*.resx` files (§3.2).
- [ ] Every `Button` carries a class from the taxonomy (`dialog1`, `dialog2`, `type2`, `type3`,
      `operation`, `navigation`, `subButton`, `detailButton`, `filterButton`, `link`) and, when
      icon-only, a `ToolTip.Tip` (§4).
- [ ] Spacing, typography and sizing use the standard scale and classes; no ad-hoc `FontSize`,
      `FontWeight` or fixed input `Width` (§3, §5).
- [ ] Anything that legitimately cannot comply carries an in-markup waiver **with a reason**
      (`<!-- ui-lint-waive R5: … -->`) — a rule was not loosened, and the waiver is called out in
      the PR description.
- [ ] The window was **run and looked at** (not just compiled), and the screenshot or a description
      of what was observed is in "How it was verified".

### Docs

- [ ] User-visible changes recorded under `[NEXT] - Unreleased` in
      [CHANGELOG.md](../CHANGELOG.md).
- [ ] Affected docs under `docs/` updated; roadmap status notes reflect what actually shipped.

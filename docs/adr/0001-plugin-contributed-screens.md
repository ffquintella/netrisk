# ADR 0001 — Plugin-contributed screens

- Date: 2026-09-28
- Status: **accepted** — implemented in the SDK, the host and BastionVault 1.8.0
- Scope: `netrisk-plugin-sdk` (`Contracts`), host (`Model`, `ServerServices`, `API`, `GUIClient`),
  `netrisk-plugin-bastionvault-integration` as the reference implementation
- Supersedes nothing. Sets the rule that replaces the workaround described in "The trigger".

## The trigger

BastionVault KV v2 secrets carry per-environment overrides. One secret
(`secret/trend/netrisk-dsv`) holds a different value in `hml` and in `prd`; the server merges base
and override and answers with **one** value, and nothing in the response says which environment it
came from. A credential can also be *environment-scoped*, in which case the vault refuses every
read that does not name an environment.

The picker has no place to put that. `SecretVaultPickerDialog.axaml` is a connection combo, a
filter, a `Secret | Id | Version` grid and a `Field` box; the grid is built from
`VaultSecretDescriptor` and what is saved is `VaultSecretReference { SecretId, Field }`. So the
plugin (v1.6.0/v1.7.0) encoded the environment inside the id — `secret/trend/netrisk-dsv?env=hml`
(`BastionVaultEnvironment.Marker`) — expanded the listing to one row per environment
(`BastionVaultSecretPlugin.AddAll`, `EnvironmentsOfAsync`), renamed rows to `netrisk-dsv (hml)` so
two rows of the same path could be told apart, and had to make `TestConnectionAsync` de-duplicate
before counting so the operator would see a number matching their vault.

That was the best available answer inside the contract, and it is wrong in four ways:

1. **The id stopped being opaque.** The host stores, indexes, prefix-scans
   (`SecretVaultService.CountReferencesAsync`) and displays a string that now has a grammar, and
   the grammar belongs to one plugin.
2. **Cartesian listing.** 2000 secrets x 3 environments is a grid to filter, not to choose from,
   and the plugin's `MaxSecrets = 2000` cap now counts rows instead of secrets.
3. **No discovery and no validation.** The environment should be a choice from a closed list with a
   label and help; it became text inside other text.
4. **The concept does not exist for the host.** It cannot be a column, a filter, or a warning that
   a scoped credential requires it.

## Decision

**A screen that is specific to a plugin is declared by the plugin and rendered by the host.**

The plugin publishes a *declarative field specification* — control kind, label, help, required,
validation — in SDK objects. The host renders it with its own controls. Option lists for a choice
control are **fetched by a contract call** with the connection's `SecretVaultContext`, never
embedded in the static declaration. The values the operator produces travel in their own place —
the reference's `Options`, the connection's `Options` — never inside another field's text.

Environment is the first case, not the feature.

### Non-negotiables this satisfies

| Constraint | How |
|---|---|
| Declarative, never executable | The SDK exposes data classes only. No HTML/XAML/script, no view loaded from the plugin assembly, no delegate the host invokes for rendering. The plugin remains third-party code behind `McMaster` `PluginLoader` (`PluginsService.LoadPluginsAsync`), and the only thing crossing the boundary is a list of records. |
| Values come by call | `GetFieldOptionsAsync(context, query, ct)`. The static spec carries no options. |
| No secret on the choice screen | `VaultSecretDescriptor`'s no-value rule extends verbatim: a `PluginFieldOption` is a value + a label, the contract documents that neither may be a secret, and no option-bearing field may be declared on a screen that would then display it back. Reading a secret to populate a picker is forbidden for the same reason field names are not enumerated today. |
| Backward compatible both ways | Every new interface member is a **default interface member**, the precedent being `RequiresAppId`. Old plugin + new host: declares nothing, host renders exactly today's screen. New plugin + old host: never asked, must still work — hence the dual-accept window in the reference implementation. |
| Stateless plugins | The declaration is a pure property computed per instantiation; `GetFieldOptionsAsync` receives everything it needs in `context` + `query`. Nothing is carried between calls. The host instantiates per call (`PluginsService.Candidates<T>`) and that does not change. |
| Stored references keep resolving | The v1 wire format is untouched and is still what the host writes whenever no plugin field has a value. See "The reference". |

## The contract (proposed signatures)

New namespace `Contracts.Ui` in the SDK. Vocabulary first, capability wiring second.

```csharp
namespace Contracts.Ui;

/// <summary>The control a field is rendered as. Closed: the host renders only what it knows.</summary>
public enum PluginFieldKind
{
    Text,      // single-line text box
    Choice,    // combo, options fetched by GetFieldOptionsAsync
    Toggle,    // check box, value "true"/"false"
    Number     // integer spinner
}

/// <summary>How far the host validates a Text field before it lets the operator commit.</summary>
public enum PluginFieldFormat
{
    Any,
    NoWhitespace,
    /// <summary>Letters, digits, '-', '_', '.' — a name, not a path.</summary>
    Identifier
}

/// <summary>One control a plugin contributes to a host screen.</summary>
public sealed class PluginFieldSpec
{
    /// <summary>
    /// Stable key. Lower-case, [a-z0-9_.-], max 32 chars. It is what the value is stored under, so
    /// renaming it orphans values the same way renaming a column would.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Label, in the plugin's own words. See "Text and translation".</summary>
    public required string Label { get; init; }

    /// <summary>One sentence under the control. Optional.</summary>
    public string? Help { get; init; }

    public PluginFieldKind Kind { get; init; } = PluginFieldKind.Text;

    public bool Required { get; init; }

    /// <summary>Pre-filled value. Must be one of the fetched options when Kind is Choice.</summary>
    public string? DefaultValue { get; init; }

    public PluginFieldFormat Format { get; init; } = PluginFieldFormat.Any;

    /// <summary>Upper bound the host enforces. Clamped by the host to 256.</summary>
    public int MaxLength { get; init; } = 128;

    /// <summary>
    /// For Choice: whether the operator may also type a value the plugin did not offer. False makes
    /// it a closed list; true makes it a suggestion list, for a vault that cannot enumerate
    /// everything it will accept.
    /// </summary>
    public bool AllowCustomValue { get; init; }

    /// <summary>
    /// For Choice: whether the option list depends on the secret selected on the screen. True makes
    /// the host re-fetch on every selection change; false makes it fetch once per connection.
    /// </summary>
    public bool OptionsDependOnSecret { get; init; }
}

/// <summary>One option of a Choice field. Never a secret, and never derived from a secret's value.</summary>
public sealed class PluginFieldOption
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; }
}

/// <summary>The host screens a plugin may contribute to. Closed; an unknown value is ignored.</summary>
public enum PluginScreen
{
    /// <summary>"Select a secret from the vault" — SecretVaultPickerDialog.</summary>
    VaultSecretSelector,

    /// <summary>The vault connection editor on the Integrations screen.</summary>
    VaultConnectionEditor
}

/// <summary>What the host knows when it asks for a field's options.</summary>
public sealed class PluginFieldQuery
{
    public required PluginScreen Screen { get; init; }
    public required string FieldKey { get; init; }

    /// <summary>The secret the operator has selected, when the screen has one and one is selected.</summary>
    public string? SecretId { get; init; }

    /// <summary>The plugin's other fields as filled so far, so one choice can narrow another.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } =
        new Dictionary<string, string>();
}
```

On `INetriskSecretVaultPlugin`, three default-implemented members:

```csharp
/// <summary>
/// Extra controls for one host screen, or empty for "render the screen as it is". Pure: no I/O,
/// no credential, no state — it is called to build a form, possibly before a connection exists.
/// </summary>
IReadOnlyList<PluginFieldSpec> DescribeScreen(PluginScreen screen) => [];

/// <summary>
/// The options for one Choice field. Called with the connection's context, so the list may depend
/// on the credential. Must return metadata only; a failure is a SecretVaultException.
/// </summary>
Task<IReadOnlyList<PluginFieldOption>> GetFieldOptionsAsync(SecretVaultContext context,
    PluginFieldQuery query, CancellationToken ct = default)
    => Task.FromResult<IReadOnlyList<PluginFieldOption>>([]);

/// <summary>
/// A stored reference in this plugin's older, self-encoded form, rewritten into the contract's
/// form — or the same reference when there is nothing to rewrite. Pure and offline. Lets a plugin
/// retire a grammar it once had to invent without the host knowing what that grammar was.
/// </summary>
VaultSecretReference NormalizeReference(VaultSecretReference reference) => reference;
```

And `VaultSecretReference` gains one property:

```csharp
/// <summary>
/// The values the plugin's own controls produced, keyed by PluginFieldSpec.Key. Empty for a
/// reference saved before the plugin declared anything, which is what every stored reference is.
/// </summary>
public IReadOnlyDictionary<string, string> Options { get; init; } =
    new Dictionary<string, string>();
```

Adding an init-only property with a default is source- and binary-compatible for a plugin compiled
against the earlier SDK: it constructs the object with the two members it knows and reads the two
it knows.

## The rendering model

The host renders, the plugin declares. Concretely, in `GUIClient`:

- A `PluginFieldViewModel` per spec (key, label, help, value, options, error) and an
  `ItemsControl` with a `DataTemplateSelector` over `PluginFieldKind` — four templates, one per
  kind, built from the existing `docs/ui-standard.md` §6.2 vocabulary (`fieldLabel`, `hint`,
  `formCard`). No new style class, no new colour, so `./build.sh LintUi` stays green and the
  §2.6 palette rule holds by construction.
- The specs reach the client as a new `List<PluginFieldSpecView>` on `SecretVaultPluginInfo`
  (already fetched by `GET /SecretVaults/plugins`), and options through one new endpoint,
  `POST /SecretVaults/{id}/field-options` with the `PluginFieldQuery` in the body — a body and not
  a query string because the selected secret id is in it and a secret id does not belong in an
  access log (same rule as `SecretReferenceDescribeRequest`).
- Validation runs twice and the second one is the real one: the client enforces
  `Required`/`MaxLength`/`Format`/membership to give an operator an error before they commit, and
  `SecretVaultService` enforces the same rules server-side before persisting a reference. A plugin
  is the final authority on its own values and must still reject a bad one in `GetSecretAsync` —
  the declaration is UI affordance, not a security boundary.
- Host caps, enforced in `SecretVaultService` when it projects the specs: at most 8 fields per
  screen, key matching `^[a-z0-9][a-z0-9_.-]{0,31}$`, `MaxLength` clamped to 256, at most 200
  options per fetch, label/help truncated at 120/300 chars. A plugin that exceeds one has its
  declaration dropped with a logged warning, and the screen renders as it does today. The host
  renders no plugin text it has not length-bounded.

**Why no regex validation.** An obvious `Pattern { get; init; }` was rejected: a pattern from a
plugin, evaluated by the host on every keystroke, is a ReDoS against the host's UI thread and the
API. `RegexOptions.NonBacktracking` plus a timeout would defuse it, but it buys expressiveness the
first three cases do not need — the environment case is a closed list, where validity is membership.
`PluginFieldFormat` covers the rest, and the plugin validates for real on the read path.

## The reference

`Model.Secrets.SecretReference` gains `IReadOnlyDictionary<string, string> Options`, and a second
wire version that is **only emitted when there are options**:

```
v1 (unchanged):  vault:v1:{connectionId}:{b64url(secretId)}[:{b64url(field)}]
v2 (new):        vault:v2:{connectionId}:{b64url(secretId)}:{b64url(field)}:{b64url(options)}
```

- `field` is the empty string in v2 when there is none — v2 is fixed-arity, so there is no
  ambiguity between "no field" and "no options".
- `options` is `k=v&k=v`, keys sorted ordinal, each part percent-encoded, then base64url'd. Sorted
  so that the cache key (`SecretVaultService.CacheKey`, built from the canonical string) is stable
  for the same selection.
- `ToString()` emits v1 whenever `Options` is empty. **Every reference in every installed database
  stays byte-identical**, and a field an operator never re-picks never changes.
- `TryParse` accepts both. `IsReference` matches the `vault:v` family. `CountReferencesAsync`'s
  prefix scan becomes two `StartsWith` per column (`vault:v1:{id}:` and `vault:v2:{id}:`) — the one
  place in the host that pattern-matches the format, and the one that must not be missed, since
  missing it makes "delete a connection still in use" allowed.
- `DisplayKey` appends the options (`secret/trend/netrisk-dsv / password (environment=hml)`), so a
  log line and the caption under a credential field say which one was read. This is the point of
  the whole change: today that information is invisible unless somebody knows the `?env=` grammar.

A v2 reference written by a new host and read by an **older** host fails closed: `TryParse` returns
false, `SecretResolver` throws its "looks like a vault reference but could not be parsed" error, and
nothing is sent to a third party. That is the correct failure for a downgrade, and it only happens
to a field re-picked after the upgrade.

## Connection-editor fields

Same vocabulary, two differences.

- The values go in a new `extra_settings LONGTEXT NULL` column on `secret_vault_connections`
  (JSON, `snake_case`, per the Track 6 table in `CLAUDE.md`), reaching the plugin as a new
  `IReadOnlyDictionary<string, string> Options` on `SecretVaultCredentials`. One column and not one
  per field, because the schema must not change when a plugin adds a control — that is the
  acceptance criterion.
- Options for a connection field must be fetchable **before the connection exists**. The host
  builds a context from the draft (base URL + typed API key) and, when the draft is not yet
  complete enough, skips the fetch and renders the field as free text with its help visible. A
  plugin must tolerate being asked with a context that does not authenticate and answer with an
  empty list rather than throwing.
- **No secret-valued connection fields in v1.** A plugin field is stored and returned in the clear,
  like `MachineId` and `AppId`; a credential needs `ISecretProtector`, the never-return rule, the
  "null means unchanged" update convention and redaction in the test message. If a vault genuinely
  needs a second credential, that is its own ADR.

This also retires the shape of the `RequiresAppId` patch: a future "this vault needs field X"
is a declaration, not a new boolean on the interface.

## Migration of the `?env=` references

**No host-side data migration, by design.** `secret/trend/netrisk-dsv?env=hml` is inside the
base64-encoded `SecretId` of a v1 reference. Only BastionVault knows that `?env=` means anything,
and a host that parsed it would be committing the exact error this ADR exists to end.

So the migration is the plugin's, in three steps:

1. **v1.8.0 accepts both.** `GetSecretAsync` reads the environment from
   `reference.Options["environment"]` when present, and otherwise from
   `BastionVaultEnvironment.Split(reference.SecretId)` exactly as today. Every stored reference
   keeps resolving, unchanged, with no operator action.
2. **v1.8.0 stops producing the old form.** `ListSecretsAsync` returns one descriptor per secret
   again — no `AddAll` expansion, no `(hml)` suffix in the name, and `TestConnectionAsync` counts
   descriptors directly because rows are secrets again. The environment comes from a declared
   `Choice` field fed by `GetFieldOptionsAsync`, which is `EnvironmentsOfAsync` rewritten to answer
   per query instead of per row: credential scope first (`BastionVaultEnvironment.Concrete`), the
   mount's KV2-024 registry second.
3. **`NormalizeReference` retires it.** The plugin returns the `?env=` reference rewritten into
   `{ SecretId = "secret/trend/netrisk-dsv", Options = { environment = "hml" } }`. The host calls it
   on the resolve and describe paths so the *displayed* reference is already the clean one, and a
   console command (`netrisk-console vault normalize-references [--dry-run]`) walks the credential
   columns in `CountReferencesAsync`'s registry and persists the rewrite. Dry-run first; the command
   is the only thing that writes, and it writes only what the plugin itself produced.

The dual-accept in step 1 stays for at least one minor version after the command ships, and the
deprecation is a line in the plugin's own changelog, not a host concern.

## Text and translation

What the host's scheme actually is, checked before proposing anything: `Localizer["Key"]` against
three files — `src/GUIClient/Resources/Localization.resx`, `.en-US.resx`, `.pt-BR.resx` — and
`GUIClient.Tests` fails a key that resolves in none of them. It is a **closed, host-owned** scheme:
a plugin cannot add a key to it, and a dynamic `Localizer[pluginKey]` would both fail that test and
turn a plugin string into a key lookup that renders as the key name when it misses.

There is already a precedent for plugin-authored text on screen:
`SecretVaultPluginInfo.Description` is `INetriskPlugin.PluginDescription`, rendered raw.

So: **host chrome stays in the resx; plugin-owned strings come from the plugin.** `Label` and `Help`
are the plugin's plain text, and `PluginFieldSpec` carries an optional
`IReadOnlyDictionary<string, string> LabelTranslations` / `HelpTranslations` keyed by culture name
(`"pt-BR"`), which the host resolves against `CultureInfo.CurrentUICulture` — exact match, then the
neutral culture, then `Label`. No new host scheme, nothing added to the resx, and a plugin that
supplies only English still renders. Everything around the plugin's fields — the section caption,
the validation messages, the "this vault needs a value here" error — is host copy and goes in the
resx as usual.

## Alternatives rejected

**Do nothing; each plugin encodes what it needs in the id.** This is the status quo, and it does
work: v1.7.0 ships. It is rejected because the cost is paid by the host and by every other consumer
of that column. The id is prefix-scanned by `CountReferencesAsync`, displayed by `DescribeAsync`,
logged by `SecretResolver`, and will be read by the next report or migration that touches a
credential column — each of them silently depending on a grammar no contract documents. The
cartesian listing and the row-counting cap are the visible symptoms; the invisible one is that two
plugins are free to invent conflicting grammars for the same column.

**A first-class `Environment` field on the contract.** Smallest possible change, and it fixes
BastionVault today. Rejected: it is `RequiresAppId` again. The contract already carries one patch
shaped like this, and the next vault will want a namespace, a mount or a tenant. A vocabulary costs
more once and nothing thereafter — the acceptance criterion "a plugin declares a new control with no
host change" is unreachable by any number of named fields.

**A view (XAML/HTML/Razor) loaded from the plugin assembly.** Maximum expressiveness, and every
other extension model does it. Rejected outright: a plugin is third-party code already running with
the API's full authority (`PluginsService` logs exactly this when a signature is missing), and a
view from it is an execution surface inside the operator's session, on the screen where the estate's
credentials are bound. Declarative data cannot execute.

**A JSON-Schema document from the plugin, rendered by a generic form library.** Expressive,
standard, and someone else's parser. Rejected for this iteration: it brings a dependency into the
host's credential path, its validation vocabulary is far wider than four control kinds, and
`$ref`/`allOf` make the "closed set the host knows how to render" property impossible to state.
Revisit if a third capability needs richer forms.

**Options embedded in the static declaration.** Simplest possible option list. Rejected by the
constraint and by the facts: BastionVault's environments come from the credential's own scope
(`approle_env_*` metadata) and the mount's KV2-024 registry, so two connections to two vaults with
the same plugin have different lists. A static list would be wrong for one of them.

**Reference format: append a fourth segment to v1.** Shorter diff. Rejected: today's `TryParse`
does `Split(':', 3)`, so a fourth segment lands inside the decoded `field` and a v1 parser accepts
a reference whose field is wrong rather than rejecting it — a silently wrong credential, which is
the one failure mode this whole subsystem is built to avoid. A distinct `v2` tag fails closed.

**Reference format: `vault:v2:{conn}:{b64(json)}`.** One segment, arbitrary structure. Rejected:
nothing in the reference is readable any more, including by a human reading a support ticket, and
the canonical-form requirement (for the cache key) becomes "canonical JSON", which is a worse
problem than "sorted key=value".

## Consequences

- The host learns one concept ("a plugin may contribute fields to a screen") and no vault-specific
  ones. The next plugin that needs a namespace or a tenant writes a `PluginFieldSpec`.
- One new nullable column, one new API endpoint, one new reference wire version that no existing
  row uses.
- `SecretReference`'s format is no longer trivially greppable by hand for references with options;
  `DisplayKey` and `DescribeAsync` are the supported way to read one, which they already were.
- The host now renders text it did not author. Bounded by the caps above, and already true of
  `PluginDescription`.
- Third candidate screen, deferred: importers already have `ImportContext.Options`
  (`Dictionary<string, string>`) with **no UI at all** — options can only be set by whatever writes
  the scanner configuration. That is the same hole, and the same vocabulary fits it, but no screen
  exists to extend and no importer plugin currently asks for one. Out of scope here; the enum
  gains a member when it is.

## Implementation order

1. **SDK** — `Contracts.Ui`, the three default members, `VaultSecretReference.Options`,
   `SecretVaultCredentials.Options`. Version bump; submodule pin updated in the host.
2. **Host, server** — `SecretReference` v2 + both-prefix scanning, projection and capping of the
   specs onto `SecretVaultPluginInfo`, the field-options endpoint, server-side validation,
   `NormalizeReference` on the resolve/describe paths, the `extra_settings` column
   (EF migration + numbered `Structure`/`Data` pair + `DatabaseInformation.yaml` bump, per
   `CLAUDE.md`), and the console normalize command.
3. **Host, client** — `PluginFieldViewModel` + the four templates, wired into
   `SecretVaultPickerViewModel` and `IntegrationsViewModel`'s vault editor.
4. **BastionVault** — declare the `environment` Choice field, fold `EnvironmentsOfAsync` into
   `GetFieldOptionsAsync`, delete `AddAll`'s expansion, restore the direct count in
   `TestConnectionAsync`, keep `BastionVaultEnvironment.Split` on the read path and implement
   `NormalizeReference` with it.

Tests land with each step, per `CLAUDE.md` and `src/AI_TESTING_INSTRUCTIONS.md`: round-trip and
rejection cases for the v2 format, the both-prefix count, a fixture plugin
(`src/Plugins/FixtureVaultPlugin`) that declares one field of each kind plus one that violates every
cap, and a BastionVault test that resolves a stored `?env=` reference and a declared-option
reference to the same value.

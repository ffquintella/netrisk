# External secret vaults (BastionVault)

NetRisk stores a lot of other people's credentials: Vision One API keys, Jira personal access tokens,
Slack webhook URLs, OIDC client secrets, SCIM tokens, the backup passphrase. Encrypting them at rest
([`ISecretProtector`](../../src/ServerServices/Interfaces/ISecretProtector.cs)) means a stolen
database dump is ciphertext rather than a working credential — but the credentials are still *in*
NetRisk, and rotating one means editing it here as well as wherever it came from.

This feature removes them. A credential field can instead hold a **reference** to a secret in an
external vault, and NetRisk resolves it at the moment of use. The first implementation talks to
**BastionVault**, and it does so through a **plugin** — so any other vault can be supported by
writing one, without touching NetRisk.

---

## What an operator does

1. **Install the plugin.** `BastionVaultPlugin.dll` and its `.deps.json` go in `Plugins/Secrets/`
   under the API **and** under the background-job host, which also resolves credentials — a plugin
   present in only one of them is an integration that works during the day and fails at night. The
   plugin is built and released from its own repository, [netrisk-plugin-bastionvault-integration](https://github.com/ffquintella/netrisk-plugin-bastionvault-integration);
   it is not part of a NetRisk build.
2. **Enable it** under *Admin → Plugins*. Installing is not enabling: a DLL in the directory
   activates nothing on its own.
3. **Add a vault connection** under *Admin → Integrations → Secret Vaults*: a name, the plugin, the
   vault's base URL (including its port, typically `8200`), the **API key — which for BastionVault is
   a vault token**, sent as `X-Vault-Token` — and optionally the machine ID (see below). Everything
   the connection can reach is whatever that token's policies allow, so NetRisk's access is scoped in
   the vault rather than here. Press *Test* — it introspects the token, reports its policies and how
   many secrets it can see, and checks the machine binding. "Authenticated" and "authenticated and
   useful" are different answers and it distinguishes them.
4. **Bind a credential field.** Every secret box on the Integrations screen grows a key icon beside
   it. Pressing it lists the secrets the connection can see; choose one, and **type the field** of it
   to use (`password`, say) — leave the field empty only for a secret that holds a single value. The
   field is typed rather than picked because a BastionVault listing reveals no field names, and the
   only way to learn them would be to read the secret, writing an access record in the vault's audit
   log for every click. A wrong field is reported at resolution time by a message naming the fields
   that do exist. Binding disables the text box; saving the connection stores the reference.

From then on nothing in NetRisk holds that credential. Rotating it in the vault takes effect within
the cache TTL, with no change in NetRisk at all.

---

## The two credentials

The connection carries **one API key** and, optionally, **one machine ID**.

For BastionVault the API key is a **vault token** — from `bvault token create`, or from
`bvault ferrogate token` on an attested host when the server requires machine identity.

The machine ID is the FerroGate identity (a SPIFFE ID) of the host NetRisk runs on, and it is
**optional**: BastionVault only refuses non-machine-bound sessions when an administrator has turned
`require_machine_identity` on, and most have not. Requiring it here would make the plugin unusable
for every server that has not. Instead the connection test asks the server what it demands and checks
the token against it — see *Machine identity is checked, not sent* below. A plugin whose vault
*always* requires one declares `RequiresMachineId => true`, and NetRisk then refuses the connection
until one is entered instead of sending a request it knows will fail.

The machine ID is stored and returned **in the clear**. It identifies the installation rather than
authenticating it, it is useless without the API key, and an operator has to be able to read it back
to compare it against what the vault shows.

---

## How a reference is stored

There is no `vault_secret_id` column beside every credential column, and no join table. A reference
lives in the credential column itself, marked:

```
vault:v1:{connectionId}:{base64url(secretId)}[:{base64url(field)}]
```

([`SecretReference`](../../src/Model/Secrets/SecretReference.cs))

The trade this makes:

- **Every credential field in the product gains vault support at once**, including ones added next
  year, because the resolver sits on the *read path* rather than on the table.
- **A reference is stored unencrypted, deliberately.** It names a secret; it is not one. Encrypting it
  would make "which fields point at vault connection 3" impossible to query — and that query is what
  lets NetRisk refuse to delete a connection that is still in use.
- **The identifiers are base64url-encoded** because a vault secret id is arbitrary text.
  BastionVault allows `/` and `:` in a path, and a delimiter that can appear inside a value is a
  parser that breaks on somebody's real data.
- **What it costs:** the reference is not a foreign key, so deleting a connection cannot cascade.
  `SecretVaultService.DeleteConnectionAsync` refuses the delete while references exist and reports
  the count, rather than pretending the database can see it.

The credential-bearing columns are enumerated in one place —
`SecretVaultService.CountReferencesAsync` — and **that is the list to extend** when a new column
gains vault support.

---

## How a reference becomes a credential

```
integration service → ISecretResolver.ResolveAsync(stored)
                          │
        ┌─────────────────┴──────────────────┐
   not a reference                      a reference
        │                                    │
  ISecretProtector.Unprotect      ISecretVaultService.ResolveAsync
                                             │
                              ┌──────────────┴──────────────┐
                        cache hit                      cache miss
                             │                              │
                   IObfuscatedSecretCache        plugin.GetSecretAsync
                                                  (over IPluginHttpClient)
```

[`ISecretResolver`](../../src/ServerServices/Interfaces/ISecretResolver.cs) replaced bare
`ISecretProtector.Unprotect` at every credential *consumption* site on the server — Vision One,
SecurityScorecard, the issue trackers, Jira Service Management and Assets, the identity providers and
the notification dispatcher. That substitution is the point: after this feature exists a credential
column holds one of two things, and exactly one place should have to know that.

**A failure is never an empty credential.** `SecretVaultResolutionException` names the vault and the
reason — connection deleted, connection disabled, plugin missing, plugin disabled, key revoked, secret
gone, field renamed. Returning null instead would hand `""` to a third-party API, and the 401 that
comes back names NetRisk's request rather than the vault.

### The cache

Resolved values are held by
[`ObfuscatedSecretCache`](../../src/ServerServices/Security/ObfuscatedSecretCache.cs) for **15 minutes
by default** (1–60, per connection).

Why it exists: a Vision One sync makes dozens of HTTP calls and each one asks for the API key. Without
a cache that is dozens of vault round trips per sync.

What "obfuscated" means, precisely: each entry is AES-256-GCM ciphertext under a key generated from
the CSPRNG **at process start and never persisted**. Anyone who can read this process's memory can
still recover the plaintext — a debugger, a core dump, an attacker with code execution — so this is
not a boundary. What it buys is that a process dump, a crash report or a heap snapshot no longer
contains the estate's credentials as scannable strings, which is how credentials most often escape in
practice. The per-process key also means a restart empties the cache by construction, which is why
the cache is registered as a **singleton**: a transient one would encrypt every entry under a key
discarded before anything could read it.

What actually bounds the exposure is the TTL, and it is **absolute, not sliding**. A sliding window on
a credential a busy sync job touches every few seconds never expires — which quietly turns a
15-minute cache into a permanent second copy, and a revoked credential into one NetRisk keeps using.
Rotating a connection's API key, or changing its base URL or machine ID, evicts everything that
connection fetched.

---

## What the client can and cannot do

**There is no endpoint anywhere that returns a secret value.** Values are resolved on the server, at
the moment an integration needs one, and never travel to a client.

So an administrator session — or a stolen administrator token — buys the ability to see *which secrets
exist* and to *re-point a NetRisk field at one*. Re-pointing is a real capability, which is why the
whole surface is behind the `configuration` permission and why listing is logged at Information
(enumerating an estate's secret names is a reasonable administrative act and also what
reconnaissance looks like). Reading a credential out of NetRisk is not a capability the API has at
all, and `SecretVaultsControllerTest.NoActionReturnsASecretValue` is what keeps it that way.

---

## Writing a secret-vault plugin

The capability contract is
[`INetriskSecretVaultPlugin`](../../libs/netrisk-plugin-sdk/Contracts/Secrets/INetriskSecretVaultPlugin.cs)
in the SDK submodule. Three operations, and what is *absent* matters as much as what is present:

| Operation | Purpose |
|---|---|
| `TestConnectionAsync` | Does this key work, and what can it see. Reports failure as a value, not an exception. |
| `ListSecretsAsync` | Metadata for everything the key can reach — names, paths, field names. **No values.** |
| `GetSecretAsync` | One value, one reference, at the moment of use. |

No "write secret" — NetRisk is a consumer, and a plugin that could write would make a compromised
NetRisk able to rewrite the estate's credentials. No "list all values" — that is an exfiltration
primitive with a friendly name. No caching in the plugin: the host caches, with a TTL it controls.

Three rules an implementation must follow:

1. **Be stateless.** The host creates an instance per lookup and passes everything through
   `SecretVaultContext`.
2. **Use `context.Http`, never your own `HttpClient`.** That is the only path subject to the host's
   SSRF policy (`OutboundUrlPolicy`) and timeouts, and the only one a test can fake. An operator can
   paste any base URL into a connection; going through the host is what stops that becoming a request
   to the cloud metadata endpoint.
3. **Never log or return a credential.** `SecretVaultTestResult.Message` and
   `SecretVaultException.Message` both reach an operator's screen and NetRisk's log.

**Do not copy `Contracts.dll` beside the plugin.** Reference it with `Private="false"` and
`ExcludeAssets="runtime"`, as
[`FixtureVaultPlugin.csproj`](../../src/Plugins/FixtureVaultPlugin/FixtureVaultPlugin.csproj) does.
The host lists the SDK interfaces in `PluginLoader`'s `sharedTypes`, so its copy must be the one that
loads; a second copy makes the plugin's `INetriskSecretVaultPlugin` a *different type* from the
host's, `IsAssignableFrom` returns false, and the plugin is **silently ignored** — no exception, no
log line, no plugin. `SecretVaultPluginLoadingTest` loads a real built plugin
(`src/Plugins/FixtureVaultPlugin` — a vault plugin with no vault, kept in the tree purely as test
scaffolding) off disk specifically to prove this has not regressed.

The assembly name must end in `Plugin.dll` (that is what discovery matches) and the file goes in a
subdirectory of the host's `Plugins` folder — `Plugins/Secrets/` by convention for this capability.

### The BastionVault wire protocol

BastionVault is **HashiCorp-Vault-compatible**. Everything its surface dictates is in one file,
[`BastionVaultApi.cs`](https://github.com/ffquintella/netrisk-plugin-bastionvault-integration/blob/main/src/BastionVaultPlugin/BastionVaultApi.cs):

| Purpose | Request | Response |
|---|---|---|
| Validate the token | `GET /v1/auth/token/lookup-self` | `{"data":{"policies":[…],"meta":{…}}}` |
| Machine-identity policy | `GET /v1/auth/ferrogate/requirement` (unauthenticated) | `{"data":{"require_machine_identity":bool,…}}` |
| Mount table | `GET /v1/sys/mounts` | `{"data":{"secret/":{"type":"kv"},…}}` |
| List one level | `LIST /v1/{mount}{path}` | `{"data":{"keys":["db","prod/"]}}` |
| Read a secret | `GET /v1/{mount}{path}` | `{"data":{"username":…,"password":…},"lease_duration":n}` |

with `X-Vault-Token: {token}` on every authenticated request. Failures are
`{"errors":["…"]}`; `503` means the vault is **sealed**, which is the one failure whose remedy has
nothing to do with NetRisk's configuration.

Four details were taken from the server source rather than from `docs/api.md`, because they differ:

1. **Listing needs the `LIST` verb.** The documentation also offers `GET …?list=true`, but
   `bv-server/src/logical_routes.rs` maps `GET` to `Operation::Read` unconditionally and
   `bv-logical/src/util.rs` lifts only `env` and `version` out of the query string. `GET …?list=true`
   therefore **reads the secret** at that path instead of listing under it — silently, with a value
   in the response.
2. **A listing is one level deep, and folders carry a trailing `/`**
   (`bv-storage/src/physical/file/local.rs`). Enumerating an estate is a recursive walk, capped here
   at 2,000 secrets and 10 levels.
3. **Every secret is a map.** There is no single-value shape; a reference with no field resolves only
   when the map happens to hold exactly one entry.
4. **The lease is `lease_duration`, in seconds, at the envelope level** — not inside `data`. It feeds
   `MaxCacheAge`, so a vault asking for a *shorter* life than the connection's TTL gets it (the vault
   wins when it is stricter and loses when it is laxer).

Two engine types are listed (`kv`, `generic`) and two mounts are deliberately skipped: `pki` holds
certificates rather than referencable secrets, and `cubbyhole` is per-token private storage, so a
reference into it could never resolve again. A `403` on `sys/mounts` falls back to the conventional
`secret/` mount, because reading the mount table is a `sys/` privilege a careful operator will not
grant NetRisk; a `403` on an individual folder is skipped rather than failing the walk, because a
token scoped to just the paths NetRisk needs is the *right* configuration and will be denied on its
siblings.

**If your deployment differs, that one file is the only thing to change**, and the plugin's own
test project pins the mapping — including two assertions written specifically to stop a regression to
a bearer token or a `?list=true` listing.

### Machine identity is checked, not sent

FerroGate machine authentication is a DPoP-bound attestation flow against `auth/ferrogate/login`
that requires a local **Machine Identity Agent (MIA)**; there is no header a plugin can add to make a
request machine-bound. A headless application obtains a machine-bound token on the host and then
presents it like any other token:

```bash
bvault ferrogate token --field client_token --audience https://vault.example.com
```

So the machine ID on a NetRisk connection is not something the plugin *sends* — it is what the
connection test **verifies the supplied token is actually bound to**. Two things are checked, because
they have different remedies:

- The token's own `meta.spiffe_id` (a *reserved* token metadata key, so `auth/token/create` refuses a
  request that supplies one — which is what makes its presence trustworthy evidence of attestation)
  must match the machine ID the connection declares, when it declares one. Accepting a token issued
  for a different machine would make the field decorative.
- If the server has `require_machine_identity` on and the token is **not** machine-bound, the test
  fails with the `bvault ferrogate token` command to run — because the server will refuse every
  subsequent request, and discovering that at 3am is the outcome this check exists to prevent.

A `404` on the requirement endpoint is the ordinary answer on a server with no FerroGate auth method
mounted and is not treated as a refusal.

---

## Key files

| Layer | File |
|---|---|
| SDK contract | [`INetriskSecretVaultPlugin.cs`](../../libs/netrisk-plugin-sdk/Contracts/Secrets/INetriskSecretVaultPlugin.cs), [`SecretVaultTypes.cs`](../../libs/netrisk-plugin-sdk/Contracts/Secrets/SecretVaultTypes.cs), [`PluginHttp.cs`](../../libs/netrisk-plugin-sdk/Contracts/Secrets/PluginHttp.cs) |
| Plugin | Its own repository: [netrisk-plugin-bastionvault-integration](https://github.com/ffquintella/netrisk-plugin-bastionvault-integration) (`BastionVaultSecretPlugin.cs`, `BastionVaultApi.cs`) |
| Reference format | [`SecretReference.cs`](../../src/Model/Secrets/SecretReference.cs) |
| Wire contracts | [`SecretVaultContracts.cs`](../../src/Model/Secrets/SecretVaultContracts.cs) |
| Entity / schema | [`SecretVaultConnection.cs`](../../src/DAL/Entities/SecretVaultConnection.cs), [`NRDbContext.Secrets.cs`](../../src/DAL/Context/NRDbContext.Secrets.cs), `DB/Structure/84.sql` + `DB/Data/84.sql` |
| Server | [`SecretVaultService.cs`](../../src/ServerServices/Secrets/SecretVaultService.cs), [`SecretResolver.cs`](../../src/ServerServices/Secrets/SecretResolver.cs), [`ObfuscatedSecretCache.cs`](../../src/ServerServices/Security/ObfuscatedSecretCache.cs), [`PluginHttpClientAdapter.cs`](../../src/ServerServices/Secrets/PluginHttpClientAdapter.cs) |
| API | [`SecretVaultsController.cs`](../../src/API/Controllers/SecretVaultsController.cs) |
| Client | `IIntegrationsService` / `IntegrationsRestService` (the `…SecretVault…` members) |
| Desktop | [`VaultSecretFieldState.cs`](../../src/GUIClient/Tools/VaultSecretFieldState.cs), [`SecretVaultPickerViewModel.cs`](../../src/GUIClient/ViewModels/Dialogs/SecretVaultPickerViewModel.cs), `SecretVaultPickerDialog.axaml`, the Secret Vaults tab of `IntegrationsView.axaml` |
| Test scaffolding | [`FixtureVaultPlugin`](../../src/Plugins/FixtureVaultPlugin) — a vault plugin with no vault, so the loader can be tested against a real assembly |

## API

All actions are `[PermissionAuthorize("configuration")]`.

| Verb | Route | Purpose |
|---|---|---|
| GET | `/SecretVaults/available` | Is the feature usable (enabled plugin + enabled connection) |
| GET | `/SecretVaults/plugins` | Installed and enabled secret-vault plugins |
| GET | `/SecretVaults` | Connections (no API keys) |
| GET | `/SecretVaults/{id}` | One connection |
| POST | `/SecretVaults` | Create — API key required |
| PUT | `/SecretVaults/{id}` | Update — null API key leaves the stored one alone |
| DELETE | `/SecretVaults/{id}` | Delete — 400 while references exist |
| POST | `/SecretVaults/{id}/test` | Test; a failure is a 200 carrying the reason |
| GET | `/SecretVaults/{id}/secrets` | Secret metadata for the picker |
| POST | `/SecretVaults/describe` | What a stored reference points at (POST, so a reference stays out of access logs) |
| GET | `/SecretVaults/{id}/usage` | How many fields resolve through this connection |

## Tests

| Project | File | Covers |
|---|---|---|
| `ServerServices.Tests` | `Secrets/SecretReferenceTest.cs` | The stored format, including secret ids containing the delimiters |
| | `Secrets/ObfuscatedSecretCacheTest.cs` | Absolute expiry, prefix eviction, plaintext not in memory |
| | `Secrets/SecretVaultServiceInMemoryTest.cs` | Connections, testing, listing, resolution, cache invalidation, delete guard |
| | `Secrets/SecretResolverTest.cs` | The literal/reference branch, and refusing a malformed reference |
| | `Secrets/SecretVaultPluginLoadingTest.cs` | A **real** plugin assembly (`FixtureVaultPlugin`) loaded off disk across the load-context boundary |
| | `Secrets/SecretVaultRegistrationTest.cs` | The DI graph composes in every host |
| | `Secrets/PluginCapabilityDiscoveryTest.cs` | A host with no plugins answers "no" rather than throwing |
| | `Track4/SecretProtectorTest.cs` | A reference is stored in the clear and warns about nothing |
| `API.Tests` | `APITests/SecretVaultsControllerTest.cs` | The HTTP contract, and that **no** action returns a value |
| `ClientServices.Tests` | `Services/SecretVaultRestServiceTest.cs` | Every route and status branch; no way to read a value |
| `GUIClient.Tests` | `Tools/VaultSecretFieldStateTest.cs` | The typed / picked / bound decision |
| | `Views/IntegrationsVaultBindingTests.cs` | The XAML `CommandParameter` keys match the view-model's |

## Not covered

- **Notification channel secrets are write-only in the desktop client.** A channel's webhook URL and
  signing secret live inside its configuration JSON and the server does not report which of them are
  vault references, so the picker can bind them but the form cannot show an existing binding. The
  server resolves them either way.
- **The backup passphrase** (`settings.backup_password`) is not yet vault-backed. The resolver would
  handle it; the read path in `SettingsService` has not been moved over.

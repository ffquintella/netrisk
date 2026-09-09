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

1. **Install the plugin.** `BastionVaultPlugin.dll` goes in `Plugins/Secrets/` under the API (and
   under the background-job host, which also resolves credentials). The in-tree plugin is installed
   there automatically by
   [`InstallSecretVaultPlugins.targets`](../../src/Plugins/InstallSecretVaultPlugins.targets).
2. **Enable it** under *Admin → Plugins*. Installing is not enabling: a DLL in the directory
   activates nothing on its own.
3. **Add a vault connection** under *Admin → Integrations → Secret Vaults*: a name, the plugin, the
   vault's base URL, the API key, and optionally the machine ID (see below). Press *Test* — it
   reports how many secrets that key can see, which is the difference between "authenticated" and
   "authenticated and useful".
4. **Bind a credential field.** Every secret box on the Integrations screen grows a key icon beside
   it. Pressing it lists the secrets the connection can see; choosing one binds the field and
   disables the text box. Saving the connection stores the reference.

From then on nothing in NetRisk holds that credential. Rotating it in the vault takes effect within
the cache TTL, with no change in NetRisk at all.

---

## The two credentials

The connection carries **one API key** and, optionally, **one machine ID**.

The machine ID is the identity BastionVault issues for the host NetRisk is installed on, and it is
**optional** — BastionVault only binds a key to a machine when the account is configured that way, so
requiring it would make the plugin unusable for everyone else. When a machine-bound account is reached
without one the vault answers 401, and the connection test says so explicitly rather than leaving the
operator to suspect the API key. A plugin whose vault *always* requires it declares
`RequiresMachineId => true`, and NetRisk then refuses to use the connection until one is entered
instead of sending a request it knows will fail.

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
[`BastionVaultPlugin.csproj`](../../src/Plugins/BastionVaultPlugin/BastionVaultPlugin.csproj) does.
The host lists the SDK interfaces in `PluginLoader`'s `sharedTypes`, so its copy must be the one that
loads; a second copy makes the plugin's `INetriskSecretVaultPlugin` a *different type* from the
host's, `IsAssignableFrom` returns false, and the plugin is **silently ignored** — no exception, no
log line, no plugin. `SecretVaultPluginLoadingTest` loads the real built plugin off disk specifically
to prove this has not regressed.

The assembly name must end in `Plugin.dll` (that is what discovery matches) and the file goes in a
subdirectory of the host's `Plugins` folder — `Plugins/Secrets/` by convention for this capability.

### The BastionVault wire protocol

Everything BastionVault's REST surface dictates is in one file,
[`BastionVaultApi.cs`](../../src/Plugins/BastionVaultPlugin/BastionVaultApi.cs):

```
GET  {base}/api/v1/secrets       -> { "secrets": [ { id, name, path, description,
                                                     fields: [..], version, updatedAt } ] }
GET  {base}/api/v1/secrets/{id}  -> { id, version, value, fields: { name: value }, maxCacheSeconds }
```

with `Authorization: Bearer {apiKey}` on every request and `X-BastionVault-Machine-Id: {machineId}`
added when the connection carries one. A bare JSON array is accepted in place of the `secrets`
envelope. `maxCacheSeconds` lets the vault ask for a *shorter* cache life than the connection's — the
vault wins when it is stricter and loses when it is laxer.

**If your BastionVault deployment's paths, header name or payload fields differ, that one file is the
only thing to change**, and `BastionVaultPlugin.Tests` pins the mapping so the change is visible.

---

## Key files

| Layer | File |
|---|---|
| SDK contract | [`INetriskSecretVaultPlugin.cs`](../../libs/netrisk-plugin-sdk/Contracts/Secrets/INetriskSecretVaultPlugin.cs), [`SecretVaultTypes.cs`](../../libs/netrisk-plugin-sdk/Contracts/Secrets/SecretVaultTypes.cs), [`PluginHttp.cs`](../../libs/netrisk-plugin-sdk/Contracts/Secrets/PluginHttp.cs) |
| Plugin | [`BastionVaultSecretPlugin.cs`](../../src/Plugins/BastionVaultPlugin/BastionVaultSecretPlugin.cs), [`BastionVaultApi.cs`](../../src/Plugins/BastionVaultPlugin/BastionVaultApi.cs) |
| Reference format | [`SecretReference.cs`](../../src/Model/Secrets/SecretReference.cs) |
| Wire contracts | [`SecretVaultContracts.cs`](../../src/Model/Secrets/SecretVaultContracts.cs) |
| Entity / schema | [`SecretVaultConnection.cs`](../../src/DAL/Entities/SecretVaultConnection.cs), [`NRDbContext.Secrets.cs`](../../src/DAL/Context/NRDbContext.Secrets.cs), `DB/Structure/84.sql` + `DB/Data/84.sql` |
| Server | [`SecretVaultService.cs`](../../src/ServerServices/Secrets/SecretVaultService.cs), [`SecretResolver.cs`](../../src/ServerServices/Secrets/SecretResolver.cs), [`ObfuscatedSecretCache.cs`](../../src/ServerServices/Security/ObfuscatedSecretCache.cs), [`PluginHttpClientAdapter.cs`](../../src/ServerServices/Secrets/PluginHttpClientAdapter.cs) |
| API | [`SecretVaultsController.cs`](../../src/API/Controllers/SecretVaultsController.cs) |
| Client | `IIntegrationsService` / `IntegrationsRestService` (the `…SecretVault…` members) |
| Desktop | [`VaultSecretFieldState.cs`](../../src/GUIClient/Tools/VaultSecretFieldState.cs), [`SecretVaultPickerViewModel.cs`](../../src/GUIClient/ViewModels/Dialogs/SecretVaultPickerViewModel.cs), `SecretVaultPickerDialog.axaml`, the Secret Vaults tab of `IntegrationsView.axaml` |
| Deployment | [`InstallSecretVaultPlugins.targets`](../../src/Plugins/InstallSecretVaultPlugins.targets) |

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
| | `Secrets/SecretVaultPluginLoadingTest.cs` | The **real** plugin loaded off disk across the load-context boundary |
| | `Secrets/SecretVaultRegistrationTest.cs` | The DI graph composes in every host |
| | `Secrets/PluginCapabilityDiscoveryTest.cs` | A host with no plugins answers "no" rather than throwing |
| | `Track4/SecretProtectorTest.cs` | A reference is stored in the clear and warns about nothing |
| `API.Tests` | `APITests/SecretVaultsControllerTest.cs` | The HTTP contract, and that **no** action returns a value |
| `ClientServices.Tests` | `Services/SecretVaultRestServiceTest.cs` | Every route and status branch; no way to read a value |
| `GUIClient.Tests` | `Tools/VaultSecretFieldStateTest.cs` | The typed / picked / bound decision |
| | `Views/IntegrationsVaultBindingTests.cs` | The XAML `CommandParameter` keys match the view-model's |
| `BastionVaultPlugin.Tests` | `BastionVaultSecretPluginTest.cs` | The wire protocol, and that no message echoes the API key |

## Not covered

- **Notification channel secrets are write-only in the desktop client.** A channel's webhook URL and
  signing secret live inside its configuration JSON and the server does not report which of them are
  vault references, so the picker can bind them but the form cannot show an existing binding. The
  server resolves them either way.
- **The backup passphrase** (`settings.backup_password`) is not yet vault-backed. The resolver would
  handle it; the read path in `SettingsService` has not been moved over.

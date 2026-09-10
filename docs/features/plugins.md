# Plugins

Extensibility layer built on the external [`netrisk-plugin-sdk`](../../libs/netrisk-plugin-sdk) submodule. Plugins are DLLs loaded at runtime and can be toggled on/off per-instance. Known built-in integrations include FaceID biometric auth and the Nessus vulnerability importer.

## Key Model Classes

- [PluginInfo.cs](../../src/Model/Plugins/PluginInfo.cs) — metadata
- [PluginDll.cs](../../src/Model/Plugins/PluginDll.cs) — DLL descriptor
- `INetriskPlugin` — the contract (from the SDK submodule)

## Server Service

[`IPluginsService`](../../src/ServerServices/Interfaces/IPluginsService.cs):

- `LoadPluginsAsync` — discover and load DLLs
- `IsInitialized`, `PluginExistsAsync`, `PluginIsEnabledAsync`
- `GetInfoAsync`, `GetPluginsAsync`, `GetPluginAsync<T>` (generic typed retrieval)
- `GetPluginByNameAsync<T>` — matches the plugin's own name as well as the capability, and returns
  null rather than throwing when it is absent. Prefer it over `GetPluginAsync<T>`, which checks that
  *some* plugin with the requested name exists and then returns the first instance assignable to `T`
  from any loader — on an installation with two plugins of one capability, that is the wrong one.
- `GetEnabledPluginsAsync<T>` — every switched-on plugin of a capability
- `SetPluginEnabledStatusAsync`

## API

[`PluginsController`](../../src/API/Controllers/PluginsController.cs):

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/plugins` | List plugins |
| GET | `/plugins/Info` | Metadata |
| GET | `/plugins/{pluginName}/Enabled` | Enabled state |
| POST | `/plugins/{pluginName}/Enabled` | Toggle enabled |
| POST | `/plugins/upload` | Install a plugin package (admin only) — see below |

## Installing a plugin package

Administration → Plugins has an upload button that sends a `.zip` to `POST /plugins/upload`. The
server unpacks it into `Plugins/<package>/` beside the API binary and reloads, so no redeploy and no
shell access to the host is needed. The package directory name comes from the uploaded file name,
sanitised to one safe path segment.

`ServerServices/Plugins/PluginPackageInstaller` decides everything from the archive's **table of
contents, before a byte is written** — so a rejection never has to clean up after itself, and the
rules are testable without a filesystem:

| Refused | Why |
|---|---|
| An entry that resolves outside the destination, or an absolute path | Zip slip |
| More than 400 MB expanded, more than 5000 entries, more than 100 MB uploaded | Zip bomb / resource bound |
| No `*Plugin.dll` at the package's top level | `GetPluginsDlls` globs `Plugins/<dir>/*Plugin.dll` only, so it would install and never load |
| A `*Plugin.dll` one folder deeper than the top level | Same reason — the archive's single wrapping folder *is* stripped, a second one is not |
| A package shipping `Contracts.dll` | A second copy of the shared interfaces makes `IsAssignableFrom` false and the host ignores the plugin with no error (see the capability section above) |

A package that replaces an existing installation of the same name is staged: the old directory is
moved aside first and restored if extraction dies part-way, so an interrupted install cannot leave
half of one version beside half of another. Every rejection comes back as a `PluginInstallResult`
with `Success = false` and a sentence the desktop client shows verbatim — the server's message names
what to change, and a house error string would leave the operator with nothing to act on.

**This is not a new trust boundary.** A loaded plugin runs in the API process with the API's full
authority, so uploading one is running code on the server — exactly as privileged as copying a DLL
into the directory by hand, which is what this replaces. The endpoint is administrator-only, an
installed plugin arrives **disabled**, and the `plugins_require_signature` policy still applies at
load time.

## Client

[`PluginsRestService`](../../src/ClientServices/Services/PluginsRestService.cs).
`UploadPluginAsync` sends the archive as multipart. It deliberately uses the *error-reporting*
client and `ExecuteAsync`: the default client sets RestSharp's `ThrowOnAnyError`, which raises
before the caller sees the response and carries only "Request failed with status code 400" — which
would throw away the entire refusal message.

## Capability interfaces

A plugin implements `INetriskPlugin` plus one capability interface per thing it does, and the host
discovers it by that interface:

| Capability | Interface | Host use |
|---|---|---|
| Model extension | `INetriskModelPlugin` | Domain model extension points |
| Face recognition | `INetriskFaceIDPlugin` | Biometric enrolment and verification |
| Vulnerability classification | `INetriskVulnerabilityClassificationPlugin` | Severity overrides |
| Report import | `Contracts.Importers.INetriskVulnerabilityImporterPlugin` | Ingesting scanner reports |
| Deduplication | `Contracts.Importers.IDeduplicationStrategyPlugin` | Finding identity strategies |
| Secret vault | `Contracts.Secrets.INetriskSecretVaultPlugin` | Resolving a stored secret *reference* to a live credential — see [External secret vaults](secret-vaults.md) |

Adding a capability means adding its interface to the SDK submodule and listing it in
`PluginsService.LoadPluginsAsync`'s `sharedTypes`. **Do not ship `Contracts.dll` beside a plugin**: the
loader would give it a second copy of the shared interfaces, the plugin's interface would stop being
the host's, `IsAssignableFrom` would return false, and the plugin would be silently ignored. Reference
Contracts with `Private="false"` and `ExcludeAssets="runtime"`, as
[`FixtureVaultPlugin.csproj`](../../src/Plugins/FixtureVaultPlugin/FixtureVaultPlugin.csproj) does.

Plugins themselves live outside this repository — the BastionVault vault plugin is
[netrisk-plugin-bastionvault-integration](https://github.com/ffquintella/netrisk-plugin-bastionvault-integration).
`src/Plugins/FixtureVaultPlugin` is the one exception, and it is test scaffolding rather than a
feature: a vault plugin with no vault behind it, so the loader can be tested against a real
assembly.

## Capabilities

- Runtime DLL discovery and load
- Package upload and install from the administration screen, without shell access to the API host
- Per-plugin enable/disable without redeploy
- Generic typed plugin resolution (`GetPluginAsync<T>`)
- Consumers guard calls with `IsFaceIDPluginEnabled`-style checks and throw `PluginDisabledException` when a dependent feature is disabled
- SDK contract lives in the [`netrisk-plugin-sdk`](../../libs/netrisk-plugin-sdk) submodule

## Tests

- `API.Tests/APITests/PluginsControllerTest.cs` — the HTTP contract
- `ServerServices.Tests/Secrets/PluginCapabilityDiscoveryTest.cs` — a host with no plugins answers
  "no" rather than throwing, including when the `Plugins` directory does not exist
- `ServerServices.Tests/Secrets/SecretVaultPluginLoadingTest.cs` — `FixtureVaultPlugin` loaded off
  disk, which is what proves the shared-assembly arrangement above still holds
- `ServerServices.Tests/Plugins/PluginPackageInstallerTest.cs` — package validation and extraction,
  weighted towards the rejections: zip slip, absolute paths, zip bomb, entry count, and each of the
  silent-failure cases in the table above. Extraction is checked separately against a *forged*
  validation, because the write path defends itself rather than trusting the validator
- `ClientServices.Tests/Services/PluginsRestServiceTest.cs` — including that a 400 refusal reaches
  the caller with its message, and that a 401 discards the token on a client that does not throw

## Common Exceptions

`PluginDisabledException`, `DataNotFoundException`

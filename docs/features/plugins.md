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

## Client

[`PluginsRestService`](../../src/ClientServices/Services/PluginsRestService.cs).

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
[`BastionVaultPlugin.csproj`](../../src/Plugins/BastionVaultPlugin/BastionVaultPlugin.csproj) does.

## Capabilities

- Runtime DLL discovery and load
- Per-plugin enable/disable without redeploy
- Generic typed plugin resolution (`GetPluginAsync<T>`)
- Consumers guard calls with `IsFaceIDPluginEnabled`-style checks and throw `PluginDisabledException` when a dependent feature is disabled
- SDK contract lives in the [`netrisk-plugin-sdk`](../../libs/netrisk-plugin-sdk) submodule

## Tests

- `API.Tests/APITests/PluginsControllerTest.cs` — the HTTP contract
- `ServerServices.Tests/Secrets/PluginCapabilityDiscoveryTest.cs` — a host with no plugins answers
  "no" rather than throwing, including when the `Plugins` directory does not exist
- `ServerServices.Tests/Secrets/SecretVaultPluginLoadingTest.cs` — the real BastionVault plugin loaded
  off disk, which is what proves the shared-assembly arrangement above still holds

## Common Exceptions

`PluginDisabledException`, `DataNotFoundException`

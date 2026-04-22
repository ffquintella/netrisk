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

## Capabilities

- Runtime DLL discovery and load
- Per-plugin enable/disable without redeploy
- Generic typed plugin resolution (`GetPluginAsync<T>`)
- Consumers guard calls with `IsFaceIDPluginEnabled`-style checks and throw `PluginDisabledException` when a dependent feature is disabled
- SDK contract lives in the [`netrisk-plugin-sdk`](../../libs/netrisk-plugin-sdk) submodule

## Tests

No dedicated test file in the current suite.

## Common Exceptions

`PluginDisabledException`, `DataNotFoundException`

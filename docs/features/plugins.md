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
| DELETE | `/plugins/{pluginName}` | Remove an installed plugin (admin only) — see below |

## Installing a plugin package

Administration → Plugins has an upload button that sends a `.zip` to `POST /plugins/upload`. The
server unpacks it into `Plugins/<package>/` beside the API binary and reloads, so no redeploy and no
shell access to the host is needed. Set `NETRISK_PLUGINS_PATH` to put that root somewhere else — a
mounted volume in a container — and leave it unset for the default.

**The package directory is named after the plugin assembly, not the uploaded file.** A package
carrying `BastionVaultPlugin.dll` installs into `Plugins/BastionVaultPlugin/` whether it arrived as
`BastionVaultPlugin-1.2.0.zip` or `BastionVaultPlugin-1.2.1.zip`, so a new release lands *on* the
previous one. Naming it after the file is what let one plugin be installed twice: release packages
are named for their version, each landed in its own directory, `GetPluginsDlls` globbed both, and
administration listed the same plugin at two versions with two independent enabled switches — with
no defined answer to which of them a capability lookup resolved. An installation that already
accumulated those directories is cleaned up the next time the plugin is installed: every other
directory carrying the same plugin assembly is removed, and reported in
`PluginInstallResult.RemovedDirectories`. A package with two different `*Plugin.dll` files at its top
level has no single identity, so it falls back to the file-derived name.

The list itself collapses a duplicate as a second line of defence, since a plugin directory can also
arrive by hand: `PluginListing.CollapseVersions` shows one row per plugin name, the highest version,
and logs a warning naming the directories.

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

## Removing a plugin

Administration → Plugins has a delete button per row, which sends `DELETE /plugins/{pluginName}`.
The server switches the plugin off, deletes **every** directory whose assemblies provide it, and
reloads.

The order is deliberate: disabling comes first, so a delete that cannot finish can never leave an
enabled plugin the operator believes is gone. And it cannot always finish — **a loaded plugin
assembly stays locked by the host process**, and nothing in .NET reliably unloads one while
instances may still be referenced. When the files cannot be deleted the directory gets a
`.netrisk-uninstalled` marker; the loader skips a marked directory and sweeps what it can on every
load pass, deleting the marker and the directory itself only once nothing else is left inside. The
response says which happened through `PluginUninstallResult.RemovalPending`, and the desktop client
shows it: either "the plugin was removed", or "its files are still in use and will be deleted when
the server next restarts". Either way the plugin is disabled and off the list immediately.

The sweep must never delete the marker before the assembly — that was a bug during development, and
its symptom is the deleted plugin reappearing on the load pass after the next one.

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
- Install of a new version over the installed one, rather than beside it
- Delete from the administration screen, including a duplicate installation
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
- `ServerServices.Tests/Plugins/PluginInstallationLifecycleTest.cs` — the real service against a real
  plugin on disk, in its own plugins root: a duplicate installation is listed once, a release package
  installs under the plugin name, a second release replaces the first, installing clears the
  directories left by the old naming, a delete removes every directory providing the plugin and
  disables it, and a deleted plugin does not come back on the next reload
- `ServerServices.Tests/Plugins/PluginListingTest.cs` — one row per plugin name at the highest
  version, with the version ordering numeric (1.2.10 is newer than 1.2.9) and plugin names compared
  ordinally, because `Plugin_<name>_Enabled` is read with the plugin's own spelling
- `ClientServices.Tests/Services/PluginsRestServiceTest.cs` — including that a 400 refusal reaches
  the caller with its message, that `RemovalPending` survives the round trip, and that a 401 discards
  the token on a client that does not throw

## Common Exceptions

`PluginDisabledException`, `DataNotFoundException`

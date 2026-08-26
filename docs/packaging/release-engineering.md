# Release engineering: signing and native installers

This is the operational guide for whoever cuts a NetRisk desktop release. It covers what the
build produces, which credentials each signed artifact needs, where those credentials come
from, and what a CI runner must have installed.

Everything here is driven by the Nuke build (`build/Build.cs` plus the
`build/Build.Signing.cs` and `build/Build.Installers.cs` partials). The manifests live under
[`build/installers/`](../../build/installers) and are rendered at build time from reviewed
templates; the pure logic behind them (version normalisation, signing decisions, template
rendering, secret redaction) lives in `build/NetRisk.Packaging` and is covered by
`src/Packaging.Tests`.

> **Nothing in this repository contains a credential, and no ordinary build needs one.** When
> signing material is absent the packaging targets emit one warning line, produce the unsigned
> artifact, and succeed. That is the expected outcome for a developer machine and for a CI
> fork. Pass `--require-signing` (or `--require-notarization`) to turn a missing credential
> into a build failure — that is what a release pipeline should do.

---

## 1. Artifacts

| Platform | Target | Artifact | Signed with |
|---|---|---|---|
| Windows | `PackageWindowsGUI` | `NetRisk-Setup-<version>.exe` (Inno Setup, interactive) | Authenticode |
| Windows | `PackageWindowsMSI` | `NetRisk-<version>-x64.msi` (per-machine, silent) | Authenticode |
| Windows | `PackageWindowsMSIX` | `NetRisk-<version>-x64.msix` + `NetRisk.appinstaller` | Authenticode (**mandatory**) |
| macOS | `PackageMacGUI` / `PackageMacA64GUI` | `NetRisk.app`, `.pkg`, drag-and-drop `.dmg` | Developer ID + notarization |
| Linux | `PackageLinuxGUI` | `GUIClient-Linux-x64-<version>.zip` | — |
| Linux | `PackageLinuxFlatpak` | `app.netrisk.NetRisk-<version>.flatpak` | Flatpak repo GPG (see §6) |
| Linux | `PackageLinuxSnap` | `netrisk_<version>_amd64.snap` | Snap Store (signed on upload) |

Convenience aggregates: `PackageWindowsInstallers`, `PackageLinuxInstallers`,
`PackageAllInstallers`. `VerifySignatures` re-checks whatever is already in `output/publish`.

Every artifact gets a `.sha256` companion file. The Windows `.exe`, the Linux `.zip` and the
macOS `.dmg` keep their historical checksum file names (extension replaced); the artifacts
added by this track append `.sha256` to the full file name, because `.msi` and `.msix` share a
base name and would otherwise overwrite each other's checksum.

All identifiers — the MSI `UpgradeCode`, the MSIX identity, the macOS bundle id, the Flatpak
app-id, the Snap name — are declared once in
[`PackageIdentity`](../../build/NetRisk.Packaging/PackageIdentity.cs). **Treat them as
append-only**: changing one turns an upgrade into a second parallel install.

---

## 2. Where credentials come from

Every value is a Nuke parameter with an environment-variable fallback. Use the environment in
CI; the parameter form is for a one-off local run.

| Parameter | Environment variable | Secret |
|---|---|---|
| `--require-signing` | `NETRISK_REQUIRE_SIGNING` | no |
| `--require-notarization` | `NETRISK_REQUIRE_NOTARIZATION` | no |
| `--windows-signing-mode` | `NETRISK_WINDOWS_SIGNING_MODE` | no |
| `--trusted-signing-endpoint` | `NETRISK_TRUSTED_SIGNING_ENDPOINT` | no |
| `--trusted-signing-account` | `NETRISK_TRUSTED_SIGNING_ACCOUNT` | no |
| `--trusted-signing-certificate-profile` | `NETRISK_TRUSTED_SIGNING_CERTIFICATE_PROFILE` | no |
| `--windows-certificate-thumbprint` | `NETRISK_WINDOWS_CERTIFICATE_THUMBPRINT` | no |
| `--windows-certificate-file` | `NETRISK_WINDOWS_CERTIFICATE_FILE` | no |
| `--windows-certificate-password` | `NETRISK_WINDOWS_CERTIFICATE_PASSWORD` | **yes** |
| `--windows-certificate-csp` | `NETRISK_WINDOWS_CERTIFICATE_CSP` | no |
| `--windows-certificate-key-container` | `NETRISK_WINDOWS_CERTIFICATE_KEY_CONTAINER` | no |
| `--timestamp-url` | `NETRISK_TIMESTAMP_URL` | no |
| `--timestamp-url-fallbacks` | `NETRISK_TIMESTAMP_URL_FALLBACKS` | no |
| `--mac-signing-identity` | `NETRISK_MAC_SIGNING_IDENTITY` | no |
| `--mac-installer-signing-identity` | `NETRISK_MAC_INSTALLER_SIGNING_IDENTITY` | no |
| `--mac-team-id` | `NETRISK_MAC_TEAM_ID` | no |
| `--mac-certificate-base64` | `NETRISK_MAC_CERTIFICATE_BASE64` | **yes** |
| `--mac-certificate-password` | `NETRISK_MAC_CERTIFICATE_PASSWORD` | **yes** |
| `--mac-notary-keychain-profile` | `NETRISK_MAC_NOTARY_KEYCHAIN_PROFILE` | no |
| `--mac-notary-api-key-id` | `NETRISK_MAC_NOTARY_API_KEY_ID` | no |
| `--mac-notary-api-issuer-id` | `NETRISK_MAC_NOTARY_API_ISSUER_ID` | no |
| `--mac-notary-api-key-path` | `NETRISK_MAC_NOTARY_API_KEY_PATH` | no (the file it points at is) |
| `--msix-publisher` | `NETRISK_MSIX_PUBLISHER` | no |
| `--app-installer-base-uri` | `NETRISK_APPINSTALLER_BASE_URI` | no |
| `--snap-grade` | `NETRISK_SNAP_GRADE` | no |
| `--branded-dmg` | `NETRISK_BRANDED_DMG` | no |

Secrets are declared `[Secret]` in the build so Nuke keeps them out of its own output, and
every command line that carries one is passed through `SecretRedactor` before it reaches a log.
Do not add a signing command that logs its raw arguments.

---

## 3. Windows Authenticode

### 3.1 Choosing a signing provider

Since June 2023 the CA/Browser Forum baseline requires code-signing private keys to live on a
FIPS 140-2 Level 2 (or Common Criteria EAL4+) hardware token or HSM. A `.pfx` you can copy
around no longer meets that bar for a publicly-trusted certificate, so the practical options
are cloud signing services. The build supports two shapes:

1. **Azure Trusted Signing** (recommended, and what `auto` prefers). Short-lived certificates
   issued per signature from a Microsoft-operated HSM; no certificate file exists anywhere.
2. **`signtool`** against an installed certificate (`/sha1 <thumbprint>`), a vendor CSP/KSP
   key container (`/csp` + `/kc` — this is how DigiCert KeyLocker and SSL.com eSigner expose a
   cloud HSM), or, for internal/test builds only, a `.pfx` plus password.

Do not buy an EV certificate expecting instant SmartScreen trust: Microsoft removed the EV
reputation bypass in 2024. Reputation now accrues from download volume regardless of
certificate class.

### 3.2 Azure Trusted Signing setup

1. Create a Trusted Signing account and a certificate profile in the Azure portal, in a region
   whose endpoint you note (for example `https://weu.codesigning.azure.net`).
2. Register an Entra application (or use a managed identity on the runner) and grant it the
   **Trusted Signing Certificate Profile Signer** role on the certificate profile.
3. Install the signing client on the runner: `dotnet tool install --global sign`.
4. Set on the runner:

   ```
   AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET   # consumed by DefaultAzureCredential
   NETRISK_TRUSTED_SIGNING_ENDPOINT=https://weu.codesigning.azure.net
   NETRISK_TRUSTED_SIGNING_ACCOUNT=netrisk
   NETRISK_TRUSTED_SIGNING_CERTIFICATE_PROFILE=netrisk-public
   NETRISK_REQUIRE_SIGNING=1
   ```

   The Azure credentials stay in the environment: the build never puts them on a command line.

### 3.3 signtool setup

```
NETRISK_WINDOWS_SIGNING_MODE=signtool
NETRISK_WINDOWS_CERTIFICATE_THUMBPRINT=<sha1 of the cert in the runner's store>
# or, for a vendor cloud HSM:
NETRISK_WINDOWS_CERTIFICATE_CSP=DigiCert Signing Manager KSP
NETRISK_WINDOWS_CERTIFICATE_KEY_CONTAINER=<key alias>
NETRISK_WINDOWS_CERTIFICATE_FILE=<path to the public certificate>
```

A `.pfx` without a password is rejected up front rather than left to hang the runner on
signtool's interactive prompt.

`signtool.exe` and `makeappx.exe` are found on `PATH` first and otherwise under
`%ProgramFiles(x86)%\Windows Kits\10\bin\**\x64`, so a plain Windows SDK install is enough —
no developer command prompt required.

### 3.4 Timestamping

Every signature is timestamped (RFC 3161, SHA-256) so it stays valid after the certificate
expires. The build tries the configured primary URL, then any comma-separated fallbacks, then
its built-in list (`timestamp.acs.microsoft.com`, `timestamp.digicert.com`,
`timestamp.sectigo.com`). A timestamp authority outage therefore degrades to a slower build
rather than a failed release.

### 3.5 Verification

After signing, `signtool verify /pa /all` runs over every file and a failure fails the build.
`./build.sh VerifySignatures` re-runs the same check over `output/publish` on demand. This gate
is never bypassed or relaxed.

### 3.6 Certificate rotation

1. Provision the new certificate profile / certificate alongside the old one.
2. Update the runner's environment variables (Trusted Signing: certificate profile name;
   signtool: thumbprint or key container).
3. Cut a release and confirm `VerifySignatures` passes and the published `.exe` shows the new
   publisher in its Windows properties dialog.
4. **MSIX only:** if the certificate *subject* changed, `--msix-publisher` must change with it
   and the MSIX identity changes with it — which Windows treats as a different application.
   Plan that as a migration, not a rotation. Keeping the same subject across rotations avoids
   the problem entirely, so ask the CA to preserve the DN.

---

## 4. macOS Developer ID and notarization

The pipeline, in order, per architecture:

1. `codesign` every nested Mach-O (deepest first) with `--options runtime --timestamp`.
2. `codesign` the `.app` with the same flags plus
   [`entitlements.plist`](../../build/installers/macos/entitlements.plist).
3. `xcrun notarytool submit --wait` the app (zipped with `ditto`), then `xcrun stapler staple`.
4. `pkgbuild` the `.pkg`, `productsign` it, notarize and staple it.
5. Assemble the drag-and-drop `.dmg`, `codesign` it, notarize and staple it.
6. `spctl --assess` each container — a Gatekeeper rejection fails the build.

Notarization rejection is a hard failure. The build prints the submission id, downloads Apple's
notarization log and echoes it, because that log is the only place that names the offending
binary.

### 4.1 Credentials

You need **two** certificates from the Apple Developer portal:

* **Developer ID Application** — signs the app bundle and the DMG (`--mac-signing-identity`).
* **Developer ID Installer** — signs the `.pkg` (`--mac-installer-signing-identity`). Without
  it the `.pkg` is produced unsigned and the build says so.

For notarization, prefer an **App Store Connect API key** over an Apple ID: it has no MFA and
no password to rotate through a human.

1. App Store Connect → Users and Access → Integrations → App Store Connect API → generate a
   key with the **Developer** role. Download the `AuthKey_<KEYID>.p8` **once**.
2. Note the Key ID and the Issuer ID.
3. On the runner:

   ```
   NETRISK_MAC_SIGNING_IDENTITY=Developer ID Application: Example Ltd (TEAMID1234)
   NETRISK_MAC_TEAM_ID=TEAMID1234
   NETRISK_MAC_NOTARY_API_KEY_ID=ABC123DEF4
   NETRISK_MAC_NOTARY_API_ISSUER_ID=11111111-2222-3333-4444-555555555555
   NETRISK_MAC_NOTARY_API_KEY_PATH=/private/tmp/AuthKey_ABC123DEF4.p8
   NETRISK_REQUIRE_NOTARIZATION=1
   ```

   Write the `.p8` from a CI secret to a path outside the workspace and delete it afterwards.

On a developer Mac a stored keychain profile is easier:

```bash
xcrun notarytool store-credentials netrisk-notary \
  --key ~/private/AuthKey_ABC123DEF4.p8 --key-id ABC123DEF4 \
  --issuer 11111111-2222-3333-4444-555555555555
```

then `--mac-notary-keychain-profile netrisk-notary`. When both forms are configured the API key
wins, because it is the one that works headlessly.

### 4.2 Importing the certificate on a CI runner

A hosted macOS runner has no certificates in its keychain. Export the two Developer ID
identities to a single `.p12`, base64 it, and supply:

```
NETRISK_MAC_CERTIFICATE_BASE64=<base64 of the .p12>
NETRISK_MAC_CERTIFICATE_PASSWORD=<its password>
```

The build creates a throwaway keychain with a random password, imports into it, *prepends* it
to the user keychain search list (the login keychain is left in place) and deletes it in a
`finally` block — including when notarization fails.

### 4.3 Entitlements

The hardened runtime is enabled for every binary. Only three entitlements are granted:
`allow-jit` and `allow-unsigned-executable-memory` (CoreCLR JITs managed code) and
`device.camera` (FaceID capture). `disable-library-validation` is deliberately **not** granted:
the build signs every nested Mach-O with its own Developer ID, so library validation passes on
its own, and `Packaging.Tests` fails if that entitlement — or any other hardened-runtime
weakening — is added.

`Info.plist` carries `NSCameraUsageDescription`. Without it a signed build is killed the moment
FaceID touches the camera instead of prompting.

### 4.4 The DMG

Default (`hdiutil`): `NetRisk.app`, an `/Applications` symlink, a `.background/background.png`
and a `.VolumeIcon.icns`. No Finder, no AppleScript — it works on a headless runner. The
trade-off is that macOS only shows the background image and icon positions when a `.DS_Store`
records them, which needs Finder.

`--branded-dmg` uses [`create-dmg`](https://github.com/create-dmg/create-dmg)
(`brew install create-dmg`) to lay the window out properly. It drives Finder through
AppleScript and therefore needs a real GUI session; if `create-dmg` is not installed the build
warns and falls back to the plain layout.

---

## 5. Windows installers

### 5.1 MSI (WiX v5)

Authoring: [`build/installers/windows/msi/NetRisk.wxs`](../../build/installers/windows/msi/NetRisk.wxs).
The runner needs the WiX CLI: `dotnet tool install --global wix --version 5.0.2`. The build
looks for `wix` on `PATH` first, then for a `dotnet wix` from a local tool manifest, and if it
finds neither it reports that exact install command — it never installs a global tool behind
your back. (The repository's `dotnet-tools.json` is deliberately developer-local and gitignored,
so it is not the place to pin this.) **v5 is deliberate:** WiX v6 and v7 refuse to build until
you accept the Open Source Maintenance Fee EULA.

The MSI is the enterprise artifact and has no installer UI; the interactive installer is the
Inno Setup `.exe`. It installs per-machine, harvests the publish directory (so a new dependency
ships without editing the authoring), and wires the upgrade table so version N+1 replaces N and
a downgrade is refused with a message.

Public properties:

| Property | Effect |
|---|---|
| `INSTALLFOLDER` | Install directory (default `%ProgramFiles%\NetRisk`) |
| `SERVERURL` | Writes `netrisk.ini` next to the executable with `[Server] Url=<value>` |
| `INSTALLDESKTOPSHORTCUT` | `1` adds a desktop shortcut |

```powershell
msiexec /i NetRisk-2.16.3-x64.msi /qn `
        INSTALLFOLDER="C:\Apps\NetRisk" `
        SERVERURL="https://netrisk.example.com:5443/"
```

`netrisk.ini` is the client's optional last configuration layer (see
`src/GUIClient/ClientConfigurationSources.cs`), so an administrator-set server URL overrides the
shipped `appsettings.json`. It is authored through the MSI `IniFile` table — no custom action —
and removed on uninstall. The same file works for the macOS and Linux deployments: drop it next
to the executable.

**WiX only runs on Windows.** On macOS/Linux `PackageWindowsMSI` logs a single warning and
skips; the authoring is verified instead by `Packaging.Tests` (upgrade code, per-machine scope,
secure properties, INI row, shortcut targets, no-extension requirement).

### 5.2 MSIX and App Installer

Manifest template:
[`AppxManifest.xml.template`](../../build/installers/windows/msix/AppxManifest.xml.template).
The target stages the package layout (payload + tile assets + rendered manifest), calls
`makeappx pack`, signs the result and writes `NetRisk.appinstaller` alongside it.

Two things to get right:

* **`--msix-publisher` must equal the signing certificate's subject DN, exactly.** Otherwise
  Windows refuses the package. The default `CN=NetRisk` only matches a self-signed test
  certificate.
* **An unsigned MSIX cannot be installed at all.** The target still produces one (with a loud
  warning) so the layout can be inspected, but it is not shippable.

Capabilities are `runFullTrust` (unavoidable for a packaged Win32 app) and `internetClient`.
`Packaging.Tests` pins that exact set, so adding a capability is a deliberate, reviewed change.

Publish `NetRisk.appinstaller` and the `.msix` under `--app-installer-base-uri`; installed
clients then self-update every 8 hours with no updater inside the app.

`makeappx.exe` ships in the Windows SDK, so this target also skips with a warning on
macOS/Linux — after writing the layout and the `.appinstaller`, both of which are validated by
tests.

---

## 6. Linux containers

### 6.1 Flatpak

Manifest template:
[`app.netrisk.NetRisk.yml.template`](../../build/installers/linux/flatpak/app.netrisk.NetRisk.yml.template),
on `org.freedesktop.Platform` 24.08. The .NET side is a self-contained `linux-x64` publish
staged by Nuke, so no SDK runs inside the sandbox.

Sandbox grants, and why:

| Grant | Reason |
|---|---|
| `--socket=wayland`, `--socket=fallback-x11`, `--share=ipc`, `--device=dri` | Rendering and GPU for the Skia backend |
| `--share=network` | NetRisk is a REST client of a NetRisk server |
| `--filesystem=xdg-download` | A predictable destination for exported reports |
| `--talk-name=org.kde.StatusNotifierWatcher`, `--own-name=org.kde.*` | Tray icon (SNI names carry an unpredictable pid suffix) |
| `--talk-name=org.freedesktop.secrets` | Server credentials via the Secret Service |

**Deliberately not granted:** `--device=all`, `--filesystem=home`, `--filesystem=host`, and any
raw bus socket. Two consequences to communicate to users:

* File dialogs go through the XDG desktop portal. Avalonia does this automatically inside a
  sandbox; paths outside the portal selection are not readable.
* **The FaceID camera features do not work under the default sandbox**, because FlashCap opens
  `/dev/video*` directly and there is no fine-grained camera device grant. An administrator who
  needs them opts in per machine:
  `flatpak override --device=all app.netrisk.NetRisk`.

Build and publish:

```bash
./build.sh PackageLinuxFlatpak --configuration Release
# staged manifest + payload: output/publish/flatpak
flatpak build-sign  <repo> --gpg-sign=<key>       # sign the repo for Flathub/self-hosting
flatpak build-update-repo <repo> --gpg-sign=<key>
```

For Flathub, submit the rendered manifest from `output/publish/flatpak` to
`flathub/app.netrisk.NetRisk`; the template in this repository stays the source of truth.
`appstreamcli validate` runs automatically when installed, and a validation failure fails the
build — Flathub rejects invalid metadata.

### 6.2 Snap

Recipe template:
[`snapcraft.yaml.template`](../../build/installers/linux/snap/snapcraft.yaml.template) —
`core24`, `confinement: strict`, the `gnome` extension.

Interfaces: `desktop`, `desktop-legacy`, `wayland`, `x11`, `opengl`, `network`, `home`,
`removable-media`, `password-manager-service`, `camera`. `network-bind` is **not** requested —
the client never listens. `camera` is not auto-connected on most systems, so FaceID needs:

```bash
snap connect netrisk:camera
```

Channel strategy: `--snap-grade` controls the `grade` field and defaults to `stable` for a
`Release` build and `devel` otherwise (a `devel` snap cannot be promoted to `stable`).

```bash
./build.sh PackageLinuxSnap --configuration Release
snapcraft upload output/publish/netrisk_<version>_amd64.snap --release=candidate
# promote after smoke-testing
snapcraft release netrisk <revision> stable
```

Suggested mapping: tagged release → `stable`, release candidate → `candidate`, main branch →
`edge`.

---

## 7. What a CI runner needs

| Job | Runner | Must have |
|---|---|---|
| Windows installers | `windows-latest` | .NET 10 SDK, Windows SDK (signtool + makeappx), `dotnet tool install --global wix --version 5.0.2`, Docker not required |
| Windows signing (Trusted Signing) | same | `dotnet tool install --global sign` |
| macOS bundle/DMG | `macos-14` or newer | Xcode command line tools (`codesign`, `notarytool`, `stapler`, `spctl`, `pkgbuild`, `productsign`, `ditto`), optionally `create-dmg` |
| Linux Flatpak | `ubuntu-latest` | `flatpak`, `flatpak-builder`, the `org.freedesktop.Platform//24.08` + `Sdk//24.08` runtimes, `appstream` |
| Linux Snap | `ubuntu-latest` | `snapcraft` (`snap install snapcraft --classic`), LXD or a `--destructive-mode` container |

Cross-building is not possible for the signed formats: WiX, `makeappx` and `signtool` need
Windows; `codesign` and `notarytool` need macOS; `flatpak-builder` and `snapcraft` need Linux.
Plan one job per platform. See [`docs/ci/`](../ci/README.md) for the existing pipeline
definitions.

Recommended release job order: build and test → Windows job → macOS job → Linux job →
`VerifySignatures` on each platform's own artifacts → publish.

---

## 8. Troubleshooting

| Symptom | Cause |
|---|---|
| `Authenticode signing skipped: No Windows signing material configured` | Expected on a machine with no credentials. Pass `--require-signing` in a release pipeline to make it fail instead. |
| `--require-signing was given but No Windows signing material configured` | The CI secret did not reach the runner. Check the variable names in §2. |
| `signtool failed against every timestamp authority` | All timestamp servers unreachable — usually egress filtering, not an outage. |
| `Notarization of … was not accepted` | Read the log the build printed. Almost always an unsigned nested binary or a missing hardened-runtime flag. |
| MSIX installs nowhere, "publisher name does not match" | `--msix-publisher` is not the certificate's exact subject DN. |
| `MSI packaging skipped: WiX only runs on Windows` | Expected off Windows. |
| `WiX was not found, so no MSI was produced` | Run the `dotnet tool install --global wix` line from §5.1 on the runner. In a `Release` build this is a hard failure rather than a skip. |
| `Snap packaging skipped: snapcraft is not installed` | Expected off Linux; the recipe and payload are still staged. |
| Camera does nothing in the Flatpak/Snap build | By design — see §6. |

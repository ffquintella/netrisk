# Track 5 — Native Packaging & Release Engineering: Detailed Specifications

> Status: **Planned** · Roadmap: [ROADMAP.md → Track 5](../../ROADMAP.md)
> Research basis: web survey of code-signing and desktop-distribution practices (June 2026) — sources at the end of each milestone.

This track automates artifact production for secure, native software distribution. All work lands in the Nuke build (`build/Build.cs`) as new or extended `Package*` targets.

---

## Milestone 5.1: Automated Code-Signing Pipelines

**Critical research findings.**
- Since mid-2023, Windows code-signing keys must live on FIPS 140-2 HSMs — file-based PFX signing is effectively dead. Cloud signing services (Azure Trusted Signing, SSL.com eSigner, DigiCert KeyLocker) are the CI-friendly path.
- **EV no longer bypasses SmartScreen** (behavior removed in 2024) — reputation builds over time regardless, so don't overpay for EV expecting instant trust.
- **Always timestamp** signatures (RFC 3161) so they remain valid after the certificate expires.

### 5.1.1 Windows Authenticode in `PackageWindowsGUI`

- New Nuke parameters/secrets in `build/Build.cs` for the signing provider. Recommend **Azure Trusted Signing** as the first-class integration, with `signtool` + cloud-HSM as the generic fallback.
- Sign every shipped `.exe`/`.dll` and the installer artifacts from 5.2.
- SHA-256 digests; RFC 3161 timestamp server with one retry/fallback TS URL (timestamp-server outages are the classic flaky-build cause).
- Signing activates only when credentials are present — local dev builds stay unsigned and never fail for missing certs.
- A `VerifySignatures` step (`signtool verify /pa /all`) gates artifact publication.
- Document certificate/account setup and rotation in `docs/`.

### 5.1.2 macOS Developer ID & notarization in `PackageMacGUI`

Pipeline, in order:
1. `codesign` all binaries/frameworks **with hardened runtime** (`--options runtime`) and an entitlements file.
2. Sign the `.app` bundle.
3. `xcrun notarytool submit --wait` (credentials via an **App Store Connect API key** — CI-safe, no Apple-ID password).
4. `xcrun stapler staple`.
5. Verify with `spctl --assess`.

- The Nuke target fails hard on notarization rejection and surfaces Apple's log URL.
- Both arm64 and x64 (or a universal binary) go through the same path.
- **Acceptance:** Gatekeeper-clean install verified on a macOS runner.

**Sources (5.1):** [Big Iron — Code-signing certs in 2026](https://www.bigiron.cc/guides/code-signing-certs-for-your-own-binaries-when-it-matters) · [Microsoft Learn — Code signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options) · [Electron — Code signing & notarization flow](https://www.electronjs.org/docs/latest/tutorial/code-signing)

---

## Milestone 5.2: Modern Native Installers

**Research positioning.** MSIX is Microsoft's strategic format (containerized, ~99.96% install success, guaranteed clean uninstall, signature mandatory) — but enterprises still demand MSI for silent GPO/Intune rollouts, so **ship both**. On Linux, Flatpak (Flathub) has the desktop mindshare and Flatpak/Snap both bring sandboxing and update channels.

### 5.2.1 Windows: `.msi` + `.msix`

**MSI**
- WiX Toolset v5 project under `build/`; per-machine install.
- Silent install verified: `msiexec /i netrisk.msi /qn`.
- Upgrade table wired so version N+1 cleanly replaces N.
- MSI properties for install dir and server-URL preconfiguration (enterprise ask).

**MSIX**
- Manifest + packaging in a new `PackageWindowsMSIX` Nuke target.
- **Signed (5.1.1)** — unsigned MSIX won't install.
- An App Installer (`.appinstaller`) file published alongside for built-in auto-update semantics.

**Common acceptance:** both produced by CI from the same compiled output; install/uninstall/upgrade smoke-tested on a Windows runner.

### 5.2.2 macOS: drag-and-drop `.dmg`

- Scripted DMG assembly (e.g., `create-dmg`) inside `PackageMacGUI`: branded background, app icon + `/Applications` symlink drag target, volume name/icon.
- The DMG itself is signed; the app inside is notarized + stapled (5.1.2).
- CI-reproducible with no Finder-scripting dependency.

### 5.2.3 Linux: Flatpak and Snap

**Flatpak**
- Manifest on the org.freedesktop runtime; the .NET app ships as self-contained publish output.
- **Least-privilege sandbox permissions enumerated deliberately:** wayland/x11, network, documents portal for file dialogs.
- AppStream metadata + desktop file; target **Flathub** publication, manifest kept in this repo.

**Snap**
- `snapcraft.yaml` (core22+, self-contained .NET publish), strict confinement with the same interface review.
- Publish to the Snap Store under a registered name; track/channel strategy (stable/candidate/edge mapped to release types).

**Common**
- Built in CI as `PackageLinuxFlatpak` / `PackageLinuxSnap` Nuke-orchestrated targets.
- **Acceptance:** install + launch + connect-to-server on a clean Ubuntu VM for both formats, with file dialogs and tray behavior verified under sandboxing (the classic Flatpak/Snap breakage points for desktop apps).

**Sources (5.2):** [Microsoft Learn — What is MSIX](https://learn.microsoft.com/en-us/windows/msix/overview) · [Redmondmag — MSIX replacing MSI](https://redmondmag.com/articles/2025/09/23/modern-app-packaging.aspx) · [Linux Journal — Flatpak and Snap](https://www.linuxjournal.com/content/future-linux-software-will-flatpak-and-snap-replace-native-desktop-apps)

---

## Dependencies & sequencing

- **5.1 must land before 5.2**: MSIX requires a signature to install at all, and the DMG flow embeds notarization.
- SBOM publication (Track 7.2.2) hooks into the same `Package*` targets — coordinate the target refactoring.

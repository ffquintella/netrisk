# Supply-Chain Security Policy

> Track 7 milestone 7.2.3 · First issued 2026-08-26
> Companion gates: [`.github/dependabot.yml`](../../.github/dependabot.yml), [`.github/workflows/security.yml`](../../.github/workflows/security.yml), [`scripts/security/scan-dependencies.sh`](../../scripts/security/scan-dependencies.sh), [`scripts/security/check-submodule-bump.sh`](../../scripts/security/check-submodule-bump.sh)

NetRisk ships self-contained binaries. Everything inside them — NuGet packages, five vendored git
submodules, and the packages those pull in transitively — is code NetRisk is responsible for even
though it did not write it. This document says how that code gets in, how it gets updated, and who
answers for each piece.

---

## 1. What ships

| Layer | Count | Provenance mechanism |
|---|---|---|
| Direct NuGet packages | ~90 across 33 projects | `nuget.config` with **package source mapping** |
| Transitive NuGet packages | resolved at restore | Same, plus the SBOM records exact versions |
| Vendored submodules | 5 | Pinned commit SHAs + a CI review gate |
| .NET runtime | 1 (self-contained) | `global.json` pins the SDK |
| Build tooling | WiX 5, `sign`, `signtool`, `codesign`, `notarytool`, `flatpak-builder`, `snapcraft`, CycloneDX | Reported-not-installed; never fetched implicitly by a build |

### The bill of materials

Every `Package*` target emits `netrisk-<component>-<version>.cdx.json` (CycloneDX 1.6) plus a
`.sha256`, into the component's publish directory — see [`build/Build.Sbom.cs`](../../build/Build.Sbom.cs).

Generated **at build time from the resolved dependency graph**, which is the entire point: a
hand-maintained list records what somebody believed was shipping, while the resolved graph records
what is, transitive packages and exact versions included. A consumer can feed the manifest to
[OWASP Dependency-Track](https://dependencytrack.org/) and be told about a new CVE in a *released*
version without NetRisk re-scanning anything.

```bash
dotnet tool install --global CycloneDX     # the build reports this; it never installs it for you
./build.sh PackageApi                      # SBOM emitted automatically (GenerateSbom is TriggeredBy)
./build.sh GenerateSbom --require-sbom      # release mode: a missing tool is now a build failure
```

The target follows the same two rules as the Track 5 signing targets, for the same reason: a missing
tool warns once and still produces the artifact (the normal case for a developer and a CI fork), and
only `--require-sbom` turns the gap into an error. A build that silently installs global tooling into
somebody's profile is not a build anyone should trust.

### Package source mapping — why it matters here

`nuget.config` maps `netrisk*`, `Pomelo.EntityFrameworkCore.MySql*`, `OpenFaceONNX`, `umapx` and
`FlashCap*` to the private Cloudsmith feed and everything else to nuget.org. That mapping is not
housekeeping: without it, a **dependency-confusion** attack is available to anyone who can publish
a package named `netrisk.something` to nuget.org, because NuGet would happily resolve it from
whichever feed answered first. With the mapping, a `netrisk*` package is only ever accepted from the
private feed.

---

## 2. Vendored submodules

Five, all under `libs/`, all currently owned by the NetRisk maintainer. A submodule reference *is* a
pin — git records the exact commit — so the risk is not drift; it is that **a bump is a one-line diff
that can pull in any amount of code**, which makes it the highest-leverage, lowest-visibility change
anyone can make to this repository.

| Submodule | Upstream | Attack surface it sits on | Owner |
|---|---|---|---|
| `NessusParser` | `github.com/ffquintella/NessusParser` | **Parses untrusted scan files** — the highest-risk of the five (threat-model boundary TB4) | Maintainer |
| `netrisk-plugin-sdk` | `github.com/ffquintella/netrisk-plugin-sdk` | Defines the plugin contract; a change here changes what a plugin may do (TB5) | Maintainer |
| `reliable-rest-client-wrapper` | `github.com/ffquintella/reliable-rest-client-wrapper` | Every outbound HTTP call from the desktop client, including TLS options (TB1) | Maintainer |
| `Aura.UI` | `github.com/ffquintella/Aura.UI` | Desktop controls; no network, no parsing | Maintainer |
| `TreeDataGrid.Avalonia` | `github.com/ffquintella/TreeDataGrid.Avalonia` | Desktop controls; no network, no parsing | Maintainer |

The first three are **security-relevant**; the last two are presentation. That distinction drives the
review depth below. Note that `TreeDataGrid.Avalonia` is not named in the Track 7 spec — it was added
after the spec was written, which is itself an argument for deriving this table from `.gitmodules`
rather than from prose.

### Review procedure for a bump

Required for every submodule pointer change, and enforced by the `submodule-review` job:

1. **Read the upstream diff.** `git -C libs/<name> log --oneline <old>..<new>` and
   `git -C libs/<name> diff <old>..<new>`.
2. **Confirm the new commit is on the upstream default branch** and is not a force-push over a SHA
   this repository previously pinned. A rewritten upstream history is the signal that something is
   wrong.
3. **Look specifically for changes to parsing, networking, cryptography or file handling.** For the
   three security-relevant submodules, read those hunks line by line rather than skimming the
   summary. For the two presentation submodules, a summary read is proportionate.
4. **Record the review in the pull-request description**: the submodule name, the old and new SHAs,
   what changed, and whether any of it touches the four areas above.
5. **Run the tests.** `NessusParser` in particular is covered by
   `ServerServices.Tests/Track3/ImporterParsingTest.cs` and
   `ServerServices.Tests/Track7/ImporterXxeTest.cs` — the second is what would catch an upstream
   change that re-enabled DTD processing.

The CI gate checks 4, because it is the only one a machine can check, and because writing it down is
what forces 1–3 to have happened. It can be run locally:

```bash
BASE_REF=<base-sha> HEAD_REF=<head-sha> PR_BODY="$(cat message.txt)" \
  ./scripts/security/check-submodule-bump.sh
```

### Vendor or track?

| Choose **track as a submodule** when | Choose **vendor into `src/`** when |
|---|---|
| Upstream is actively maintained and NetRisk's changes flow back | Upstream is abandoned, or NetRisk has diverged permanently |
| The code is genuinely reusable outside NetRisk | The code only makes sense inside NetRisk |
| The security surface is small or well tested | The code needs NetRisk-specific hardening that upstream will not take |

`NessusParser` is the live case for the second column. Track 7 finding NR-2026-022 could not be
fixed upstream from this repository, so it was fixed at the *call site* in `NetRisk`
(`NessusImporter.ParseHardened`) — a workaround that is fine once and a smell twice. If a second such
fix is needed, vendor it.

---

## 3. Update cadence

| Trigger | Action | Who |
|---|---|---|
| Dependabot pull request (weekly, Monday 06:00 UTC) | Review, check the changelog, merge if tests pass | Maintainer |
| `dependency-scan` fails on a **new** advisory | Treat per the [triage SLA](TRIAGE_SLA.md) for the advisory's severity | Maintainer |
| Weekly scheduled `security` workflow | Catches an advisory published against an already-pinned version — a case that appears in **no diff** | Automatic |
| Security-relevant submodule bump upstream | Review and bump within the SLA for the severity it fixes | Maintainer |
| Minor release | Refresh the SBOM, re-run the full gate, update [BURN_DOWN.md](BURN_DOWN.md) | Maintainer |

### Patching a vulnerable dependency

In order of preference:

1. **Upgrade the package.** For a *transitive* dependency, add a direct `PackageReference` pinning
   the fixed version — that is the supported way to lift a transitive package in NuGet.
2. **Upgrade the submodule**, following §2.
3. **Accept, with an expiry.** Add an entry to
   [`security/dependency-suppressions.yml`](../../security/dependency-suppressions.yml). Every entry
   requires an advisory id, an owner, a real reason and an expiry **no more than 180 days out**. On
   the expiry date the scan starts failing again and somebody has to fix it or consciously renew —
   which is the same risk-acceptance-with-an-expiry discipline NetRisk ships as a product feature
   (Track 3.2.3). An expired suppression is a build failure, not a warning.
4. **Never** widen the grep in `scan-dependencies.sh`. That is the one change that would make the
   gate lie, and it is called out in the script's own failure message.

The suppression file is deliberately empty: the baseline scan on 2026-08-26 found **no** vulnerable
package across all 33 projects, so there is nothing to accept. Its schema is validated by
`Packaging.Tests/ContinuousSecurityConfigurationTest.EverySuppressionHasARealExpiryAnOwnerAndAReason`,
so a shape the shell parser would misread fails in a test rather than in CI.

---

## 4. Build-host integrity

The signed formats cannot be cross-built — WiX, `makeappx` and `signtool` need Windows; `codesign`
and `notarytool` need macOS; `flatpak-builder` and `snapcraft` need Linux — so a release passes
through three hosts. Each is part of the supply chain:

* signing material is never in the repository (Track 5 rule 1): parameters or `NETRISK_*`
  environment variables only, `[Secret]`-marked and redacted from logs;
* `VerifySignatures` re-checks every artifact after the fact, so a host that silently failed to sign
  is caught before publication;
* SHA-256 checksums accompany every artifact and every SBOM, so a consumer can verify what they
  downloaded independently of the signature.

---

## 5. What this policy does **not** cover

Stated so the gaps are not mistaken for coverage:

* **Reproducible builds.** Two builds of the same commit are not byte-identical. Achieving that in
  .NET needs deterministic compilation plus a pinned toolchain image; not attempted.
* **Signed commits.** Not required today. It would raise the bar on a compromised maintainer account,
  which is the threat this policy is weakest against.
* **Dependency-Track deployment.** The SBOM is *produced*; nobody is continuously monitoring released
  versions against new advisories. The weekly scan covers `main`, not the versions customers are
  running.
* **Runtime attestation.** Nothing verifies at start-up that the loaded assemblies match the SBOM.
  Plugins in particular are unverified — Track 7 finding NR-2026-027.

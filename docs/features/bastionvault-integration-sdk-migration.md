# Migrating the BastionVault plugin onto `BastionVault.IntegrationSdk`

A design for replacing the hand-written BastionVault wire protocol in
[netrisk-plugin-bastionvault-integration](https://github.com/ffquintella/netrisk-plugin-bastionvault-integration)
with the vendor's own .NET client, `BastionVault.IntegrationSdk`.

This is a design, not a record of work done. Nothing here has been implemented. It is written
against `BastionVault.IntegrationSdk` **0.19.0** and plugin version **1.2.0**; a later SDK release
may move the surface it depends on.

Companion reading: [External secret vaults (BastionVault)](secret-vaults.md) — the feature this
plugin serves, and the host-side contract it must keep honouring.

---

## 1. What exists today

**The plugin** is 779 lines across two files, in its own repository:

| File | Lines | What it holds |
|---|---:|---|
| `src/BastionVaultPlugin/BastionVaultApi.cs` | 284 | The wire protocol: routes, the `X-Vault-Token` header, the `LIST` verb, envelope/keys/fields/token-info parsing, and the operator-facing failure text |
| `src/BastionVaultPlugin/BastionVaultSecretPlugin.cs` | 495 | The three contract operations: connection test, tree walk, secret read |
| `tests/BastionVaultPlugin.Tests/` | — | 33 tests against `FakePluginHttpClient`; no network |

**The SDK** is `net10.0`, has **zero external package dependencies**, and builds with
`TreatWarningsAsErrors`. The surface this migration needs:

- `BastionVaultClient` → `.Auth.Token`, `.Auth.Ferrogate`, `.Sys`, `.Kv` (`.V1`, `.V2`,
  `DetectVersionAsync`, `ReadManyAsync`), `.Logical`
- `ITransport` — the transport injection point, with `TransportRequest` / `TransportResponse`
- `BastionVaultException` — one error type carrying `Code` (`BV-*`), `Hint`, `ServerMessage`,
  `ServerErrors`, `StatusCode`, `RetryAfter`, `Retryable`
- `EnvironmentSource` — `Process` (the default) and `None`

**NetRisk itself speaks no BastionVault.** The host resolves an address to a single node
([`VaultEndpointResolver`](../../src/ServerServices/Secrets/VaultEndpointResolver.cs)) and hands the
plugin that node plus an `IPluginHttpClient`
([`SecretVaultService.OpenAsync`](../../src/ServerServices/Secrets/SecretVaultService.cs)). Every
line of protocol lives in the plugin.

One consequence worth stating plainly: **the `uox-bastionvault` NuGet source added to this
repository's `nuget.config` is not required by this migration.** The consumer is the plugin
repository. The source only becomes load-bearing here if §7's optional second phase is ever taken.

---

## 2. Why do this at all

Not for line count — the plugin will not get much shorter. Three reasons, in order of weight.

### 2.1 The plugin does not support KV v2, and says something misleading when it meets one

This is demonstrable from the code rather than inferred.

`BastionVaultApi.ParseFields` takes the envelope's `data` object and treats each key as a field of
the secret. On a **`kv-v2`** mount a read answers `data: { data: { … }, metadata: { … } }`. The
plugin therefore sees a secret with two fields named `data` and `metadata`, and
`BastionVaultSecretPlugin.SelectValue` reports:

> BastionVault secret '…' holds several fields (data, metadata) and no field was selected.
> Re-select it and name the field to use.

A perfectly valid credential becomes an error message that sends the operator to re-select a field
that was never the problem. The same mismatch breaks enumeration: the walk lists `secret/`
directly, while a v2 mount keeps its keys under `secret/metadata/`.

Neither case is covered by the existing tests — the words `v2` and `metadata` do not appear in
`BastionVaultSecretPluginTest.cs`.

The SDK closes both: `Kv.DetectVersionAsync(mount)` reads the mount type (through a per-client
cached `sys/mounts`, so twenty mounts cost one request), `Kv.V2.ListAsync` composes the `metadata/`
group, and `Kv.V2.ReadSecretAsync` unwraps `data.data`.

### 2.2 The version of a secret is currently thrown away

`GetSecretAsync` returns `VaultSecretValue.Version = null` unconditionally, because a v1 read
carries no version. `KvV2Secret.Metadata.Version` supplies one — which is what makes a rotation
visible in NetRisk's log, the reason the field exists on the contract.

### 2.3 The tree walk is unpaced

The walk issues one `LIST` per folder up to `MaxSecrets = 2000`, against a server the SDK documents
as banning at 200 requests in 10 seconds. Opening the secret picker against a large estate is a
sustained burst today. The SDK's client-side rate gate paces it, and `Kv.ReadManyAsync` collapses
many reads into one request where a caller needs several secrets.

Underneath all three: the wire details in `BastionVaultApi` were, by its own header comment,
*"verified against the server source rather than the reference documentation"*. That is careful
work, and it is also a copy of the server's behaviour maintained by hand in a second repository.
The SDK is the vendor's copy, tested against captured fixtures.

---

## 3. The load-bearing piece: an `ITransport` over `IPluginHttpClient`

Everything else depends on this.

The plugin **must not** use the SDK's own `HttpClientTransport`. NetRisk's rule is that all plugin
egress goes through [`IOutboundHttpClient`](../../src/ServerServices/Interfaces/IOutboundHttpClient.cs), which is where
the SSRF destination policy is applied — without it, a `BaseUrl` an operator pasted in reaches
whatever it names, including `169.254.169.254`. That is the entire reason
[`PluginHttpClientAdapter`](../../src/ServerServices/Secrets/PluginHttpClientAdapter.cs) exists.

The SDK provides the seam: `BastionVaultClientOptions.Transport` takes any `ITransport`. New file
in the plugin, roughly 120 lines:

```csharp
internal sealed class PluginHttpTransport(IPluginHttpClient http) : ITransport
{
    public bool SupportsCustomVerbs => true;

    public async Task<TransportResponse> SendAsync(TransportRequest request, CancellationToken ct = default)
    {
        // TransportRequest.Uri/Headers/Body(bytes) -> PluginHttpRequest(url/headers/body string)
        // PluginHttpResponse.StatusCode == 0      -> throw BastionVaultException(BV-TRANSPORT-001, …)
    }
}
```

Four constraints on it, none negotiable:

1. **Transport failures must surface as `BastionVaultException` carrying
   `BV-TRANSPORT-001/002/003/005`**, never as a raw exception — that is how the SDK's retry
   classifier reads them. Writable from outside: the `BastionVaultException` constructor and the
   `ErrorCodes` constants are public.
2. **`SupportsCustomVerbs` must be `true`, and truthfully so.** The SDK fails construction with
   `BV-CONFIG-009` if a transport declares otherwise, because the server has no `?list=true`
   fallback. It is true here: `OutboundHttpClient` builds `new HttpMethod(request.Method)`, so
   `LIST` passes through — as it already does today.
3. **Body conversion is UTF-8 both ways.** `PluginHttpResponse.Body` is a `string?` while
   `TransportResponse.Body` is bytes. Acceptable because no operation this plugin uses is binary;
   `Sys.BackupAsync` would be, and is not used. Worth a comment at the conversion, not silence.
4. **`MaxResponseBytes` is honoured by the host, at the host's own number — stage 0, done.**
   The SDK requires the transport to abort an oversized response *while reading* (TRN-033). The host
   seam now does: [`OutboundHttpClient`](../../src/ServerServices/Http/OutboundHttpClient.cs) sends
   with `HttpCompletionOption.ResponseHeadersRead` and reads through a length-limited loop, so a
   body past `OutboundHttpRequest.MaxResponseBytes` is abandoned mid-read rather than measured after
   the allocation. A `Content-Length` already over the cap is refused before the body is touched at
   all. Either way the caller sees the ordinary transport-failure shape — status 0 with
   `TransportError` — which is what `PluginHttpTransport` already maps.

   Previously this was a pre-existing host defect that affected **every** plugin HTTP call, not just
   BastionVault's: `ReadAsStringAsync` with no cap buffered whatever an operator-configured remote
   chose to send. Fixed in `netrisk` ahead of the migration.

   **The cap is the host's, not the SDK's.** The default is 16 MiB
   (`OutboundHttpRequest.DefaultMaxResponseBytes`) — roughly an order of magnitude above the largest
   real response through this seam, and deliberately far below the SDK's own 128 MiB default, which
   is sized for a `Sys.BackupAsync` snapshot this plugin does not read. `MaxResponseBytes` was
   **not** added to `PluginHttpRequest`, on the same reasoning that keeps `AllowInvalidCertificate`
   off it: a plugin that could raise its own ceiling could restore the unbounded allocation. So a
   `BastionVaultClientOptions.MaxResponseBytes` above 16 MiB is silently the host's number instead —
   the transport must not pretend otherwise, and the honest adapter behaviour is to let the host's
   refusal surface as the transport error it is.

   Evidence: `ServerServices.Tests/Track7/OutboundHttpResponseSizeTest` — an undeclared 64 MiB body
   against a 64 KiB cap is rejected with the server having written a small fraction of it, a
   `Content-Length` over the cap fails before the body is read, and a body at or under the cap is
   returned unchanged.

---

## 4. Client construction: four settings that must be explicit

```csharp
new BastionVaultClient(new BastionVaultClientOptions
{
    Address           = context.Credentials.BaseUrl,
    Token             = context.Credentials.ApiKey,
    Transport         = new PluginHttpTransport(context.Http),
    ClusterDiscovery  = false,   // (1)
    AllowInsecureHttp = true,    // (2)
    RetryPolicy       = /* few attempts */, Timeout = /* short */,   // (3)
}, EnvironmentSource.None);      // (4)
```

The sketch deliberately forwards neither `MachineId` nor `AppId`. `MachineId` is *checked*, never
sent — see
[secret-vaults.md § Machine identity is checked, not sent](secret-vaults.md#machine-identity-is-checked-not-sent).
`AppId` is §5.3, and the answer there is not "add a line here".

**(4) is the dangerous default.** `new BastionVaultClient(options)` reads the **process
environment** for every unset setting, and the process is the NetRisk API or background-job host. A
`VAULT_TOKEN` or `VAULT_ADDR` in that environment would silently override the connection an
operator configured — a credential source nobody chose and nothing displays.
`EnvironmentSource.None` is the SDK's own environment-free construction (CFG-005) and is the only
correct choice for a plugin.

**(1) `ClusterDiscovery = false`.** The host already did SRV discovery, health-scored the
candidates and cached the selection per connection; it hands the plugin one node. Letting the SDK
rediscover duplicates the DNS work, bypasses the host's cache — and, more seriously, the SDK's
`DnsSrvResolver` resolves outside `IPluginHttpClient`, which means outside the destination policy.

**(2) `AllowInsecureHttp = true`.** The SDK refuses a non-loopback `http://` address at
construction. Whether plain HTTP or an unverified certificate is acceptable is a decision the host
already owns (the connection's `IgnoreSslErrors`, plus the outbound policy). A second judge here
turns a working internal deployment into a configuration error with an unfamiliar code.

**(3) Retry and timeout must be reconciled, not stacked.** `IOutboundHttpClient` has its own
timeout; the SDK adds a retry policy on top. Left at defaults that is N × 30 s hanging in front of
a credential read. Map `TransportRequest.Timeout` onto `PluginHttpRequest.Timeout` in the adapter
and keep the SDK's policy short.

---

## 5. Operation by operation

| Today | After | Note |
|---|---|---|
| `GET auth/token/lookup-self` + `ParseTokenInfo` | `client.Auth.Token.LookupSelfAsync()` | `TokenInfo.Meta["spiffe_id"]` stays the proof of machine binding |
| `GET auth/ferrogate/requirement` + `MachineRequirement` + swallowed 404 | `client.Auth.Ferrogate.RequirementAsync()` / `IsMachineIdentityRequiredAsync()` | The SDK's `FerrogateRequirement` is field-for-field identical (`RequireMachineIdentity`, `ExpectedAudience`, `TrustDomain`, `MiaEnvironment`). The deliberate swallow becomes `catch (BastionVaultException)` on code |
| `GET sys/mounts` + `ParseSecretMounts` | `client.Sys.ListMountsAsync()` + the existing filter | The filter is **NetRisk policy, not protocol** — keep it |
| `LIST <folder>` + `ParseKeys` | `Kv.V1.ListAsync` / `Kv.V2.ListAsync`, chosen by the ladder in §5.1 | Fixes §2.1 enumeration |
| `GET <path>` + `ParseFields` | `Kv.V1.ReadAsync` / `Kv.V2.ReadSecretAsync` | Fixes §2.1 read, supplies §2.2 version |
| `DescribeFailure(status, body, transportError)` | map `BastionVaultException` → NetRisk message | See below |

### 5.1 Choosing v1 or v2 without `sys/mounts`

`Kv.DetectVersionAsync(mount)` is built on `Sys.MountTypeOfAsync`, which is built on
`Sys.ListMountsAsync` — that is, on **`sys/mounts`**, the one request the 403 fallback below exists
to avoid. A token scoped to `secret/netrisk/*` and nothing else is a *good* configuration, and it
is precisely the token for which detection would re-issue the forbidden request and abort the
enumeration the fallback was meant to rescue. Worse, `MountTypeOfAsync` caches only on success, so
the denial repeats on every call rather than once.

So version detection is a ladder, not a call, and it runs **once per mount** with the verdict held
for the duration of the walk:

1. `Kv.DetectVersionAsync(mount)`. Authoritative whenever `sys/mounts` is readable, and free after
   the first mount — the mount-table cache is per client, so twenty mounts cost one request.
2. On a denial, probe with `Kv.V2.ReadConfigAsync(mount)`. `{mount}/config` exists only on a v2
   mount; a non-null answer is v2, and the SDK already returns `null` for the 404-with-empty-body
   case rather than throwing.
3. On a denial there too — a token may hold `read` on `secret/data/netrisk/*` and nothing on
   `secret/config` — attempt `Kv.V2.ListAsync("", mount)`. Keys back means v2.
4. Otherwise treat the mount as v1, which is what the plugin assumes unconditionally today. The
   fallback is the current behaviour, so the worst case of the ladder is no worse than the status
   quo.

Steps 2–4 only ever run on the least-privilege path. A token that can read the mount table never
pays for them.

**This needs its own regression test**, and it is the one most likely to be skipped: a token that
can read secrets but **cannot** read `sys/mounts`, asserted on both a v1 and a v2 mount, and
asserted to issue `sys/mounts` at most once rather than once per folder.

### 5.2 What must survive the rewrite

This is where the regression risk concentrates. None of the following is protocol knowledge the SDK
can replace:

- **`DescribeFailure`'s operator-facing text.** *"BastionVault is sealed (HTTP 503). It must be
  unsealed before it can serve secrets."* and *"A list operation needs the LIST verb; a proxy in
  front of the vault may be dropping it."* are written for a NetRisk administrator. The SDK's `Hint`
  is good but generic and speaks the SDK's idiom. What disappears is the *parsing*; the judgement
  about wording stays, as a `BastionVaultException` → message function of comparable size.
- **The redaction rule inside it.** Echo `ServerMessage` / `ServerErrors` only; never an arbitrary
  response body. The original reason has not changed: an arbitrary body may contain anything,
  including a reflected credential.
- **The walk.** `MaxSecrets = 2000`, `MaxDepth = 10`, a 403 skipping one folder rather than
  aborting the enumeration, and the fallback to the conventional `secret/` mount when `sys/mounts`
  answers 403. The SDK has no recursive enumeration; it supplies the per-level `ListAsync`.
- **`SelectValue` in full.** "A requested field that is missing is an error, never a fallback" is a
  NetRisk security rule, not a vault behaviour.
- **The path guard** (`..`, trailing `/`) and its message. The SDK has `RequireSafePrefix`, but the
  text the operator needs is the plugin's.

Expected shape afterwards: `BastionVaultApi.cs` falls from 284 lines to roughly 80 (error mapping
and the mount filter); `BastionVaultSecretPlugin.cs` stays about the same size, with parsing
replaced by typed calls. **The gain is not fewer lines — it is trading a hand-maintained copy of
someone else's protocol for the vendor's, which is tested against captured fixtures.**

### 5.3 What happens to `AppId`

[`SecretVaultService.OpenAsync`](../../src/ServerServices/Secrets/SecretVaultService.cs) puts the
connection's stored application identity into `SecretVaultCredentials.AppId`, and the host refuses
to save a connection without one when the plugin declares `RequiresAppId`. **The BastionVault
plugin sends it nowhere, and cannot**: its pinned `netrisk-plugin-sdk` submodule predates both
members — `SecretVaultCredentials` there has no `AppId` property and `INetriskSecretVaultPlugin` has
no `RequiresAppId`. An App ID typed into a BastionVault connection today is encrypted, stored,
counted by the reference registry, and silently dropped at call time.

That is a pre-existing gap, not one this migration introduces, and no BastionVault connection can
be relying on `app_id` authorization through this plugin — the identity has never been on the wire.
But the migration is the moment it stops being invisible, so it is decided here rather than left
out of the sketch in §4:

- **Bump the `sdk/` submodule** to a revision that carries `AppId` and `RequiresAppId`. Required
  regardless, so the plugin compiles against the contract the host actually passes.
- **Then choose, explicitly.** Either keep `RequiresAppId => false` and **document in
  `secret-vaults.md` that BastionVault ignores the field**, so an operator stops filling in a box
  that does nothing — or wire the SDK's AppRole login, `Auth.AppId.LoginAsync(roleId, secretId)`.
  The second is not a one-line mapping and must not be improvised: AppRole needs a **role id and a
  secret id**, while the NetRisk contract offers one `ApiKey` plus one `AppId`, and deciding which
  is which changes what an operator must paste into the connection. It is a contract question for
  the host, not a detail of this migration.

The first option is the default this design recommends, because it is truthful about today's
behaviour and costs nothing. The second is a feature, and should be scoped as one.

> **Decided: the second option, shipped in plugin v1.4.0.** The recommendation above was overtaken
> by an incident. A BastionVault app secret was pasted into the API key box of a real connection and
> sent verbatim as the token header; the vault's audit log recorded `(unauthenticated) …
> reason=invalid-token`, which is indistinguishable from an expired token, and the App ID sitting on
> the same connection was — exactly as this section predicted — encrypted, stored, and dropped at
> call time. Documenting that the field does nothing would not have prevented it, because the
> operator's expectation was the reasonable one: an app credential is exchanged for a token.
>
> The contract question this section flags was answered **app id → `role_id`, API key →
> `secret_id`**, on the grounds that the API key box is already write-only and encrypted at rest
> while the App ID box is plain readable text — which is exactly the sensitivity of the two values.
> The mode is selected per connection by whether the app id is present, so `RequiresAppId` stays
> `false` and no existing connection changes behaviour. The submodule was bumped to `3db5811`, the
> revision the host already pins.
>
> One thing this section did not anticipate: posting the credential in a *request body* makes a
> reflected-credential leak realistic, because a login rejection quotes what it was sent. The
> plugin now scrubs the API key out of every message it emits. See
> [secret-vaults.md § The API key is a token or a secret id](secret-vaults.md#the-api-key-is-a-token-or-a-secret-id).

---

## 6. Tests

The change is large and lands with proof, per
[src/AI_TESTING_INSTRUCTIONS.md](../../src/AI_TESTING_INSTRUCTIONS.md).

- **The 33 existing tests stay unedited.** They stub `IPluginHttpClient`, which remains the
  injection point — now one layer below, behind `PluginHttpTransport`. Them passing untouched *is*
  the non-regression evidence. Any test that does need editing needs an individual reason; a bulk
  adjustment to get green would be exactly the weakening the testing rules forbid.
- **New regression tests for KV v2** — a `kv-v2` mount, a read answering `data.data`, asserting the
  right value comes out, and an enumeration finding keys under `metadata/`. These fail on today's
  code and pass after, which is the standard this repository holds fixes to.
- **`PluginHttpTransport` tests** — `StatusCode == 0` becomes `BV-TRANSPORT-001`; a `LIST` reaches
  the seam intact; the `X-Vault-Token` header is set; **the token never appears in an exception
  message or a log line**.
- **An environment-isolation test** — with `VAULT_ADDR` set in the test process, the client still
  uses the connection's `BaseUrl`. This is the guard on §4(4), and without it that setting is a
  comment.
- **A least-privilege detection test** — a token that reads secrets but is denied `sys/mounts`,
  on a v1 mount and on a v2 mount, asserting the §5.1 ladder resolves both and issues
  `sys/mounts` at most once. Without it the 403 fallback and version detection can silently
  contradict each other, which is the failure mode §5.1 exists to prevent.

---

## 7. Optional second phase: the host (recommended *not* to do)

[`VaultEndpointResolver`](../../src/ServerServices/Secrets/VaultEndpointResolver.cs) (237 lines),
[`VaultAddress`](../../src/ServerServices/Secrets/VaultAddress.cs) (189) and
`DnsClientSrvLookup` (68) do host-side what the SDK's section 13 does: RFC 2782 ordering, health
probing, a TTL-bounded cache.

Leave them. They are tested, they are where the destination policy is enforced, and the SDK's DNS
path runs outside the host's HTTP seam — adopting it would hand the SDK the destination decision.
If it is ever revisited it is a security decision in its own right, not a by-product of this
migration.

---

## 8. Risks

| Risk | Weight |
|---|---|
| The SDK's `MaxResponseBytes` is capped by the host's, which a plugin cannot raise (§3.4) | **Low — stage 0 landed.** `OutboundHttpClient` now reads through a length-limited stream with a 16 MiB default, so an oversized vault or list response is refused mid-read rather than allocated. What remains is the mismatch: an SDK caller asking for more than 16 MiB gets 16 MiB, and no operation this plugin uses comes close |
| SDK 0.19.0 is pre-1.0 and **declares no conformance level**; its public surface may move | Medium — pin an exact version, never a range |
| SDK retry stacked on the host's timeout (§4.3) | Medium, and a configuration error rather than a design one — covered by a timing test |
| The SDK DLL loads inside the plugin's `McMaster` load context | Low — zero transitive dependencies, so no version conflict with the host |
| Version detection collides with the least-privilege `sys/mounts` fallback (§5.1) | **Medium — the likeliest way to ship a regression**, because the naive `DetectVersionAsync` call looks correct and breaks exactly the token configuration the fallback was written for |
| The `sdk/` submodule pin predates `AppId` / `RequiresAppId` (§5.3) | Low for this migration, but it must be bumped before the plugin compiles against the contract the host passes |
| `secret-vaults.md` § *The BastionVault wire protocol* becomes partly wrong | Certain — updating it is part of the work, not a follow-up |

---

## 9. Staging

Each stage ends with the suite green; none leaves the plugin unusable.

| Stage | Work | Rough size |
|---|---|---|
| **0** ✅ | **In `netrisk`, not the plugin:** bound the response read in `OutboundHttpClient` — `OutboundHttpRequest.MaxResponseBytes` (16 MiB default), `HttpCompletionOption.ResponseHeadersRead`, a length-limited read, early refusal of an oversized `Content-Length`, and `Track7/OutboundHttpResponseSizeTest`. **Done** — prerequisite for §3.4 cleared; closed an existing unbounded-allocation path for **all** plugin egress | done |
| 1 | `PluginHttpTransport` + client factory + adapter tests. No operation touched | ½ day |
| 2 | Migrate `TestConnectionAsync` (lookup-self, FerroGate) — smallest surface, identical SDK types | ½ day |
| 3 | Migrate read and enumeration with the §5.1 detection ladder, plus the KV v2 and least-privilege regression tests | 1–2 days |
| 4 | Bump the `sdk/` submodule and settle `AppId` (§5.3); reduce `BastionVaultApi.cs` to error mapping; bump to 1.3.0; `make pack`; update `secret-vaults.md` | ½ day |

---

## 10. Key files

| Path | Repository |
|---|---|
| `src/BastionVaultPlugin/BastionVaultApi.cs` | netrisk-plugin-bastionvault-integration |
| `src/BastionVaultPlugin/BastionVaultSecretPlugin.cs` | netrisk-plugin-bastionvault-integration |
| `tests/BastionVaultPlugin.Tests/` | netrisk-plugin-bastionvault-integration |
| [`Contracts/Secrets/INetriskSecretVaultPlugin.cs`](../../libs/netrisk-plugin-sdk/Contracts/Secrets/INetriskSecretVaultPlugin.cs) | netrisk-plugin-sdk |
| [`ServerServices/Secrets/PluginHttpClientAdapter.cs`](../../src/ServerServices/Secrets/PluginHttpClientAdapter.cs) | netrisk |
| [`ServerServices/Secrets/SecretVaultService.cs`](../../src/ServerServices/Secrets/SecretVaultService.cs) | netrisk |

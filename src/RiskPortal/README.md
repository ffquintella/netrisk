# NetRisk Risk Portal

The business-facing surface of Track 8 milestone 8.6: a small web application where the people an
entity has appointed as its risk reviewers periodically review, rank and decide their entity's risks —
accept them, commission mitigation work, or escalate — without needing the desktop client or GRC
training.

## Architectural decision record

The spec asked for "Razor Pages or Blazor Server; decide at implementation via a short ADR". This is
that ADR.

**Razor Pages.** Three reasons, in order of weight:

1. **The audience is executives on phones.** A Blazor Server page needs a live WebSocket for every
   interaction; on a phone that drops out of coverage mid-review, the page stops responding and the
   circuit has to be re-established. A Razor Pages form survives a tunnel.
2. **The interaction is a form, not an application.** The reviewer reads a list, drags it into order,
   and answers three questions per risk. That is server-rendered HTML with one small piece of
   JavaScript for the drag handle — not a component tree with state.
3. **It is one process fewer to reason about under load.** Blazor Server holds per-user server-side
   state for the length of a session; a review campaign for a large entity is a long session.

**Why not reuse `ClientServices`.** The desktop client's `RestService` is a process-wide singleton
holding one user's token, established through an interactive login. A web application needs a client
per request, scoped to the signed-in user's session. `PortalApiClient` is that: a thin typed
`HttpClient` over the same REST endpoints, which is a smaller thing than adapting a desktop-shaped
service to a request-scoped lifetime.

**Why the `WebSite` project is untouched.** `WebSite` is deliberately database-decoupled — SQLite plus
a signed periodic sync — because it is the public-facing download site. The portal needs live,
authenticated, read-write access to the register. Putting it in `WebSite` would either break that
decoupling or make the portal read stale data.

## Authentication

The portal authenticates against the API exactly as the desktop client does, because the API's
security model requires it: every credential presentation, Basic or Bearer, is checked against an
**approved client registration**.

1. On first start the portal registers itself (`POST /Registration`) with a stable client id derived
   from its configured `Portal:ClientId`, or a generated one persisted to the data directory.
2. Until an administrator approves that registration in the desktop app, the sign-in page says so and
   shows the id to approve. This is the same one-time ceremony the desktop client goes through.
3. Sign-in exchanges the user's credentials for a JWT (`GET /Authentication/GetToken` with Basic
   auth), and the JWT goes into a data-protected authentication cookie. **The password is never
   stored** — not in the cookie, not in the session, not in memory beyond the request that used it.
4. Every subsequent API call sends `Authorization: Bearer <jwt>` plus the `ClientId` header.

Signing out calls `POST /Sessions/Logout`, which revokes the token server-side by its `jti` (security
finding NR-2026-028) rather than merely dropping the cookie.

## Configuration

| Key | Meaning |
|---|---|
| `Server:Url` | The NetRisk API base URL. Required. |
| `Portal:ClientId` | Stable client id for the registration. Generated and persisted if absent. |
| `Portal:Hostname` | Reported at registration so an administrator can recognise it. |
| `https:port` / `https:certificate:file` / `https:certificate:password` | Optional; when absent the portal listens on the host's default Kestrel endpoints, which is what a reverse-proxy deployment wants. |

Secrets come from the environment (`Server__Url`, `Portal__ClientId`) or, in a Debug build, from
user-secrets — the precedence the rest of the product uses: file, then user-secrets, then environment.

## Running it

```bash
Server__Url=https://127.0.0.1:5443 dotnet run --project src/RiskPortal/RiskPortal.csproj
```

Nuke targets: `./build.sh CompileRiskPortal` and `./build.sh PackageRiskPortal`.

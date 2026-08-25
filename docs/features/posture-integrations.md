# Posture integrations: Trend Micro Vision One and SecurityScorecard

> Track 4 milestones 4.4 and 4.5.

Both integrations feed the same three things: asset inventory, findings, and the entity-wide **Cyber
Risk Index**. Both ingest their findings through the shared ingestion pipeline rather than writing
`vulnerabilities` rows directly — that is what gives them the same deduplication, sticky triage and
SLA due dates as a Nessus import. An integration that inserted its own rows would reactivate every
false positive on every sync.

**The Cyber Risk Index is 0–100 where higher is worse**, consistent with the vendor scores it
aggregates. That matters below.

## Trend Micro Vision One (4.4)

### Connection

Vision One is **regional**, and a key issued in one region is rejected by every other. The connection
picks a region and the API root is derived from it:

| Region | API root |
|---|---|
| `us` | `https://api.xdr.trendmicro.com` |
| `eu` | `https://api.eu.xdr.trendmicro.com` |
| `jp` | `https://api.xdr.trendmicro.co.jp` |
| `sg` | `https://api.sg.xdr.trendmicro.com` |
| `au` | `https://api.au.xdr.trendmicro.com` |
| `in` | `https://api.in.xdr.trendmicro.com` |
| `mea` | `https://api.mea.xdr.trendmicro.com` |

An unlisted region is allowed with an explicit https base URL.

**Test connection** reads one row of `/v3.0/asrm/attackSurfaceDevices` — the endpoint the sync
actually uses. A `/whoami`-style probe would pass with a token that lacks the ASRM permission, which
is the failure that matters. A 401 names the configured region, because that is nearly always the
cause.

### Inventory (4.4.2)

`GET /v3.0/asrm/attackSurfaceDevices`, all pages, following the opaque `nextLink` **verbatim**.

Devices are matched against existing hosts strongest-identity-first:

1. `(external_provider, external_id)` — the provider's own id
2. MAC address
3. FQDN
4. hostname
5. IP

IP is last on purpose: DHCP makes it the weakest of the five, and matching on it first merges two
machines that happened to share a lease.

On a match, the external id is written so the next sync matches directly, and **only empty fields are
filled in**. A hostname a person typed is better data than one an agent guessed, and overwriting it
nightly is how an integration becomes something people turn off. Criticality is the exception — it is
the asset classification the customer configured in Vision One, which is more current than a NetRisk
value nobody maintains.

Vision One expresses criticality as a word (`critical`, `high`) *and* as a number, on a 1–5 scale *and*
on 0–100. All four are normalized to 1–5; a 0–100 value is banded rather than truncated, because
truncating 80 and 20 alike to 5 would flatten the distinction the customer configured.

### Vulnerabilities and virtual patching (4.4.3)

`GET /v3.0/asrm/vulnerableDevices`, expanded to **one finding per CVE per device** — one finding
listing thirty CVEs cannot be triaged or given an SLA. The dedup identity is
`{deviceId}:{cveId}`.

When Vision One reports a **virtual patch** (an IPS rule covering the CVE on that device), the rule id
is recorded in the finding's evidence so a triager knows a compensating control is in place without
going to the Vision One console.

Whether that *closes* the finding is a per-connection policy and is **off by default**. A virtual patch
is a compensating control, not a fix — the underlying software is still vulnerable — so closing the
finding by default would quietly hide unpatched software. When it is on, the transition is recorded
with `source=Job` and the IPS rule in the justification, and a finding somebody has marked false
positive is left alone rather than reopened and re-closed.

### Risk scores and the index (4.4.4)

`GET /v3.0/asrm/highRiskDevices` merged with the inventory's scores. Each device's 0–100 score lands
on `hosts.risk_score` with its source, and the entity's index is a **criticality-weighted mean**:

```
index = Σ(score × criticality) / Σ(criticality)
```

Weighted rather than a plain average: a critical server at 90 and twenty test machines at 10 should
not average out to "fine", which is exactly what an unweighted mean would say.

**Write-back** of asset criticality and acceptance-derived exemptions
(`POST /v3.0/asrm/attackSurfaceDevices/update`) is available and **off by default** — writing into
somebody's EDR console is not something an integration should start doing on its own.

## SecurityScorecard (4.5)

### Connection

Authenticates with `Authorization: Token <key>` — **not** `Bearer`, which is rejected outright and
produces a puzzling 401 against a valid key.

The target is a **bare registered domain** (`acme.com`). A URL, a path or an email address is refused
at save: typing one produces a 404 from SecurityScorecard that reads as "no scorecard exists" rather
than "you typed a URL".

**Test connection** reads `GET /companies/{domain}`, which is the one call that proves both the token
works and the domain is visible to this account.

### Posture and the ten factors (4.5.2)

| Endpoint | Produces |
|---|---|
| `GET /companies/{domain}` | Overall score (0–100) and letter grade |
| `GET /companies/{domain}/factors` | The ten factor scores |

The ten factors: network security, DNS health, patching cadence, endpoint security, IP reputation,
application security, cubit score, hacker chatter, leaked information, social engineering.

Factor history is **append-only** — one row per factor per run, plus a synthetic `overall` row flagged
as such so the whole posture history is one ordered query. Overwriting yesterday's Patching Cadence
would leave the product knowing the current score and nothing about whether it is getting worse, which
is the only question a factor score can usefully answer.

**The score is inverted into the index.** SecurityScorecard is 0–100 where *higher is better*; the
Cyber Risk Index is 0–100 where higher is worse. An 88 (grade B) becomes an index of **12**. Getting
this backwards would report a well-rated company as the riskiest entity in the register.

### Findings (4.5.3)

| Endpoint | Category |
|---|---|
| `GET /companies/{domain}/issues/potentially_vulnerable` | `SecurityScorecard_Vulnerability` — CVEs on the domain's assets |
| `GET /companies/{domain}/issues` | `SecurityScorecard_Issue` — missing SPF, expiring SSL, open ports, … |

Both are ingested as findings against a synthetic **domain asset** host, because SecurityScorecard
rates a domain and NetRisk's register is organized by asset. Findings attached to no host at all would
be invisible in every asset-oriented view.

The dedup identity is `{type}:{target}:{cve}` — the same issue on the same host is the same finding,
and the same issue on a different subdomain is not. Titles are humanized: a register full of
`spf_record_missing` is unreadable.

SecurityScorecard's `positive` severity means *good news*, and maps to `None` so the ingestion
pipeline's negligible filter drops it.

The issue list is treated as a **full scan** — it is the complete current state for the domain, so an
issue that has dropped off it genuinely has been resolved and may be auto-closed. Vision One's device
list is **not**: it covers the devices Vision One knows about, and treating that as exhaustive would
auto-close every finding from every other scanner.

## Scheduling and observability

Both jobs run daily (`TrendMicroSync` 03:00, `SecurityScorecardSync` 04:00 UTC) and ask the service
which connections are **due** by their own interval — so a six-hour tenant and a weekly one are served
by one recurring job, and a connection synced by hand from the admin UI resets its own clock.

Every run writes an `integration_sync_logs` row: counts, a human summary, and any error. The status is
three-valued — a run that imported 900 of 1000 devices is `PartiallySucceeded`, which is neither a
success nor a failure, and reporting it as either is how a persistent per-device mapping bug goes
unnoticed for months.

## Credentials

API keys and tokens are encrypted at rest with a key derived from the installation's server secret. No
endpoint returns them; the connection view carries `hasApiKey` / `hasApiToken` and nothing more.

## Known limitations

* Vision One paging stops after 500 pages (100,000 devices) and logs that it truncated.
* SecurityScorecard issue paging stops after 50 pages (25,000 rows per endpoint) and logs the same.
* Only one posture provider writes an entity's index; the last sync wins and records its source in
  `entities.posture_source`.
* Vision One's exemption write-back sets asset criticality and a description; it does not create a
  formal Vision One exception object.

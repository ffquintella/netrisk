# Hosts view redesign (S38)

Status: design approved for implementation, 2026-10-01. Delivered by milestone M51 (T230–T235).

## 1. Problem

The desktop Hosts view ([HostsView.axaml](../../src/GUIClient/Views/HostsView.axaml)) is a
three-pane form whose internal regions cannot be resized: the vulnerabilities grid is pinned to
250 px and the comments panel to 170 px, so on a 1400 px-tall window the detail area is mostly
empty while the grid that users actually work in shows six rows. Services occupy a 150 px column
with one `name / Port: n` entry per two lines. The host already carries `Criticality` (1–5),
`Environment`, `Owner` and `RiskScore` columns (written by the Jira Assets and Trend Micro imports)
but the view shows none of them. Every save on a host is recorded (legacy `audit` blob table) and a
field-level trail exists for governance entities, yet no screen answers "what changed on this host
and who or what changed it". The list can be filtered by host name only, through a hidden search box.

## 2. Research summary

Full notes with sources are in §9. The patterns that drove the design:

- Every product with a rich asset page (Microsoft Defender, Rapid7 InsightVM, Wazuh, Snipe-IT,
  Lansweeper) uses a **header with name plus risk/criticality badges and tabs below**, never one
  long form. Defender's tab set is the closest analogue: Overview, Timeline, Discovered
  vulnerabilities, Inventories.
- Nielsen Norman Group: tabs beat accordions on desktop for a few long sections; put the highest-use
  tab first and selected by default; tab labels one or two words.
- Criticality scales differ by vendor (Tenable 1–10, Qualys 1–5, Rapid7 and Defender named levels).
  NetRisk already stores 1–5, so the UI exposes that scale with named labels and a coloured pill
  that always carries text (never colour alone).
- Change history (NetBox, Jira, ServiceNow): field, old value, new value, actor, timestamp, and a
  request/correlation id grouping one save. Jira's known weakness is an unfiltered list; Snipe-IT's
  is mutation paths that never log. NetRisk's interceptor already runs on every `SaveChanges`, so
  imports are covered for free.
- Apple HIG split views: dividers must drag, and panes need minimum sizes. Avalonia `GridSplitter`
  needs `ResizeDirection` matching the axis and `MinWidth`/`MinHeight` on the definitions it moves.
- Fluent 2: a read-only, system-generated value is a Badge, not a dismissible Tag. Service chips
  are badges.
- Enterprise table guidance: default sort "needs action first" (severity descending), freeze the
  header, move low-use columns out of the default set.

## 3. Target layout

```
┌──────────────┬─┬──────────────────────────────────────────────────────────────────┐
│ Hosts        │ │ ▌ WEBDC1VPR0120            ● Active   [Critical 5]  Produção     │
│ ┌──────────┐ │ │   10.156.124.57 · webdc1vpr0120.fgv.br · Windows Server 2019     │
│ │ search   │ │ │   Team INFRA-WEB-WINDOWS · Owner … · Risk 72                    │
│ └──────────┘ │ │   Open vulnerabilities  [C 2] [H 5] [M 11] [L 3]                 │
│ Status ▾     │ ├─┼──────────────────────────────────────────────────────────────────┤
│ Team   ▾     │ │ │ Vulnerabilities │ Overview │ Services │ History │ Comments      │
│ Critic ▾     │ │ ├──────────────────────────────────────────────────────────────────┤
│ Env    ▾     │ │ │                                                                │
│──────────────│ │ │   (tab content fills all remaining height)                     │
│ [5] WEBDC1…  │ │ │                                                                │
│ [3] 9JT001   │ │ │                                                                │
│ [·] WEBPR1   │ │ │                                                                │
│ …            │ │ │                                                                │
│ 1–100 of 412 │ │ │                                                                │
│ + ✎ ⟳ 🗑 ⇪    │ │ │                                                                │
└──────────────┴─┴──────────────────────────────────────────────────────────────────┘
```

### 3.1 Left pane: host list with facets

- The search box is **always visible** at the top of the pane (Ctrl+F focuses it). The 2-second
  debounce stays but drops to 500 ms.
- Below it a filter row of `ComboBox`es: **Status** (All / Active / Retired), **Team** (All + teams
  from `ITeamsService`), **Criticality** (All / 1–5 / Not set), **Environment** (All + distinct
  values returned by the server). Each change recomposes one server-side filter string
  (`hostName@=x,status==42,teamId==3,criticality==5,environment==Produção`) and reloads page 1.
- Each list row shows a criticality pill (or a neutral `–` pill when unset), host name in the
  primary line, and IP in a secondary muted line.
- Paging: the client reads `X-Total-Count`; a footer shows `first–last of total` with previous /
  next `Button.detailButton`s. Page size stays 100.
- The pane keeps its `GridSplitter`; its width is persisted per user through
  `IMutableConfigurationService` (key `hostsView.leftPaneWidth`), bounded by `MinWidth="220"` and
  `MaxWidth="520"`.
- Toolbar buttons are unchanged in function (add, edit, reload, delete, export) but gain
  `ToolTip.Tip` bound to localized strings; the magnifier button goes away because search is
  permanent.

### 3.2 Header card (always visible)

A `Border.card` with: host name (`TextBlock.title`), status icon + text, criticality pill,
environment badge, then one muted line `IP · FQDN · OS`, one line `Team · Owner · Risk score`, and a
row of four severity-count badges (Critical / High / Medium / Low) for **open** vulnerabilities,
fed by the new summary endpoint (§5.3). The card is `Auto`-height; it never scrolls.

### 3.3 Tabs (fill the remaining height)

`TabControl` with, in this order, **Vulnerabilities** (default), **Overview**, **Services**,
**History**, **Comments**. Selected tab is persisted (`hostsView.selectedTab`).

- **Vulnerabilities**: the existing DataGrid, now `*`-height, `CanUserSortColumns="True"`,
  `CanUserResizeColumns="True"`, frozen header (DataGrid default), default sort severity descending
  then last detection descending. Default visible columns: status icon, severity (text, with a
  severity class on the cell), title, score, last detection, fix team. `Id`, first detection,
  detection count and analyst stay in the grid but `IsVisible="False"` by default; a column
  chooser is out of scope (recorded in §8). Double-click opens the vulnerability in the
  Vulnerabilities view as today's navigation allows; if no such navigation exists it is a no-op
  and the row stays selectable.
- **Overview**: a two-column `Grid` with a vertical `GridSplitter` (`horizontalSplitter` class).
  Left: the label/value details (Id, host name, IP, FQDN, MAC, OS, OS version, status,
  registration date, last verification, source, external id/provider, team, owner, environment,
  criticality, risk score + source + updated at, free-text comment) using `form_label`/`form_text`
  classes. Right: service chips in a `WrapPanel` (§3.4).
- **Services**: full table (`DataGrid`): icon, name, protocol, port, vulnerability count on that
  service. Read-only; service CRUD stays in the edit dialog.
- **History**: see §5.4. Header row with filters (**Field** combo, **Actor** combo, **Since** date)
  and a list of entries grouped by `CorrelationId` (one save = one group with actor and timestamp),
  each field as `Field   old → new`. Empty state text when nothing is recorded.
- **Comments**: the existing comments list and composer, now `*`-height.

### 3.4 Service chips

A `Border.chip` pill (new style, neutral surface, 4 px gap, no truncation) containing a 14 px
`MaterialIcon` and `name · port`. Icon by well-known service name/port via a new
`ServiceToIconConverter` (pure, testable in `GUIClient.Tests`):

| Match | Icon kind |
|---|---|
| ssh, telnet, shell, port 22/23/514 | `Console` |
| http, https, www, http-alt, port 80/443/8080/8443 | `Web` |
| smtp, imap, pop3, port 25/143/110/465/587/993/995 | `Email` |
| dns, domain, port 53 | `Dns` |
| mysql, mariadb, mssql, postgres, mongodb, oracle, redis, port 3306/1433/5432/27017/1521/6379 | `Database` |
| rdp, vnc, ms-wbt-server, port 3389/5900 | `RemoteDesktop` |
| ldap, ldaps, kerberos, port 389/636/88 | `AccountKey` |
| smb, netbios, microsoft-ds, port 139/445 | `FolderNetwork` |
| snmp, ntp, port 161/123 | `Lan` |
| anything else, including `general` / port 0 | `Lan` |

### 3.5 Criticality

- Scale: the existing `hosts.criticality` 1–5 column. Labels: 1 Very low, 2 Low, 3 Medium, 4 High,
  5 Critical; `null` renders as "Not set".
- Pill: `Border.criticality` with modifier classes `c1`…`c5` and `unset`. Colours are **tokens**
  in `Styles/Tokens.axaml` (`NrCriticality1`…`NrCriticality5`, `NrCriticalityUnset`, each with a
  `…Text` foreground token meeting 4.5:1), referenced only from `WindowStyles.axaml`. The same
  five tokens back `Border.severity` + `s0`…`s4` for vulnerability severity (0 None, 1 Low,
  2 Medium, 3 High, 4 Critical), so there is one colour language for "how bad".
- Editable in the host edit dialog (`EditHostDialog`) via a `ComboBox` of six items, plus
  `Environment` and `Owner` text fields. Saved through the existing `PUT /Hosts/{id}`.
- Filterable (§5.1).

## 4. Data model

No new tables. Columns used, all pre-existing: `hosts.criticality`, `hosts.environment`,
`hosts.owner`, `hosts.risk_score`, `hosts.risk_score_source`, `hosts.risk_score_updated_at`,
`hosts.os_version`, `hosts.external_id`, `hosts.external_provider`, `hosts.last_verification_date`,
`hosts.source`; `audit_logs` for history. No EF migration, no numbered SQL, no `targetVersion`
bump.

## 5. Server and client changes

### 5.1 Filter whitelist

`API/ApplicationEntityFilterMapperProvider.cs` Host mapping gains `criticality`, `environment`,
`owner`, `source`, `riskScore`, `lastVerificationDate`. Test: the existing mapper test class gains
a case per field, and one asserting an unknown field is still rejected.

### 5.2 Environments facet

`GET /Hosts/Environments` returns the distinct non-empty `environment` values, ordered,
`[PermissionAuthorize("hosts")]`. `IHostsService.GetEnvironmentsAsync()` on both server and client.

### 5.3 Vulnerability severity summary

`GET /Hosts/{id}/VulnerabilitySummary` returns `HostVulnerabilitySummaryDto { HostId, Open:
{ Critical, High, Medium, Low, None }, Total }`, where *open* means the vulnerability status is not
in the closed set already used by the Vulnerabilities view (`IntStatus.Closed`, `Fixed`, `Rejected`,
`Retired`, … — use the same helper, do not invent a second definition). Severity is parsed from
`Vulnerability.Severity` ("0"–"4"); unparsable values count as None. One grouped query, no N+1.
A batch form `GET /Hosts/VulnerabilitySummary?ids=1|2|3` (same DTO list, max 500 ids) feeds the
list rows later if needed; it is **not** called by the list in this milestone (list rows show
criticality only) but ships with the single form so the API does not change twice.

### 5.4 Change history

- `GovernanceAuditInterceptor.AuditedTypes` gains `nameof(Host)` and `nameof(HostsService)`.
  `IgnoredFields` gains `nameof(Host.LastVerificationDate)` and `nameof(Host.RiskScoreUpdatedAt)`
  (every import touches them; they would drown the trail). The interceptor's class comment is
  updated to say why hosts are in and why those two fields are out.
- Actor: whatever the `AuditableContext.AuditActor` already yields (user name for API requests,
  `SystemActor` for jobs). Imports should set a descriptive actor where the context allows it
  (`Nessus import`, `Jira Assets import`, `Trend Micro import`); if the current context plumbing
  does not expose a setter on the import path, record that as a note on T234 rather than widening
  the task.
- `GET /Hosts/{id}/History?limit=500` on `HostsController` (policy `hosts`, not
  `RequireRiskmanagement`) delegating to `IAuditTrailService.GetForRecordAsync("Host", id, limit)`;
  entries for the host's `HostsService` rows are **not** merged in this milestone (note on T234).
  Returns the existing `AuditLogDto` shape.
- Client `IHostsService.GetHistoryAsync(int hostId, int limit = 500)`.
- Retention: the governance trail's existing retention applies unchanged.

### 5.5 Client paging

`HostsRestService.GetFilteredAsync` returns `(List<Host> items, int total)` by reading
`X-Total-Count`, mirroring `VulnerabilitiesRestService`. The existing single-list signature stays
as an overload so other callers do not change. Client-side sort by `HostName` is replaced by
sending `sorts=hostName`.

## 6. UI standard compliance (S1)

- No inline colours. The `ProgressRing Foreground="CornflowerBlue"` literal in the current view
  is replaced by a token reference while the file is rewritten. `IntStatusToColorConverter` is not
  used by the new view; status is icon + text via `IntStatusToMaterialIconkindConverter` with a
  `MaterialIcon.success/.warning` class.
- Every string in all three `Localization*.resx`, bound through `Str*` properties.
  `LocalizationCoverageTest` enforces it.
- Every `Classes` token defined (`StyleClassReferenceTest`); every new `Nr*` token referenced
  (`ThemeTokenLayerTests`).
- Buttons keep the `subButton` family in the list toolbar and `detailButton` for paging;
  comments keep `dialog1`.
- Keyboard: `GridSplitter`s are focusable and arrow-key movable (built in); tab headers, combos
  and the comment box are in tab order; Ctrl+F focuses search.

## 7. Implementation plan

Order matters: the backend pieces land first so the view is built against real endpoints.

| Task | Scope | Tests |
|---|---|---|
| T233 | Filter whitelist (§5.1), environments endpoint (§5.2), client `GetFilteredAsync` with total (§5.5) | mapper test cases; `HostsControllerTest` for `/Environments`; `HostsRestServiceTest` for total-count parsing |
| T235 | Severity summary endpoint, single + batch (§5.3) | `HostsServiceInMemoryTest` grouped counts incl. unparsable severity and closed statuses excluded; controller test; client test |
| T234 | Host in audit allowlist, ignored fields, `/History` endpoint, client method (§5.4) | interceptor test: Host edit writes rows, `LastVerificationDate` change writes none, `HostsService` row writes rows; controller test with `hosts` policy; client test |
| T232 | Tokens + `Border.criticality`/`Border.severity`/`Border.chip` styles; `CriticalityToLabelConverter`, `ServiceToIconConverter`; edit dialog fields (§3.5, §3.4) | converter tests in `GUIClient.Tests`; token/style tests stay green |
| T230 | Rewrite `HostsView.axaml` + `HostsViewModel`: header card, tabs, splitters, persistence, facets, paging (§3.1–3.3) | `HostsViewModel` filter-string composition test (pure method); `LintUi`; `GUIClient.Tests` view scans |
| T231 | Services chips on Overview + Services tab + History tab wiring (§3.3, §3.4) | included in T230's view scans; converter tests from T232 |

Each task updates `CHANGELOG.md` `[Unreleased]` and ticks itself in `ROADMAP.md`. The milestone is
done when `./build.sh LintUi`, `dotnet test src/netrisk.sln` (non-Docker) and the app opened on a
real database show the Hosts view with a resizable grid.

## 8. Explicitly out of scope

- Column chooser / row-density setting for the vulnerabilities grid.
- A computed host priority combining criticality and open severity (Rapid7-style multipliers).
  The data to do it now exists; it is a separate roadmap item.
- Merging `HostsService` audit rows into the host's History tab.
- Severity badges on every list row (batch endpoint ships, the list does not call it yet).
- Reading the legacy `audit` blob table; history starts when T234 ships.

## 9. Sources

- Microsoft Defender XDR device entity page (header indicators, tab set, filterable
  vulnerabilities table): https://learn.microsoft.com/en-us/defender-xdr/entity-page-device
- Defender device value Low/Normal/High: https://learn.microsoft.com/en-us/defender-vulnerability-management/tvm-assign-device-value
- Tenable ACR 1–10 banding and AES: https://docs.tenable.com/vulnerability-management/Content/Lumin/LuminMetrics.htm
- Qualys asset criticality 1–5: https://docs.qualys.com/en/cs/latest/container_assets/asset_criticality_score.htm
- Rapid7 InsightVM asset page and criticality multipliers: https://docs.rapid7.com/insightvm/locating-assets/ and https://docs.rapid7.com/insightvm/adjusting-risk-with-criticality/
- Wazuh per-agent tabbed inventory: https://documentation.wazuh.com/current/user-manual/capabilities/system-inventory/viewing-system-inventory-data.html
- NetBox object change model: https://netboxlabs.com/docs/netbox/models/core/objectchange/
- Snipe-IT missing-history failure mode: https://github.com/snipe/snipe-it/issues/14010
- NN/G tabs: https://www.nngroup.com/articles/tabs-used-right/ and accordions on desktop: https://www.nngroup.com/articles/accordions-on-desktop/
- NN/G data tables: https://www.nngroup.com/articles/data-tables/
- Fluent 2 Tag vs Badge: https://fluent2.microsoft.design/components/web/react/core/tag/usage
- Apple HIG split views: https://developers.apple.com/design/human-interface-guidelines/components/layout-and-organization/split-views/
- Avalonia GridSplitter: https://docs.avaloniaui.net/docs/reference/controls/gridsplitter ; persisting layout: https://github.com/AvaloniaUI/Avalonia/discussions/12259
- Enterprise table density and default sort: https://www.pencilandpaper.io/articles/ux-pattern-analysis-enterprise-data-tables

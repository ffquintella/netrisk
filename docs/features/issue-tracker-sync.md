# Issue-tracker synchronization

> Track 4 milestone 4.2. Extended by milestone 4.6 —
> [Jira Service Management & Assets](jira-service-management.md) — which widened these links from
> findings to findings, incidents and risks, and gave the field, status and priority mappings the
> editors they had been missing.

NetRisk creates and links developer tasks from vulnerability findings, and keeps the two in step in
both directions. The modular core means adding a fifth tracker is one renderer, not a redesign:
providers do transport and shape translation only, and every policy decision — severity mapping,
templates, when to auto-create, what a closed ticket means — lives on the connection.

```
finding ──► template + priority mapping ──► IIssueTrackerProvider ──► Jira / GitHub / GitLab / ADO
   ▲                                                                          │
   └────── status mapping ◄── webhook (validated) or 15-minute poll ◄──────────┘
```

## Providers

| Provider | Auth | Project | Priority | Transitions | Webhook authenticity |
|---|---|---|---|---|---|
| **Jira Cloud** (REST v3) | Basic — account email + API token | Project key (`SEC`) | Native field | By **transition id**, resolved from the issue's *available* transitions | Unsigned: shared secret in the receiver URL |
| **GitHub Issues** | Bearer PAT / App token | `owner/repo` | **None** — expressed as a `priority:` label | `open` / `closed` only | `X-Hub-Signature-256`, HMAC-SHA256 of the raw body |
| **GitLab Issues** | `PRIVATE-TOKEN` | Full path or numeric id | **None** — a `priority::` scoped label | `state_event` `close` / `reopen` | `X-Gitlab-Token`, compared in constant time |
| **Azure DevOps** | Basic — empty user + PAT | Project name | `Microsoft.VSTS.Common.Priority` | `System.State` via JSON Patch | Unsigned: shared secret in the receiver URL |

Details worth knowing because they are easy to get wrong:

* Jira v3 takes **Atlassian Document Format**, not a string; a plain string is rejected outright.
* GitLab addresses an issue by its per-project **`iid`**, not its global `id`. Using the id produces
  a 404 against a project that does have the issue.
* Azure DevOps answers an invalid PAT with **HTTP 203 and a sign-in page**, not a 401. The connection
  test recognises both that and an HTML body behind a 200.
* Jira labels cannot contain whitespace; the provider replaces spaces with hyphens.

## Field mapping

Per connection. **Editable since 4.6**: the status-mapping grid shipped read-only with no way to add a
row, and the templates and the priority mapping had no editor at all — the wholesale `PUT` behind them
existed from 4.2.1 and nothing called it. A Jira connection also gains a
[Jira-field mapping](jira-service-management.md#the-configurable-mappings) whose picker is the site's
own field list, so a custom field is chosen rather than typed from memory.

| Setting | Default |
|---|---|
| Title template | `[{{Severity}}] {{Title}}` |
| Description template | A table of severity, status, asset, component, location, CVE links, CVSS, first-seen and SLA due date, plus the description, evidence excerpt and a deep link |
| Priority mapping | Provider defaults — Jira `Highest…Low`; ADO `1…4` (**inverted**, because its scale is) |
| Default labels | none |

Templates use `{{Placeholder}}` substitution, not a template language. The values are
attacker-influenced finding text going into somebody else's tracker, and expressions in that position
would be a server-side injection surface for no benefit — nobody needs a loop in an issue title. An
unknown placeholder is left visible so a typo shows up in the preview rather than producing a ticket
with a hole in it.

Available placeholders: `FindingId`, `Title`, `Severity`, `RawSeverity`, `Status`, `Description`,
`Evidence`, `Asset`, `Component`, `Location`, `Cves`, `Cwes`, `Cvss`, `FirstDetection`, `SlaDueDate`,
`FixedInVersion`, `RuleId`, `Link`.

## Creating and linking

Since 4.6 a ticket may hang off a **finding, an incident or a risk**. Findings keep everything below —
the auto-create policy, the preview, the lifecycle actions and the conflict queue. For an incident or a
risk the link is a reference: the external status is mirrored and shown, and nothing transitions. See
[Links beyond findings](jira-service-management.md#links-beyond-findings).

* **Create issue** from a finding, or from a multi-selection. Per-finding failures are reported by
  absence rather than by failing the whole request — filing 39 of 40 tickets beats filing none.
* Creation is **idempotent per (connection, finding)**: a repeated call returns the existing link
  rather than filing a duplicate.
* **Link existing** by key or URL. The issue is read before the link is stored, because a link to an
  issue that does not exist fails silently on every later sync.
* **Policy mode**: a connection may auto-create for every new finding at or above a severity. Off by
  default — ticket-per-finding noise is the failure this milestone exists to avoid.

## Bi-directional sync

**Inbound.** A validated webhook (primary) or a per-connection poll (fallback, 15 minutes by default,
for instances that cannot reach NetRisk). The connection's status mapping decides what an external
status means:

| Action | Effect |
|---|---|
| `MarkMitigated` | Transition the finding to `Mitigated` |
| `ScheduleReverify` | Leave the finding open and request re-verification |
| `MarkFalsePositive` | Transition to `FalsePositive` |
| `Reactivate` | Transition back to `Active` |
| `None` | Record the change, do nothing |

The mapping names an **action**, not a destination status, because closing a ticket does not always
mean the finding is fixed. Teams that require a re-scan first choose `ScheduleReverify`; teams that do
not choose `MarkMitigated`. Both are legitimate policies.

A closed issue with no explicit mapping is treated as `MarkMitigated`. An operator who disagrees maps
that status to `None`.

Every applied transition is recorded with `source=IssueSync` and the tracker's status in the
justification, so the finding's timeline shows who closed it and why.

**Outbound.** A NetRisk transition posts a comment and, where the tracker supports it and the mapping
names one, an outbound transition.

**Loop protection.** A link whose last change came *from* the tracker is skipped on the next outbound
push. Without it, an inbound "Done" posts a comment that the tracker reports as a change, which comes
back in as another inbound sync.

**Conflicts.** When NetRisk has already moved a finding to a suppressed state and the tracker asks for
something different, last-writer-wins is applied *and the link is flagged*. The conflict queue is what
makes that visible instead of a finding that appears to have changed direction on its own.

## Webhook endpoints

```
POST /IssueSyncWebhooks/{connectionId}?secret=<shared secret>
```

Anonymous, because the caller is GitHub or Jira. Authenticity rests entirely on the per-connection
secret: a signature for GitHub and GitLab, the query parameter for Jira and Azure DevOps, which cannot
sign. Verification happens **before** the payload is acted on; a body that does not verify is answered
401 and never reaches the sync logic.

The raw body is read as a string rather than model-bound — an HMAC is computed over the exact bytes
that arrived, and re-serializing a bound model produces different bytes and a signature that never
matches.

## Known limitations

* Jira and Azure DevOps cannot sign a webhook body, so their authenticity check is a URL secret. Serve
  the receiver over HTTPS.
* GitHub and GitLab have no priority field; the mapped priority becomes a label.
* `ScheduleReverify` raises the `issuesync.applied` notification but does not itself create a task.

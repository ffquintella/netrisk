# Notification channels

> Track 4 milestone 4.1. Payload schema version: `1`.

NetRisk broadcasts domain events to the places security and engineering teams already work. The
design has three parts and one rule: **a notification failure never fails the operation that raised
it**. Creating a Critical risk has to succeed even when Slack is down.

```
domain event ──► INotificationEventPublisher ──► dispatcher (match, retry, fallback, digest)
                                                      │
                                                      ├──► Email    (HTML + plaintext)
                                                      ├──► Slack    (Block Kit)
                                                      ├──► Teams    (Adaptive Card)
                                                      └──► Webhook  (documented JSON + HMAC)
```

## Event catalog

| Event | Raised when | Severity filter | Digest recommended |
|---|---|---|---|
| `risk.created` | A risk is recorded | yes | no |
| `risk.severity_changed` | A risk's score band moves | yes | no |
| `vulnerability.imported` | A scanner import completes | yes | **yes** |
| `finding.status_changed` | A finding moves through triage | yes | **yes** |
| `sla.approaching` | A finding nears its deadline | yes | **yes** |
| `sla.breached` | A finding passes its deadline | yes | no |
| `incident.created` | An incident is opened | — | no |
| `irp.task_assigned` | An IRP task is assigned | — | **yes** |
| `riskacceptance.expiring` | An acceptance nears expiry | — | **yes** |
| `issuesync.applied` | A tracker changed a finding | — | **yes** |

An event that has no severity **passes** a severity filter rather than failing it. An incident has no
severity band, and a subscriber asking for "Critical and above" should still hear about incidents —
the alternative is discovering months later that incident alerts never fired.

## Providers

| Provider | Configuration | Rendering | Notes |
|---|---|---|---|
| **Email** | `recipients`, `subjectPrefix` | Inline-styled HTML + plaintext alternative | Per-recipient; one bad address does not stop the rest. |
| **Slack** | `webhookUrl` | Block Kit inside a severity-coloured attachment | Honours HTTP 429 `Retry-After`. Fields chunk at ten per section. |
| **Teams** | `webhookUrl` (Workflows) | Adaptive Card 1.4 in an `attachments[].content` envelope | **Not** the retired O365 `MessageCard`. Answers 202. |
| **Webhook** | `webhookUrl`, `signingSecret`, `headers` | Documented JSON | Signs with HMAC-SHA256 — see below. |

A Slack incoming webhook created today is bound to the channel chosen at creation; the `channel`
override is ignored, and the test button says so rather than reporting a bare success.

## Webhook payload and signature

```json
{
  "schemaVersion": "1",
  "event": "sla.breached",
  "occurredAt": "2026-08-25T09:30:00.0000000Z",
  "title": "SLA breached: SQL injection on /billing",
  "body": "The finding passed its remediation deadline.",
  "severity": 4,
  "severityLabel": "Critical",
  "link": "https://netrisk.acme.com/vulnerabilities/42",
  "aggregatedCount": 1,
  "subject": { "type": "finding", "id": 42, "entityId": 7 },
  "fields": { "Finding": "#42", "Severity": "Critical", "Asset": "db-prod-01" }
}
```

Headers:

| Header | Value |
|---|---|
| `X-NetRisk-Event` | The event name, so a receiver can route without parsing the body |
| `X-NetRisk-Timestamp` | Unix seconds |
| `X-NetRisk-Signature` | `sha256=<hex>` — HMAC-SHA256 over `"{timestamp}.{body}"` |

Verify it the same way GitHub or Stripe signatures are verified:

```python
expected = "sha256=" + hmac.new(secret.encode(), f"{timestamp}.{body}".encode(), hashlib.sha256).hexdigest()
hmac.compare_digest(expected, presented)   # constant time, always
```

The timestamp is **inside** the signed string. Signing only the body would let a captured request be
replayed forever with no way for the receiver to notice.

A channel with no signing secret still delivers, but the receiver cannot tell a NetRisk alert from a
forged one. The test button says so.

## Delivery: retry, fallback and digests

* **Immediate** for a subscription with no digest window: the send is attempted inline when the event
  is raised, so "new Critical risk → Slack" lands within seconds.
* **Retry** on a retryable failure (0, 408, 429, 5xx) up to three attempts, with a quadratic backoff
  of 1, 4 and 9 minutes. A 400 or a 403 is a configuration problem and is *not* retried — retrying it
  only delays the operator finding out.
* **Fallback** once the primary is out of attempts or has failed permanently: the dispatcher walks the
  channel's fallback chain to the first enabled channel. It does **not** fall back on the first
  transient blip, which would double-notify on every Slack hiccup. The primary's error is kept on the
  delivery row.
* **Digest** for a subscription with a window: matching events queue, and the sweep sends one summary
  when the window closes. The digest takes the *highest* severity of what it summarises — one that
  reads as Medium while containing a Critical is worse than no digest.

The `NotificationDispatch` job runs every minute and owns retries, digest windows, and a daily purge
of delivery rows older than 90 days.

## Delivery log

Every attempt is a row in `notification_deliveries`: status, attempt count, last error, the rendered
title and the payload. "The SLA breach fired — did the team hear about it?" cannot be answered from
the absence of a Slack message, which is why this is a table and not a log line.

Provider errors are **redacted** before they are written: Slack webhook URLs and anything
token-shaped are replaced, because provider error bodies have been known to echo the credential back
and the delivery log is readable by anyone who can administer notifications.

A failed delivery can be re-queued from the admin UI (it resets to three fresh attempts). A delivered
one cannot — that button would duplicate the alert.

## Credentials

Webhook URLs, signing secrets and custom headers are **encrypted at rest** with a key derived from the
installation's server secret. No endpoint returns them: reads replace them with `••••••••`, and a
write that sends the placeholder back keeps the stored value. That is what lets the admin form
round-trip without the client ever holding a token.

## Configuration keys

| Key | Purpose |
|---|---|
| `app:baseUrl` | Root the "Open in NetRisk" deep links are built from. Without it, notifications have no button rather than a broken one. |

## Known limitations

* A digest window is closed by the once-a-minute sweep, so the window's resolution is a minute.
* Slack's `channel` override applies only to legacy webhooks.
* The generic webhook is one-way; NetRisk does not receive notification acknowledgements.

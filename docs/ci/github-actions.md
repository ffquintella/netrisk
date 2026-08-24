# GitHub Actions

Scan, upload, gate. Actions are pinned to a commit SHA rather than a tag — a tag can be moved, which
makes it a supply-chain risk in a workflow that handles a credential.

```yaml
name: Security scan

on:
  pull_request:
  push:
    branches: [main]

permissions:
  contents: read

jobs:
  scan:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683 # v4.2.2

      - name: Run Trivy
        uses: aquasecurity/trivy-action@18f2510ee396bbf400402947b394f2dd8c87dbb0 # v0.29.0
        with:
          scan-type: fs
          format: json
          output: trivy-results.json
          severity: CRITICAL,HIGH,MEDIUM

      - name: Upload to NetRisk
        id: upload
        env:
          # A repository or environment secret. Never a literal, and never in the repository.
          NETRISK_TOKEN: ${{ secrets.NETRISK_TOKEN }}
          NETRISK_URL: ${{ vars.NETRISK_URL }}
        run: |
          set -euo pipefail

          response=$(curl --fail --silent --show-error \
            -X POST "$NETRISK_URL/Vulnerabilities/import/trivy" \
            -H "Authorization: Bearer $NETRISK_TOKEN" \
            -H "Content-Type: application/json" \
            -H "Idempotency-Key: ${{ github.run_id }}-${{ github.run_attempt }}-trivy" \
            --data-binary @trivy-results.json)

          echo "import_id=$(echo "$response" | jq -r '.importId')" >> "$GITHUB_OUTPUT"

      - name: Gate on new criticals
        env:
          NETRISK_TOKEN: ${{ secrets.NETRISK_TOKEN }}
          NETRISK_URL: ${{ vars.NETRISK_URL }}
        run: |
          netrisk-console ci gate \
            --job "${{ steps.upload.outputs.import_id }}" \
            --fail-on new-critical
```

## Notes

**The idempotency key includes `run_attempt`.** Re-running a *failed* job is a genuinely new attempt
and should import; a retried *step* within one attempt should not. Including the attempt number gets
both right. Drop it if you would rather a re-run never re-import.

**`permissions: contents: read`** is least privilege for this workflow. Raise it only for what the
scanner itself needs.

**Pull requests from forks do not receive secrets.** If you want fork PRs scanned, run the scan on
`pull_request` and the upload on `workflow_run`, so the credential is never exposed to code the fork
controls.

## Gating on a schedule instead

For a nightly scan you usually want the register updated but the job green:

```yaml
      - name: Import without gating
        run: netrisk-console ci gate --job "$IMPORT_ID" --fail-on none
```

The SLA breach policy is the useful middle ground — it fails only when something the scan found is
already past its remediation deadline:

```yaml
      - name: Fail on SLA breaches only
        run: netrisk-console ci gate --job "$IMPORT_ID" --fail-on sla-breach
```

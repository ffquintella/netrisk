# Azure Pipelines

```yaml
trigger:
  branches:
    include: [main]

pool:
  vmImage: ubuntu-latest

variables:
  # NETRISK_URL is an ordinary variable. NETRISK_TOKEN comes from a variable group backed by Azure
  # Key Vault, or is marked secret in the pipeline's variables — never inline in this file.
  - group: netrisk-credentials

steps:
  - checkout: self

  - script: |
      set -euo pipefail
      curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh \
        | sh -s -- -b /usr/local/bin v0.58.1
      trivy fs --format json --output "$(Build.ArtifactStagingDirectory)/trivy-results.json" \
        --severity CRITICAL,HIGH,MEDIUM .
    displayName: Run Trivy

  - script: |
      set -euo pipefail

      response=$(curl --fail --silent --show-error \
        -X POST "$(NETRISK_URL)/Vulnerabilities/import/trivy" \
        -H "Authorization: Bearer $(NETRISK_TOKEN)" \
        -H "Content-Type: application/json" \
        -H "Idempotency-Key: $(Build.BuildId)-$(System.JobAttempt)-trivy" \
        --data-binary @"$(Build.ArtifactStagingDirectory)/trivy-results.json")

      import_id=$(echo "$response" | jq -r '.importId')
      echo "##vso[task.setvariable variable=importId]$import_id"
    displayName: Upload to NetRisk
    env:
      # Secret variables are not exposed to the script environment automatically; mapping it
      # explicitly is required and is also the documented practice.
      NETRISK_TOKEN: $(NETRISK_TOKEN)

  - script: |
      netrisk-console ci gate --job "$(importId)" --fail-on new-critical
    displayName: Gate on new criticals
```

## Notes

**Secret variables must be mapped into `env:` explicitly.** Azure does not put them in the script
environment on its own; a script that reads `$NETRISK_TOKEN` without the mapping silently sees an
empty string, and the upload fails with a 401 that looks like a token problem rather than a
configuration one.

**`System.JobAttempt` in the idempotency key** distinguishes a re-run of a failed job (which should
import) from a repeated request inside one attempt (which should not).

**Prefer a variable group backed by Key Vault** over a pipeline-level secret variable: it gives the
token one place to be rotated and an access log.

## Gating a pull request only

```yaml
  - script: |
      if [ "$(Build.Reason)" = "PullRequest" ]; then
        netrisk-console ci gate --job "$(importId)" --fail-on new-critical
      else
        netrisk-console ci gate --job "$(importId)" --fail-on sla-breach
      fi
    displayName: Gate
```

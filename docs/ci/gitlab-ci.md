# GitLab CI

```yaml
stages: [scan, gate]

variables:
  # Set NETRISK_URL as a project CI/CD variable.
  # NETRISK_TOKEN must be *masked* and *protected* so it is neither printed in a job log nor
  # exposed to an unprotected branch.
  TRIVY_VERSION: "0.58.1"

security:scan:
  stage: scan
  image: aquasec/trivy:${TRIVY_VERSION}
  script:
    - trivy fs --format json --output trivy-results.json --severity CRITICAL,HIGH,MEDIUM .
  artifacts:
    paths: [trivy-results.json]
    expire_in: 1 day

security:upload:
  stage: gate
  image: curlimages/curl:8.11.1
  needs: [security:scan]
  script:
    - |
      set -eu
      response=$(curl --fail --silent --show-error \
        -X POST "$NETRISK_URL/Vulnerabilities/import/trivy" \
        -H "Authorization: Bearer $NETRISK_TOKEN" \
        -H "Content-Type: application/json" \
        -H "Idempotency-Key: ${CI_PIPELINE_ID}-${CI_JOB_ID}-trivy" \
        --data-binary @trivy-results.json)
      echo "IMPORT_ID=$(echo "$response" | sed -n 's/.*"importId":\([0-9]*\).*/\1/p')" > import.env
  artifacts:
    reports:
      dotenv: import.env

security:gate:
  stage: gate
  needs: [security:upload]
  script:
    - netrisk-console ci gate --job "$IMPORT_ID" --fail-on new-critical
```

## Notes

**Mask *and* protect the token variable.** Masking keeps it out of job logs; protecting keeps it off
unprotected branches, where anyone who can open a merge request can run a pipeline.

**The idempotency key uses `CI_JOB_ID`, not `CI_PIPELINE_ID` alone.** GitLab's retry of a job creates
a new job id, so a retried upload after a genuine failure re-imports, while a repeated request within
one job does not.

**Passing the import id between jobs** uses a `dotenv` report, which is GitLab's own mechanism for
it — writing it to a file artifact and re-reading it works too but is more moving parts.

## Merge-request-only gating

To let the nightly pipeline import without failing while merge requests are gated:

```yaml
security:gate:
  script:
    - |
      if [ "$CI_PIPELINE_SOURCE" = "merge_request_event" ]; then
        netrisk-console ci gate --job "$IMPORT_ID" --fail-on new-critical
      else
        netrisk-console ci gate --job "$IMPORT_ID" --fail-on sla-breach
      fi
```

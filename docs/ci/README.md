# CI/CD integration

> Track 3 (ASPM) milestone 3.5. Copy-pasteable recipes for GitHub Actions, GitLab CI and Azure
> Pipelines live beside this file.

The shape is the same on every platform:

```
run the scanner  ──►  upload the report to NetRisk  ──►  gate the build on what it found
```

## 1. Issue a token

**Administration → API tokens → Issue token.** Grant `vulnerabilities:import`, and
`vulnerabilities:read` as well if the pipeline will poll for results.

The token is shown **once**. It is stored hashed and there is no endpoint that can display it again;
if it is lost, revoke it and issue another.

Tokens start with `nrk_`, which makes a leaked one grep-able by secret scanners. Store it the way
your platform intends — GitHub encrypted secrets, GitLab masked variables, Azure variable groups —
and never in the repository.

A token acts as the user it was issued for and can never do more than that person could; its scopes
narrow that down further. **Administrator privileges are deliberately not granted through a token**,
even when the user holds them.

## 2. Upload

One call, no separate upload step:

```bash
curl --fail --silent --show-error \
  -X POST "$NETRISK_URL/Vulnerabilities/import/trivy" \
  -H "Authorization: Bearer $NETRISK_TOKEN" \
  -H "Idempotency-Key: $CI_PIPELINE_ID-trivy" \
  -H "Content-Type: application/json" \
  --data-binary @trivy-results.json
```

The response carries the import id to poll:

```json
{ "jobId": 84, "importId": 512, "isReplay": false, "success": true, "message": "Import started" }
```

Three things worth knowing:

- **`Idempotency-Key` makes a retry harmless.** A repeated key returns the original import instead of
  importing again, which is what protects the register from a CI retry storm. Use something stable
  per pipeline run and per scanner.
- **Imports are asynchronous by default.** A 500 MB scan file is ordinary; a synchronous endpoint for
  it is a timeout waiting to happen. Add `?wait=true` for small payloads to get the final counts in
  the response.
- **Use `auto` if you would rather not name the format.** `POST /Vulnerabilities/import/auto`
  identifies it from the file's content.

## 3. Gate

```bash
netrisk-console ci gate --job "$IMPORT_ID" --fail-on new-critical
```

Exit codes: **0** pass, **2** policy violation, **1** usage error or unknown import.

| Policy | Fails when |
|---|---|
| `new-critical` | The import created any new Critical finding. |
| `new-high`, `new-medium`, `new-low` | Same, for that band **and worse**. |
| `any-high>5` | It created more than five new High-or-worse findings. |
| `sla-breach` | A finding it created is already past its remediation deadline. |
| `none` | Never — for a pipeline that only reports. |

`--json` prints the decision as JSON instead of a table, for a pipeline that wants to parse it.

**New vs pre-existing is what makes gating non-flaky.** `new-*` reads the import's own counts, which
come from the deduplication engine, so a build does not fail for a vulnerability that was already
known and accepted. A finding suppressed as a false positive or covered by a risk acceptance is not
"new" on the next scan.

An import that **failed** fails the gate whatever the policy, `none` included: nobody can claim a
build is clean when the scan results never landed.

## Recipes

- [GitHub Actions](github-actions.md)
- [GitLab CI](gitlab-ci.md)
- [Azure Pipelines](azure-pipelines.md)

## Endpoint reference

| Endpoint | Scope | Purpose |
|---|---|---|
| `GET /Vulnerabilities/importers` | `vulnerabilities:read` or `:import` | Available importers. |
| `POST /Vulnerabilities/import/{importer}` | `vulnerabilities:import` | Upload a raw report. |
| `POST /Vulnerabilities/import/{importer}/{fileId}` | `vulnerabilities:import` | Import an uploaded file. |
| `GET /Vulnerabilities/import-jobs/{id}` | `vulnerabilities:read` or `:import` | Status and counts. |
| `GET /Vulnerabilities/sla/compliance` | `vulnerabilities:read` | SLA compliance by severity. |

A token missing the required scope gets **403** with the scope it needed named in the body — so a
pipeline author debugging it does not have to read the source to find out which one to add.

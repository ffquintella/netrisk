# Comment and evidence per answer in an assessment run

> GitHub #80 · task T297 · spec S44 · schema `db_version` 92

## Problem

In *Levantamento → Execuções de Avaliações*, a run answers every question of an assessment, but the
only free text a run carried was one `assessment_runs.Comments` for the whole run, and nothing could
be attached to it. An assessor answering question ALFA of assessment FONETIC had nowhere to say *why*
and nothing to show for it, and the same for BRAVO. The issue asks for a comment and an evidence file
or image on **each** answer.

## Decisions

| # | Decision | Rejected alternative |
|---|---|---|
| D1 | The comment is a column, `assessment_run_answers.comment` (`TEXT NULL`), on the row the paged run viewer already writes for each (run, question). Bounded at 4 000 characters by the API (`AssessmentEvidencePolicy.MaxCommentLength`). | A separate comments table: the issue asks for one comment per answer, not a thread, and a second table would need its own scope filter. The legacy `assessment_runs_answers` is not used: only the old flat dialog writes it, and nothing answers through that dialog any more. |
| D2 | Evidence is an `nr_files` row with a new nullable FK `assessment_run_answer_id` → `assessment_run_answers(id)` `ON DELETE CASCADE` (`fk_nr_files_assessment_run_answer_id`, `idx_nr_files_assessment_run_answer_id`) — the one-nullable-FK-per-attachment-target pattern `risk_acceptance_id` and `incident_id` already follow. Up to 10 files per answer. | A join table, or `run_id` + `question_id` on `nr_files`: the answer row already is the (run, question) pair, and cascading from it means deleting a run takes its evidence with it. |
| D3 | Saving a comment or evidence creates the answer row when the question has not been answered yet, with `answer_content_json` left `NULL`. Such a row reads as **unanswered** everywhere: the viewer ignores an empty draft and the conditional-visibility rules treat `NULL` as no answer. | Requiring an answer first: an assessor often writes the observation before choosing the option. |
| D4 | Writes go through dedicated endpoints under `/Assessments/runs/{runId}`, gated by `RequireAssessmentAccess` (the `assessments` permission). The generic `/Files` write routes (`POST /Files`, `POST /Files/local/complete`, `PUT` and `DELETE /Files/{name}`) refuse a file that is, or would become, assessment evidence with 400 `assessment_evidence_route`, so none of the checks below can be bypassed. Downloads stay on `GET /Files/{name}`, where `FileAccessAuthorizer` now maps evidence to the `assessments` permission after confirming the answer is visible in the caller's entity scope. | Reusing `POST /Files/local/complete` with the new FK: it has no notion of a run being submitted, of a per-answer limit or of the evidence size cap. |
| D5 | Upload validation, server-side: the staged size is checked **before** the chunks are reassembled (≤ 20 MiB, `AssessmentEvidencePolicy.MaxEvidenceBytes`; an empty file is refused), the declared type must be a row of `file_types` (the product's allowlist), the name is reduced to its last path segment with control characters removed and cut to the 100-character column, the upload id must be one safe path segment (`SafePathTool`), the run must exist in the caller's entity scope and the question must belong to the run's assessment. | Trusting the client's pre-checks: the desktop client checks size and count before uploading so the user gets a clear message, but those checks are a convenience. |
| D6 | A **submitted** run is read-only for comments and evidence (409 `run_submitted`), mirroring the viewer, which already disables answering a submitted run. Evidence is deleted only by its uploader or an administrator (403 otherwise), the same rule `DELETE /Files/{name}` applies. | Allowing evidence to change after submission: the evidence is what the submitted answers rest on. |
| D7 | The file is stamped with the **assessment's** `entity_id` (`FilesService.ResolveEntityId`), the entity the `assessment_runs` and `assessment_run_answers` scope filters already follow. | The run's `EntityId`: that is the entity being *assessed*, not the scope the assessment belongs to. |

Logs carry ids only — run, question, file id, user id — never the comment text or the file name,
which can carry personal data.

## API

| Verb | Route | Result |
|---|---|---|
| PUT | `/Assessments/runs/{runId}/questions/{questionId}/comment` | 200 the answer row · 400 too long · 404 run or question · 409 `run_submitted` |
| GET | `/Assessments/runs/{runId}/evidence` | 200 every evidence file of the run, with its `questionId` · 404 run |
| POST | `/Assessments/runs/{runId}/questions/{questionId}/evidence` | completes a chunked upload staged through `POST /Files/local/chunk`: 201 the evidence · 400 invalid name, type, size or upload id · 404 · 409 `run_submitted` / `evidence_limit_reached` |
| DELETE | `/Assessments/runs/{runId}/questions/{questionId}/evidence/{uniqueName}` | 200 · 403 not the uploader · 404 not this answer's · 409 `run_submitted` |

## Desktop

Each question card of the run viewer gains a comment box (auto-saved with the same two-second
debounce as the answer) and an evidence list with *Attach evidence*, download and delete. Both are
read-only on a submitted run; the evidence controls are hidden in preview, which has no run.

## Code

- Schema: `DAL/Context/NRDbContext.AssessmentEvidence.cs`, migration `AssessmentAnswerEvidence`,
  `ConsoleClient/DB/Structure/92.sql` + `Data/92.sql`.
- Rules: `Model/Assessments/AssessmentEvidencePolicy.cs`; service
  `ServerServices/Services/AssessmentRunEvidenceService.cs`; endpoints
  `API/Controllers/AssessmentRunEvidenceController.cs`; client
  `ClientServices/Services/AssessmentEvidenceRestService.cs`; view
  `GUIClient/Views/Assessments/AssessmentRunViewer.axaml`.
- Tests: `AssessmentRunEvidenceServiceInMemoryTest`, `AssessmentEvidenceFilesTest` (ServerServices.Tests),
  `AssessmentRunEvidenceControllerTest` and `FilesControllerAssessmentEvidenceTest` (API.Tests),
  `AssessmentEvidenceRestServiceTest` (ClientServices.Tests), `AssessmentEvidenceSummaryTest`
  (GUIClient.Tests), `AssessmentAnswerEvidenceSchemaTests` (DAL.IntegrationTests, Docker).

## Not covered

- The declared file type is the client's word for it; the content is not sniffed. A file is stored as
  a blob and never executed by the server, and the client saves a download under the extension of its
  declared type.
- The legacy *answers* grid under the runs list still reads `assessment_runs_answers` and shows
  neither the comment nor the evidence.

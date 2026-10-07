# Assessments

Questionnaire-style evaluations reused across multiple runs. Each assessment has questions, possible answers, and scoring rules; answers can be tied to assets or asset groups.

## Key Model Classes

- [Assessment.cs](../../src/DAL/Entities/Assessment.cs) — template
- [AssessmentQuestion.cs](../../src/DAL/Entities/AssessmentQuestion.cs)
- [AssessmentAnswer.cs](../../src/DAL/Entities/AssessmentAnswer.cs)
- [AssessmentRun.cs](../../src/DAL/Entities/AssessmentRun.cs) — execution instance
- [AssessmentRunsAnswer.cs](../../src/DAL/Entities/AssessmentRunsAnswer.cs) — response per run
- [AssessmentScoring.cs](../../src/DAL/Entities/AssessmentScoring.cs)
- [AssessmentRunDto.cs](../../src/Model/DTO/AssessmentRunDto.cs), [AssessmentRunsAnswerDto.cs](../../src/Model/DTO/AssessmentRunsAnswerDto.cs)
- [AssessmentStatus.cs](../../src/Model/Assessments/AssessmentStatus.cs)

## Server Service

[`IAssessmentsService`](../../src/ServerServices/Interfaces/IAssessmentsService.cs):

- Templates: `List`, `Get`, `Create`, `Update`, `Delete`
- Questions / answers: `GetQuestions`, `GetQuestion`/`ById`, `SaveQuestion`, `DeleteQuestion`, `GetAnswers`, `GetQuestionAnswers`, `GetAnswer`/`ById`, `SaveAnswer`, `DeleteAnswer`
- Runs: `GetRuns`, `GetRun`, `CreateRun` (from assessment or DTO), `UpdateRun`, `DeleteRun`
- Run answers: `GetRunsAnswers`, `CreateRunAnswer`, `DeleteRunAnswer`, `DeleteAllRunAnswer`

## API

[`AssessmentsController`](../../src/API/Controllers/AssessmentsController.cs) — requires `RequireValidUser`.

| Verb | Route | Purpose |
|------|-------|---------|
| GET/POST/PUT/DELETE | `/assessments[/{id}]` | Template CRUD |
| GET | `/assessments/{id}/Questions` | Questions |
| GET | `/assessments/{id}/Answers` | Answer options |
| GET/POST | `/assessments/{id}/Runs` | List / create runs |
| GET/PUT/DELETE | `/assessments/{id}/Runs/{runId}` | Manage run |
| GET/POST/DELETE | `/assessments/{id}/Runs/{runId}/Answers` | Run responses |

### Comment and evidence per answer

Each answer of a run can carry a comment and up to 10 evidence files (GitHub #80, T297) — design and
limits in [assessment-answer-evidence.md](assessment-answer-evidence.md) (S44).
[`AssessmentRunEvidenceController`](../../src/API/Controllers/AssessmentRunEvidenceController.cs),
same `RequireAssessmentAccess` policy:

| Verb | Route | Purpose |
|------|-------|---------|
| PUT | `/Assessments/runs/{runId}/questions/{questionId}/comment` | Set or clear the comment |
| GET | `/Assessments/runs/{runId}/evidence` | Every evidence file of the run |
| POST | `/Assessments/runs/{runId}/questions/{questionId}/evidence` | Complete a chunked upload as evidence |
| DELETE | `/Assessments/runs/{runId}/questions/{questionId}/evidence/{uniqueName}` | Remove one file (uploader or admin) |

## Client

[`AssessmentsRestService`](../../src/ClientServices/Services/AssessmentsRestService.cs);
[`AssessmentEvidenceRestService`](../../src/ClientServices/Services/AssessmentEvidenceRestService.cs) for
the comment and evidence of each answer.

## Capabilities

- Reusable templates executed as many runs
- Scoring rules per assessment
- Asset- and group-scoped answers (`AssessmentAnswersToAsset` / `Group`)
- Run history for longitudinal comparison

## Tests

- `AssessmentsControllerTest` (API.Tests)
- `AssessmentsServiceTest` (ServerServices.Tests)
- Comment and evidence: `AssessmentRunEvidenceControllerTest`, `FilesControllerAssessmentEvidenceTest`
  (API.Tests); `AssessmentRunEvidenceServiceInMemoryTest`, `AssessmentEvidenceFilesTest`
  (ServerServices.Tests); `AssessmentEvidenceRestServiceTest` (ClientServices.Tests);
  `AssessmentEvidenceSummaryTest` (GUIClient.Tests); `AssessmentAnswerEvidenceSchemaTests`
  (DAL.IntegrationTests)

## Common Exceptions

`DataNotFoundException`, `DataAlreadyExistsException`, `InvalidParameterException`

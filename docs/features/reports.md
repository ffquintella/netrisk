# Reports

User-generated reports over risks, vulnerabilities, incidents, and assessments. Reports are persisted with their parameters so they can be regenerated.

## Key Model Classes

- [Report.cs](../../src/DAL/Entities/Report.cs)
- [ReportParameters.cs](../../src/Model/Reports/ReportParameters.cs)

## Server Service

[`IReportsService`](../../src/ServerServices/Interfaces/IReportsService.cs): `GetAll`, `CreateAsync`, `Delete`.

Related statistics data is provided by [`IStatisticsService`](../../src/ServerServices/Interfaces) (risk, vulnerability, and control aggregates) and used as input by reports.

PDF generation uses QuestPDF: the hand-written reports derive from [`TemplatedPdfReport`](../../src/ServerServices/Reports/TemplatedPdfReport.cs) and render in the bundled Arial Unicode MS faces under `ServerServices/Fonts`, and the template-driven reports and exports go through [`QuestPdfRenderingService`](../../src/ServerServices/Services/QuestPdfRenderingService.cs).

## API

[`ReportsController`](../../src/API/Controllers/ReportsController.cs):

| Verb | Route | Purpose |
|------|-------|---------|
| GET | `/reports` | List |
| POST | `/reports` | Create |
| DELETE | `/reports/{id}` | Delete |

## Client

[`ReportsRestService`](../../src/ClientServices/Services/ReportsRestService.cs).

## Capabilities

- Persisted report definitions with parameters
- Backed by shared `StatisticsService` for aggregates
- PDF output via QuestPDF
- Scheduled exports and custom templates are on the [roadmap](../../ROADMAP.md)

## Tests

No dedicated test file in the current suite.

## Common Exceptions

`DataNotFoundException`

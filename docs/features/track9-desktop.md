# MIGR-TI/IA desktop workspace

The **Risk governance** navigation button opens a reusable desktop workspace. Its sections are
created and loaded when selected. The API's read permissions determine which sections appear;
users who maintain only suppliers, the data catalogue, or AI models can open their respective
register without receiving access to the risk register.

| Task | Desktop workflow | Main implementation |
|---|---|---|
| T303 | Flags and decisions on the risk, flag filter and summaries in the register, Top risks | `Track9RiskFlagsViewModels`, `RiskViewModel` |
| T304 | Treatment option, monetary estimates, prerequisites and Gate C; risk targets; task acceptance criteria/evidence; Gate D portfolio | `Track9TreatmentViewModels`, `EditMitigationViewModel`, `RiskGovernanceViewModel` |
| T305 | Loss components, P95/CVaR and Gate B, correlations, appetite tail tolerances and portfolio aggregation | `Track9TailViewModels`, `GovernanceAdminViewModel` |
| T306 | KRI definitions, readings, voiding and links; reassessment declarations and queue; M1–M10 | `KriRegisterViewModel`, `RiskMonitoringPanelViewModel`, `ReassessmentQueueViewModel`, `MethodologyMetricsViewModel` |
| T307 | Archival conditions, quarterly reviews and reopening; incident backtesting; committee membership, submissions and voting | `ArchiveReviewViewModel`, `BacktestingViewModel`, `RiskCommitteesViewModel` |
| T308 | Supplier contracts, links, sub-processors, locations, HECVAT and SBOM; concentration; entity block | `ThirdPartyRegisterViewModel`, `ThirdPartyConcentrationViewModel` |
| T309 | Data catalogue and findings, purposes/legal bases, retention/transfer/location, legal requirements, RIPD approval and retirement | `DataCatalogueViewModel`, `LegalRequirementsViewModel`, `DpiaViewModel` |
| T310 | Model inventory, version-specific evaluation, readings and overrides with voiding, findings and risk links | `AiModelInventoryViewModel`, `RiskAiModelsBlockViewModel` |

View-models are under `src/GUIClient/ViewModels/Track9`; matching views are under
`src/GUIClient/Views/Track9`. `Track9WorkspaceViewModel` and `Track9WorkspaceWindow` provide the
navigation host. The risk's **Risk governance** expander loads contextual blocks when opened.
Supplier/data blocks are also reachable from entity details. Treatment economics require an
existing mitigation: save a newly created mitigation before reopening it to edit economics.

## Editing and authority

The forms call the existing `ClientServices` interfaces. Monetary calculations, derivation of
flags, appetite checks, scope, segregation of duties, committee voting and state transitions remain
server operations. The desktop does not implement another decision engine.

The third-line assurance permission takes precedence over write permissions in the new screens.
A read-only user can inspect the records but cannot submit changes. A server refusal is reported;
a failed read must not turn into an apparently successful empty evaluation. Unavailable estimates,
stale KRIs and unevaluated AI metrics remain explicit. Changing a selection invalidates outstanding
reads so an earlier response cannot replace the current record.

Task acceptance criteria and completion evidence can be entered at creation and edited for a
selected task in the governance dialog. Named mitigation and owner selectors route new tasks;
updating evidence preserves the task's owner, dates, description and status. The server stamps the
evidence's author and time. Changing the appetite's entity disables tail editing until a matching
saved appetite is selected. Legal requirements with a supplier association hidden by the caller's
permissions remain read-only, preserving that association.

## Verification

Run from the repository root:

```sh
dotnet build src/GUIClient/GUIClient.csproj --no-restore --disable-build-servers -m:1
dotnet build src/GUIClient.Tests/GUIClient.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet src/GUIClient.Tests/bin/Debug/net10.0/GUIClient.Tests.dll
./build.sh LintUi
```

The headless tests source-link the presentation/input rules under `Tools/Track9` and cover exact
permission audiences, third-line dominance, late-load rejection, required economic inputs, explicit
absence of values, appetite and task routing, hidden supplier associations, preservation of request
fields and task evidence. Localization tests check literal
and computed enum labels against all three resources. The GUI build checks compiled XAML bindings.

These checks do not constitute an authenticated desktop session against a deployed API, a production
security approval, or database replay testing. The change introduces no new schema or API endpoint.

Verified on 2026-10-08: `GUIClient` compiled with 0 errors and 8 pre-existing warnings;
`GUIClient.Tests` compiled without warnings and passed all 674 tests (0 skipped); `LintUi`
scanned 121 views with 0 violations. Its build bootstrap reported 3 existing .NET 10 tooling
warnings. Authenticated visual acceptance remains pending.

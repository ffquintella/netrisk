# Change Log
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

## [NEXT] - Unreleased

## [2.5.0] - 2026-06-03

This release includes new features and improvements.

### Added
- **Compiled bindings enabled globally** (`AvaloniaUseCompiledBindingsByDefault=true` in GUIClient): every view now declares an explicit `x:DataType`, giving compile-time binding validation and faster rendering with a lower RAM footprint. (Milestone 1.3)
- **High-performance virtualizing `TreeDataGrid`** for the dense vulnerability grid, replacing the `DataGrid`. Source/columns are built in code-behind (`FlatTreeDataGridSource<Vulnerability>`) reusing the existing converters and status cell template, with two-way selection sync.
- TreeDataGrid via the `libs/TreeDataGrid.Avalonia` submodule (MIT, .NET-Foundation source ported to Avalonia 12; security-reviewed), since Avalonia 12's official `Avalonia.Controls.TreeDataGrid` package is now commercially licensed
- Explicit `VirtualizingStackPanel` on the primary dense data lists (incidents, hosts, risks, users, notifications) to enforce UI virtualization and guard against accidental regressions
- `RiskScoringPair` record (replaces `Tuple<Risk, RiskScoring>`) so the vulnerability risk panel binds with compiled bindings
- Project docs: `CLAUDE.md`, `ROADMAP.md`, per-feature docs under `docs/features/`, `docs/ui-standard.md`
- Transitive pin for `Tmds.DBus.Protocol` 0.92.0 in GUIClient (addresses GHSA-xrw6-gwf8-vvr9)
- Transitive pin for `System.Security.Cryptography.Xml` 10.0.7 in API.Tests and ServerServices.Tests (addresses GHSA-37gx-xxp4-5rgx, GHSA-w3x6-4m5h-cxqf)
- UI standard compliance audit (`roadmap/UI_STANDARD_AUDIT.md`) and remediation plan (`roadmap/UI_STANDARD_COMPLIANCE_PLAN.md`)

### Fixed
- **macOS window dragging restored**: the custom title-bar `Menu` stretched the full window width with `ElementRole="User"` (non-draggable), leaving no `TitleBar` surface to grab; set `HorizontalAlignment="Left"` so the menu only occupies its items and the rest of the title-bar row is draggable again.
- Compile-time binding errors surfaced by enabling compiled bindings (previously silent, failing reflection bindings): added missing `StrActions` (AssessmentViewModel), `StrNotifications` (NavigationBarViewModel), `IsViewOperation`/`IsCreateOperation` (EditIncidentViewModel), and `CanCancel`/`CanClose` (IncidentResponsePlanTaskViewModel); corrected stale `ElementName`/`#name` references in `EditIncidentWindow`, `IncidentResponsePlanTaskWindow`, `EditMgmtReview`, `MainWindow`, and `AssessmentView`; typed the TreeViewItem style bindings in `EntitiesView`

### Changed
- **GUIClient UI compliance pass**: all Avalonia views now conform to the UI standard — hardcoded hex/named Background and Foreground colors removed from layout containers, all dialog/action/navigation buttons carry canonical CSS classes (`dialog1`, `dialog2`, `operation`, `subButton`, `navigation`, etc.), fixed-width inputs replaced with `MinWidth`, navigation buttons carry `Classes="navigation"`, `Classes="dark"` applied to modal windows. Semantic state colors (ProgressRing spinner, notification bell, FaceID status icons) preserved intentionally.

### Changed
- **Avalonia 11.3.11 → 12.0.1** across GUIClient, AvaloniaExtraControls, and the Aura.UI submodule. Trade-offs documented in ROADMAP.md (dev-tools overlay removed, tab drag-reorder removed, SVG assets replaced by Material icons, `SpacedGrid` replaced by native `Grid` spacing).
- ReactiveUI 22.3.1→23.2.1, ReactiveUI.Avalonia 11.3.8→12.0.1, Splat 17→19
- Material.Icons.Avalonia 2.4→3.0, MessageBox.Avalonia 3.x→12.x, Deadpikle.AvaloniaProgressRing 0.10→0.11
- LiveChartsCore family 2.0.0-rc5.4 → 2.1.0-dev-292
- SkiaSharp 3.119.2 → 3.119.3-preview.1.1 (required by Avalonia.Skia 12)
- Spectre.Console 0.51→0.55.2, Spectre.Console.Cli 0.51→0.55.0, Serilog.Sinks.Spectre 0.5→0.6.0 (breaking: `Command.Execute` now takes `CancellationToken`; visibility `protected`)
- Dependency refresh across all projects (patch/minor updates):
  - Serilog 4.3.0→4.3.1, Serilog.Sinks.Console 6.0.0→6.1.1, Serilog.Extensions.Hosting 9→10, Serilog.Extensions.Logging 9→10
  - Microsoft.Extensions.* 10.0.2→10.0.7 (Hosting, Localization, Configuration.Abstractions, DependencyInjection, DependencyInjection.Abstractions, DependencyModel)
  - Microsoft.AspNetCore.Authentication.JwtBearer 10.0.2→10.0.7, SystemWebAdapters 2.2.1→2.3.0
  - System.IdentityModel.Tokens.Jwt 8.15.0→8.17.0, System.Drawing.Common 10.0.2→10.0.7
  - BCrypt.Net-Next 4.0.3→4.1.0, DeviceId 6.9→6.11, Polly 8.5.2→8.6.6
  - MySqlConnector 2.4.0→2.5.0, MySqlBackup.NET.MySqlConnector 2.6.5→2.7.0
  - SkiaSharp family 3.119.1→3.119.2
  - Microsoft.ML.OnnxRuntime 1.23.2→1.24.4
  - JetBrains.Annotations 2025.2.2→2025.2.4, xunit.runner.visualstudio 3.1.4→3.1.5, Microsoft.NET.Test.Sdk →18.5.0
  - Tools.InnoSetup 6.4.3→6.7.1
- `MainWindow.axaml`: removed `ExtendClientAreaToDecorationsHint`, dead acrylic border, and redundant nested Grid wrappers; simplified layout to `RowDefinitions="Auto, Auto, *"` (menu → navigation → content)
- `NavigationBar.axaml`: replaced fragile level-index ancestor bindings (`$parent[7]`/`$parent[6]`) with type-safe `$parent[views:MainWindow]` lookups
- UI compliance pass across all GUIClient views: removed inline `Background`/`Foreground` hex literals, added canonical button classes (`dialog1`, `dialog2`, `operation`, `subButton`, `navigation`), converted fixed `Width=` to `MinWidth=` on form inputs, migrated form `StackPanel`s to responsive `Grid` layouts with `form_label` classes
- `LoginWindow.axaml`: migrated form to responsive `Grid`, added `dialog1`/`dialog2` button classes with icons
- `CloseDialog.axaml`, `FixRequestDialog.axaml`: button classes normalized, `Classes="dark"` added to window
- `NavigationBar.axaml`: `Classes="navigation"` added to all nav buttons

### Fixed
- High-severity transitive vulnerabilities in `Tmds.DBus.Protocol` and `System.Security.Cryptography.Xml`
- GUIClient startup crash on Avalonia 12 caused by `LiveChartsCore.SkiaSharpView.Avalonia` 2.0.1 still targeting Avalonia 11 APIs (`Avalonia.Input.Gestures.PinchEvent`)
- `libs\Aura.UI\Aura.UI.sln` now loads cleanly after aligning the remaining Aura.UI test/desktop sample projects with `.NET 10` + Avalonia 12 and excluding the legacy Blazor gallery sample from the solution
- MainWindow top-bar overlap where native OS title bar and custom `<Menu>` rendered in the same zone (caused by `ExtendClientAreaToDecorationsHint="True"` without the matching transparency stack)
- Navigation bar buttons crashing with `NullReferenceException` / `ArgumentNullException` after layout flattening due to hardcoded ancestor-level bindings resolving to `null`



## [2.2.0] - 2026-02-06

This is a major maintenance release with .NET 10 upgrade and significant UI improvements.

### Added
- Responsive window layouts for EditRiskWindow (controls now expand/contract with window resizing)
- Responsive DataGrid columns in RisksPanelView with user controls for reordering, resizing, and sorting
- Tooltips to status icons in RisksPanelView for better user experience
- AssetTargetFallback configuration to support .NET 8/9 packages in .NET 10 projects

### Changed
- **Upgraded to .NET 10.0** with C# 13 language support across all projects
- Updated NuGet package source mapping to allow all packages from nuget.org by default
- Upgraded Hangfire from 1.8.21 to 1.8.23
- Upgraded MySqlConnector from 2.4.0 to 2.5.0
- Upgraded Newtonsoft.Json from 13.0.3 to 13.0.4 (security update)
- Upgraded Microsoft.Extensions.DependencyInjection from 9.0.9 to 9.0.12
- EditRiskWindow now starts at 1200x800 with minimum size of 900x650
- EditRiskWindow controls converted from fixed-width StackPanels to responsive Grid layouts
- RisksPanelView DataGrid columns now use star-sizing for proportional distribution
- Updated submodules: NessusParser, Aura.UI, netrisk-plugin-sdk, reliable-rest-client-wrapper
- Improved horizontal and vertical responsiveness across GUIClient views

### Fixed
- Resolved duplicate Applications.resx resource conflicts
- Fixed EF Core dependency warnings in DAL project
- Fixed ServerServices compile warnings (CS8603 nullable reference warnings in MapsterConfiguration)
- Fixed ServerServices CS0219 warning (unused variable in FaceIDService)
- Fixed PDFsharp restore failures
- Fixed API build failures and license gating issues
- Fixed GUI client build errors during migration
- Fixed Avalonia ReactiveUI startup issues
- Fixed EditRiskWindow buttons not staying at bottom during vertical resize
- Fixed EditRiskWindow right panel overlapping dropdowns on narrow windows
- Fixed risk deletion and closure bugs
- Package dependency warnings across all projects resolved



## [2.1.4] - 27/09/2025

This is a maintenance release with several bug fixes and improvements.

### Added

### Changed

### Fixed
- Risk closure bug


## [2.1.0] - 27/08/2025

This is a maintenance release with several bug fixes and improvements.

### Added
- A search on the incident response plan list
- Risk calculation command line command
- Plugin system
- FaceId plugin verification
- FaceId registration
- FaceId verification for risk closure
- Created the security classification entity
- Created the organization data entity
- Created the organization data group entity

### Changed
- Layout improvements on the incident window
- Changed the position of the edit button on the entities view
- Upgraded several packages to the latest version
- Incident ReportedByEntity field is now nullable
- Upgraded to Avalonia 11.3
- Upgraded to .NET 9
- Upgraded LiveCharts
- Bussiness process entitiy has new fields

### Fixed
- Return to the first pagination on the risk vulnerability list after selecting a new risk
- The search on the incident response plan list
- Bug in risk association
- Contributing score no longer considers closed vulnerabilities
- Bug in closing incident response plan window

## [2.0.7] - 2025-08-01

This is a bug fix release.

### Added

### Changed
- Filter to only show approved incidents response plans on the incident window
- Layout improvements on the incident window

### Fixed
- Risk vulnerability pagination
- Risks loading time
- Added missing scroll view on the incidents window
- Removed leftover foreign key on the incident response plan


## [2.0.6] - 2025-07-01

This is a bug fix release.

### Added
- Risk vulnerability pagination

### Changed


### Fixed
- Risks loading time


## [2.0.0] - 2025-06-01

This is a new major release that brings some new features and improvements.

### Added
- Incident Management
- Incident Response Plans
- New Dashboard graphics and improved performance
- Last import date on vulnerability data
- Filters on the entity list

### Changed
- Ordering of the entity list
- Filters for the multi select fields
- Risk filter location

### Fixed
- Several bug fixed - please see [Github issues](https://github.com/ffquintella/netrisk/issues)

## [1.7.1] - 2024-11-06

This is a new major release that brings some new features and improvements.

### Added

- Vulnerability chat tracking and improved e-mail communication
- New Dashboard graphics and improved performance
- Started to use .net migrations as a way to manage the database schema

### Changed

- The way risk catalogs are stored and managed

### Fixed

- Several bug fixed - please see [Github issues](https://github.com/ffquintella/netrisk/issues)

## [1.6.1] - 2024-10-15

...


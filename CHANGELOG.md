# Change Log
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/)
and this project adheres to [Semantic Versioning](http://semver.org/).

## [2.1.5] - Unreleased

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


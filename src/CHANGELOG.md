# Changelog

All notable changes to this project are documented in this file.

## [Unreleased] - 2026-04-29

### Changed
- Updated `Pomelo.EntityFrameworkCore.MySql` from `9.0.0` to `10.0.0` in `DAL/DAL.csproj`.
- Aligned Pomelo with `Microsoft.EntityFrameworkCore*` `10.0.7` packages to resolve `NU1608` dependency constraint warnings during build.

### Fixed
- Fixed missing namespace imports causing `GUIClient` build errors (`VisualTreeAttachmentEventArgs` and `Task`) in `GUIClient/Views/DashboardView.axaml.cs` and `GUIClient/ViewModels/Reports/FileReportsViewModel.cs`.
- Standardized `detailButton` sizing and icon layout in `GUIClient/Styles/WindowStyles.axaml` to prevent icon clipping/breaking.
- Updated report file action buttons in `GUIClient/Views/Reports/FileReports.axaml` to inherit the global `detailButton` style consistently.

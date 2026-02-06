# NetRisk Build System

This directory contains the Nuke build automation scripts for the NetRisk project.

## Prerequisites

- .NET SDK (currently targeting .NET 10.0)
- Docker (for creating Docker images)
- InnoSetup (for Windows installers)

## Available Build Targets

### Version Bumping

The build system includes automated version bumping commands that update version numbers across all project files and the CHANGELOG.

#### Usage

You can bump versions in three ways:

**1. Using specific bump targets:**

```bash
# Bump major version (e.g., 2.1.5 -> 3.0.0)
./build.sh BumpMajor

# Bump minor version (e.g., 2.1.5 -> 2.2.0)
./build.sh BumpMinor

# Bump patch version (e.g., 2.1.5 -> 2.1.6)
./build.sh BumpPatch
```

**2. Using the generic Bump target with a parameter:**

```bash
# On Unix/Linux/macOS
./build.sh Bump --bump-type major
./build.sh Bump --bump-type minor
./build.sh Bump --bump-type patch

# On Windows
build.cmd Bump --bump-type major
build.cmd Bump --bump-type minor
build.cmd Bump --bump-type patch
```

#### What the Bump Command Does

1. **Scans all project files** in the `src/` directory for `.csproj` files
2. **Reads the current version** from the first project file found
3. **Calculates the new version** based on the bump type:
   - **major**: Increments the major version, resets minor and patch to 0 (X+1.0.0)
   - **minor**: Increments the minor version, resets patch to 0 (x.X+1.0)
   - **patch**: Increments the patch version (x.x.X+1)
4. **Updates all project files** with the new version in:
   - `<AssemblyVersion>` element
   - `<FileVersion>` element
   - `<Version>` element (if present)
5. **Updates CHANGELOG.md**:
   - Changes the "Unreleased" version to the new version with today's date
   - Creates a new "Unreleased" section at the top for future changes

#### Example

```bash
# Current version: 2.1.5
./build.sh BumpMinor

# Output:
# [INF] Bumping minor version...
# [INF] Current version: 2.1.5
# [INF] New version: 2.2.0
# [INF] Updated /Users/user/netrisk/src/API/API.csproj
# [INF] Updated /Users/user/netrisk/src/GUIClient/GUIClient.csproj
# [INF] ... (all other projects)
# [INF] Updated CHANGELOG.md with version 2.2.0
# [INF] Version bump completed successfully!
# [INF] Updated 15 project files
# [INF] Don't forget to commit and tag the new version!
```

#### After Bumping

After running a bump command, you should:

1. **Review the changes** in all `.csproj` files and `CHANGELOG.md`
2. **Commit the version bump**:
   ```bash
   git add -A
   git commit -m "Bump version to 2.2.0"
   ```
3. **Tag the release** (optional but recommended):
   ```bash
   git tag -a v2.2.0 -m "Release version 2.2.0"
   git push origin v2.2.0
   ```

### Compilation Targets

```bash
# Compile all projects
./build.sh Compile

# Compile specific components
./build.sh CompileApi
./build.sh CompileWebsite
./build.sh CompileBackgroundJobs
./build.sh CompileConsoleClient
./build.sh CompileLinuxGUI
./build.sh CompileWindowsGUI
./build.sh CompileMacGUI
```

### Packaging Targets

```bash
# Package all projects
./build.sh PackageAll

# Package specific components
./build.sh PackageApi
./build.sh PackageWebSite
./build.sh PackageBackgroundJobs
./build.sh PackageConsoleClient
./build.sh PackageWindowsGUI
./build.sh PackageLinuxGUI
./build.sh PackageMacGUI
./build.sh PackageMacA64GUI
```

### Docker Image Targets

```bash
# Create all Docker images
./build.sh CreateAllDockerImages

# Create specific Docker images
./build.sh CreateDockerImageApi
./build.sh CreateDockerImageWebSite
./build.sh CreateDockerImageBackgroundJobs
./build.sh CreateDockerImageConsoleClient

# Push all Docker images
./build.sh PushAllDockerImages

# Push specific Docker images
./build.sh PushDockerImageApi
./build.sh PushDockerImageWebSite
./build.sh PushDockerImageBackgroundJobs
./build.sh PushDockerImageConsoleClient
```

### Utility Targets

```bash
# Print build information
./build.sh Print

# Clean build artifacts
./build.sh Clean

# Restore NuGet packages
./build.sh Restore

# Clean working directory
./build.sh CleanWorkDir
```

## Configuration

The build system supports two configurations:

- **Debug** (default for local builds)
- **Release** (default for CI/CD builds)

To specify a configuration:

```bash
./build.sh Compile --configuration Release
```

## Version Management

Versions are managed through:

1. **Git tags**: The build system reads the latest tag starting with `releases/` to determine the version
2. **Project files**: Each `.csproj` file contains `<AssemblyVersion>` and `<FileVersion>` elements
3. **CHANGELOG.md**: Documents all version changes following [Keep a Changelog](https://keepachangelog.com/) format

## Directory Structure

- `build/Build.cs` - Main build script
- `build/Configuration.cs` - Build configuration
- `build/Docker/` - Dockerfile templates
- `build/puppet/` - Puppet configuration for deployment

## Troubleshooting

### Build fails with "No project files found"

Make sure you're running the build from the repository root directory.

### Version bump doesn't update all files

Check that your `.csproj` files have a `<PropertyGroup>` element with version properties.

### CHANGELOG.md not updated correctly

Ensure your `CHANGELOG.md` follows the standard format with an "Unreleased" section.

## Additional Resources

- [Nuke Build Documentation](https://nuke.build/)
- [Keep a Changelog](https://keepachangelog.com/)
- [Semantic Versioning](https://semver.org/)

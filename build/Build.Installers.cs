using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetRisk.Packaging;
using Nuke.Common;
using Nuke.Common.IO;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.ProjectModel;
using static Nuke.Common.EnvironmentInfo;
using Serilog;

/// <summary>
/// Milestone 5.2 — modern native installers.
///
/// Every target here shares one shape: stage the already-published output, render the format's
/// manifest from a reviewed template under <c>build/installers/</c>, then hand it to the
/// platform's packaging tool. When that tool is missing (a Windows SDK on a Mac, snapcraft on
/// Windows) the staging still happens and the target logs a single warning instead of failing,
/// so the manifests stay verifiable on any host.
/// </summary>
partial class Build
{
    AbsolutePath InstallersDirectory => BuildDirectory / "installers";
    AbsolutePath InstallerAssetsDirectory => InstallersDirectory / "assets";
    AbsolutePath WindowsReleaseDirectory => PublishDirectory / "GUIClient-Windows-x64-Releases";
    AbsolutePath WindowsPublishDirectory => PublishDirectory / "GUIClient-Windows";
    AbsolutePath LinuxPublishDirectory => PublishDirectory / "GUIClient-Linux";

    [Parameter("MSIX Publisher; must be the exact subject of the signing certificate")]
    readonly string MsixPublisher;

    [Parameter("Base URI the MSIX and .appinstaller are published under")]
    readonly string AppInstallerBaseUri;

    [Parameter("Snap grade: stable (default for Release) or devel")]
    readonly string SnapGrade;

    [Parameter("Lay the DMG out with create-dmg (branded background, needs a GUI session)")]
    readonly bool BrandedDmg;

    string EffectiveMsixPublisher =>
        ParamOrEnv(MsixPublisher, "NETRISK_MSIX_PUBLISHER") ?? PackageIdentity.MsixDefaultPublisher;

    string EffectiveAppInstallerBaseUri =>
        (ParamOrEnv(AppInstallerBaseUri, "NETRISK_APPINSTALLER_BASE_URI") ?? PackageIdentity.AppInstallerBaseUri)
        .TrimEnd('/');

    string EffectiveSnapGrade => SnapGrades.Resolve(
        ParamOrEnv(SnapGrade, "NETRISK_SNAP_GRADE"),
        Configuration == Configuration.Release);

    // ---------------------------------------------------------------------------------------
    // Shared publish steps. Extracted so the Inno installer, the MSI and the MSIX are all cut
    // from one compiled output instead of three, and the two Linux container formats from one.
    // ---------------------------------------------------------------------------------------

    Target PublishWindowsGui => _ => _
        .Description("Publish the self-contained win-x64 GUI client consumed by every Windows installer")
        .DependsOn(Clean)
        .DependsOn(Restore)
        .Executes(() =>
        {
            Directory.CreateDirectory(PublishDirectory);

            DotNetPublish(s => s
                .SetProject(Solution.GetProject("GUIClient"))
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .SetRuntime("win-x64")
                .EnableSelfContained()
                .EnablePublishSingleFile()
                .SetOutput(WindowsPublishDirectory)
                .SetVerbosity(DotNetVerbosity.minimal)
                .DisableProcessOutputLogging()
            );

            // The shipped executables and libraries are signed here, once, so every installer
            // built from this output carries signed payloads.
            SignWindowsArtifacts(
                WindowsPublishDirectory.GlobFiles("**/*.exe", "**/*.dll").ToList(),
                "the published Windows binaries");
        });

    Target PublishLinuxGui => _ => _
        .Description("Publish the self-contained linux-x64 GUI client consumed by every Linux package")
        .DependsOn(Clean)
        .DependsOn(Restore)
        .Executes(() =>
        {
            Directory.CreateDirectory(PublishDirectory);

            DotNetPublish(s => s
                .SetProject(Solution.GetProject("GUIClient"))
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .EnableSelfContained()
                .SetRuntime("linux-x64")
                .SetOutput(LinuxPublishDirectory)
                .SetVerbosity(DotNetVerbosity.minimal)
                .DisableProcessOutputLogging()
            );
        });

    // ---------------------------------------------------------------------------------------
    // Windows: MSI
    // ---------------------------------------------------------------------------------------

    Target PackageWindowsMSI => _ => _
        .Description("Build the per-machine, silent-install MSI (WiX v5)")
        .DependsOn(PublishWindowsGui)
        .Executes(() =>
        {
            Directory.CreateDirectory(WindowsReleaseDirectory);

            var wxs = InstallersDirectory / "windows" / "msi" / "NetRisk.wxs";
            var msi = WindowsReleaseDirectory / ArtifactNames.WindowsMsi(VersionClean);
            var productVersion = PackageVersions.ToMsiProductVersion(VersionClean);

            if (!IsWin)
            {
                // WiX prints "The WiX Toolset only supports Windows. All behavior after this
                // point is undefined" on other hosts and then either rejects legal authoring
                // or dies inside the bind phase, so there is nothing to gain from trying.
                Log.Warning(
                    "MSI packaging skipped: WiX only runs on Windows. {Wxs} is validated by Packaging.Tests; " +
                    "run this target on a Windows runner to produce {Msi}.", wxs, msi.Name);
                return;
            }

            var wix = ResolveWixCommand();
            if (wix is null)
                return;

            msi.DeleteFile();

            RunProcess(wix.Value.FileName,
                $"{wix.Value.ArgumentPrefix}build \"{wxs}\" -arch x64 " +
                $"-d ProductVersion={productVersion} " +
                $"-d PublishDir=\"{WindowsPublishDirectory}\" " +
                $"-d IconFile=\"{SourceDirectory / "GUIClient" / "Assets" / "NetRisk.ico"}\" " +
                $"-o \"{msi}\"",
                RootDirectory,
                TimeSpan.FromMinutes(15));

            SignWindowsArtifacts(new[] { msi }, "the MSI");
            WriteChecksum(msi);

            Log.Information(
                "MSI written to {Msi}. Silent install: msiexec /i \"{Name}\" /qn " +
                "[INSTALLFOLDER=...] [SERVERURL=https://server:5443/]", msi, msi.Name);
        });

    /// <summary>
    /// Locates the WiX CLI: a `wix` on PATH (global tool or MSI install) first, then a `dotnet
    /// wix` from a local tool manifest. The build never installs it silently — a missing
    /// toolchain is reported with the exact command that fixes it.
    ///
    /// The pinned version is deliberately v5: WiX v6 and v7 refuse to build anything until the
    /// Open Source Maintenance Fee EULA has been accepted.
    /// </summary>
    (string FileName, string ArgumentPrefix)? ResolveWixCommand()
    {
        if (ToolOnPath("wix"))
            return ("wix", string.Empty);

        // A repository or user-level tool manifest that declares wix makes `dotnet wix` work.
        if (!string.IsNullOrWhiteSpace(RunProcessCapture("dotnet", "wix --version")))
            return ("dotnet", "wix ");

        var message =
            $"WiX was not found, so no MSI was produced. Install it on the runner with: " +
            $"dotnet tool install --global wix --version {WixVersion}";

        // A release build asking for an MSI and not getting one is a broken release, not a
        // tolerable skip.
        if (Configuration == Configuration.Release)
            throw new Exception(message);

        Log.Warning("{Message}", message);
        return null;
    }

    /// <summary>
    /// WiX version the authoring targets. v5 is the last release usable without accepting the
    /// Open Source Maintenance Fee EULA.
    /// </summary>
    const string WixVersion = "5.0.2";

    // ---------------------------------------------------------------------------------------
    // Windows: MSIX + App Installer
    // ---------------------------------------------------------------------------------------

    Target PackageWindowsMSIX => _ => _
        .Description("Build the sandboxed MSIX package and its .appinstaller auto-update file")
        .DependsOn(PublishWindowsGui)
        .Executes(() =>
        {
            Directory.CreateDirectory(WindowsReleaseDirectory);

            var layout = PublishDirectory / "GUIClient-Windows-MSIX-Layout";
            var msix = WindowsReleaseDirectory / ArtifactNames.WindowsMsix(VersionClean);
            var appInstaller = WindowsReleaseDirectory / ArtifactNames.WindowsAppInstaller();

            // 1. Layout: the published payload plus the tile assets plus the manifest.
            DeleteDirectoryRobust(layout);
            Directory.CreateDirectory(layout);
            CopyDirectory(WindowsPublishDirectory, layout);
            CopyDirectory(InstallersDirectory / "windows" / "msix" / "Assets", layout / "Assets");

            File.WriteAllText(layout / "AppxManifest.xml", RenderTemplate(
                InstallersDirectory / "windows" / "msix" / "AppxManifest.xml.template",
                PackagingTokens.MsixManifest(VersionClean, EffectiveMsixPublisher)));

            // 2. The .appinstaller is plain XML and is published regardless of whether the
            // packaging tool exists on this host — the download page links it.
            File.WriteAllText(appInstaller, RenderTemplate(
                InstallersDirectory / "windows" / "msix" / "NetRisk.appinstaller.template",
                PackagingTokens.AppInstaller(VersionClean, EffectiveMsixPublisher, EffectiveAppInstallerBaseUri)));

            if (!IsWin)
            {
                Log.Warning(
                    "MSIX packaging skipped: makeappx.exe ships with the Windows SDK and only runs on Windows. " +
                    "The package layout and manifest were written to {Layout}; run this target on a Windows runner " +
                    "to produce {Msix}.", layout, msix.Name);
                return;
            }

            var makeAppx = ResolveMakeAppx();
            msix.DeleteFile();

            RunProcess(makeAppx, $"pack /d \"{layout}\" /p \"{msix}\" /o", RootDirectory, TimeSpan.FromMinutes(15));

            // An unsigned MSIX cannot be installed at all, so a skipped signature is worth a
            // louder warning here than elsewhere.
            var plan = WindowsSigningPlanner.Plan(WindowsSigning);
            SignWindowsArtifacts(new[] { msix }, "the MSIX package");

            if (!plan.ShouldSign)
                Log.Warning(
                    "{Msix} is unsigned and Windows will refuse to install it. MSIX requires a signature whose " +
                    "subject matches the manifest Publisher ({Publisher}).", msix.Name, EffectiveMsixPublisher);

            WriteChecksum(msix);
        });

    // ---------------------------------------------------------------------------------------
    // Linux: Flatpak
    // ---------------------------------------------------------------------------------------

    Target PackageLinuxFlatpak => _ => _
        .Description("Build the Flatpak bundle for the Linux GUI client")
        .DependsOn(PublishLinuxGui)
        .Executes(() =>
        {
            var staging = PublishDirectory / "flatpak";
            DeleteDirectoryRobust(staging);
            Directory.CreateDirectory(staging);

            CopyDirectory(LinuxPublishDirectory, staging / "publish");
            StageSharedLinuxFiles(staging);

            var manifestName = $"{PackageIdentity.LinuxAppId}.yml";
            File.WriteAllText(staging / manifestName, RenderTemplate(
                InstallersDirectory / "linux" / "flatpak" / $"{PackageIdentity.LinuxAppId}.yml.template",
                PackagingTokens.FlatpakManifest()));

            ValidateAppStreamMetadata(staging / $"{PackageIdentity.LinuxAppId}.metainfo.xml");

            if (!ToolOnPath("flatpak-builder"))
            {
                Log.Warning(
                    "Flatpak packaging skipped: flatpak-builder is not installed. The manifest and payload are staged " +
                    "in {Staging}; on a Linux runner install flatpak/flatpak-builder plus the " +
                    "org.freedesktop.Platform//24.08 runtime and re-run this target.", staging);
                return;
            }

            var repository = staging / "repo";
            var bundle = PublishDirectory / ArtifactNames.LinuxFlatpak(VersionClean);
            bundle.DeleteFile();

            RunProcess("flatpak-builder",
                $"--force-clean --repo=\"{repository}\" \"{staging / "build"}\" \"{staging / manifestName}\"",
                staging,
                TimeSpan.FromMinutes(45));

            RunProcess("flatpak",
                $"build-bundle \"{repository}\" \"{bundle}\" {PackageIdentity.LinuxAppId}",
                staging,
                TimeSpan.FromMinutes(20));

            WriteChecksum(bundle);
            Log.Information("Flatpak bundle written to {Bundle}.", bundle);
        });

    // ---------------------------------------------------------------------------------------
    // Linux: Snap
    // ---------------------------------------------------------------------------------------

    Target PackageLinuxSnap => _ => _
        .Description("Build the strictly-confined Snap package for the Linux GUI client")
        .DependsOn(PublishLinuxGui)
        .Executes(() =>
        {
            var staging = PublishDirectory / "snap";
            DeleteDirectoryRobust(staging);
            Directory.CreateDirectory(staging / "payload");

            CopyDirectory(LinuxPublishDirectory, staging / "payload" / "publish");
            StageSharedLinuxFiles(staging / "payload");

            File.WriteAllText(staging / "snapcraft.yaml", RenderTemplate(
                InstallersDirectory / "linux" / "snap" / "snapcraft.yaml.template",
                PackagingTokens.SnapcraftYaml(VersionClean, EffectiveSnapGrade)));

            ValidateAppStreamMetadata(staging / "payload" / $"{PackageIdentity.LinuxAppId}.metainfo.xml");

            if (!ToolOnPath("snapcraft"))
            {
                Log.Warning(
                    "Snap packaging skipped: snapcraft is not installed. snapcraft.yaml and the payload are staged in " +
                    "{Staging}; on a Linux runner install snapcraft (snap install snapcraft --classic) and re-run " +
                    "this target.", staging);
                return;
            }

            RunProcess("snapcraft", "pack --verbosity brief", staging, TimeSpan.FromMinutes(60));

            var snap = staging.GlobFiles("*.snap").FirstOrDefault();
            if (snap is null)
                throw new Exception($"snapcraft reported success but produced no .snap in {staging}.");

            var target = PublishDirectory / snap.Name;
            target.DeleteFile();
            snap.Move(target);

            WriteChecksum(target);
            Log.Information("Snap written to {Snap}. Channel strategy: {Grade}.", target, EffectiveSnapGrade);
        });

    /// <summary>Files both Linux container formats need: launcher, desktop entry, metadata, icons.</summary>
    void StageSharedLinuxFiles(AbsolutePath staging)
    {
        var shared = InstallersDirectory / "linux" / "shared";

        (shared / "netrisk.sh").Copy(staging / "netrisk.sh", ExistsPolicy.FileOverwrite);
        (shared / $"{PackageIdentity.LinuxAppId}.desktop")
            .Copy(staging / $"{PackageIdentity.LinuxAppId}.desktop", ExistsPolicy.FileOverwrite);

        File.WriteAllText(staging / $"{PackageIdentity.LinuxAppId}.metainfo.xml", RenderTemplate(
            shared / $"{PackageIdentity.LinuxAppId}.metainfo.xml.template",
            PackagingTokens.AppStreamMetainfo(VersionClean, DateTime.UtcNow)));

        (InstallerAssetsDirectory / "netrisk-256.png").Copy(staging / "icon-256.png", ExistsPolicy.FileOverwrite);
        (InstallerAssetsDirectory / "netrisk-512.png").Copy(staging / "icon-512.png", ExistsPolicy.FileOverwrite);
    }

    void ValidateAppStreamMetadata(AbsolutePath metainfo)
    {
        if (!ToolOnPath("appstreamcli"))
        {
            Log.Debug("appstreamcli not installed; skipping AppStream validation of {File}.", metainfo.Name);
            return;
        }

        try
        {
            RunProcess("appstreamcli", $"validate --no-net \"{metainfo}\"", RootDirectory, TimeSpan.FromMinutes(5));
            Log.Information("AppStream metadata in {File} validates.", metainfo.Name);
        }
        catch (Exception exception)
        {
            // Flathub rejects a package whose metadata does not validate, so this is a real
            // failure rather than a nice-to-have.
            throw new Exception($"AppStream validation of {metainfo} failed: {exception.Message}", exception);
        }
    }

    // ---------------------------------------------------------------------------------------
    // macOS: signed, notarized app bundle + pkg + drag-and-drop DMG
    // ---------------------------------------------------------------------------------------

    private void CreateMacPkgAndDmg(AbsolutePath publishDirectory, string archLabel, string version)
    {
        const string appName = PackageIdentity.ProductName;
        const string executableName = PackageIdentity.ExecutableName;

        var bundleRoot = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}-app";
        var appBundle = bundleRoot / $"{appName}.app";
        var contentsDir = appBundle / "Contents";
        var macosDir = contentsDir / "MacOS";
        var resourcesDir = contentsDir / "Resources";

        DeleteDirectoryRobust(bundleRoot);
        Directory.CreateDirectory(macosDir);
        Directory.CreateDirectory(resourcesDir);

        RunProcess("cp", $"-R \"{publishDirectory}/.\" \"{macosDir}\"", RootDirectory);
        (InstallerAssetsDirectory / PackagingTokens.MacIconFileName)
            .Copy(resourcesDir / PackagingTokens.MacIconFileName, ExistsPolicy.FileOverwrite);

        File.WriteAllText(contentsDir / "Info.plist", RenderTemplate(
            InstallersDirectory / "macos" / "Info.plist.template",
            PackagingTokens.MacInfoPlist(version, DateTime.UtcNow.Year)));

        var executablePath = macosDir / executableName;
        if (File.Exists(executablePath))
            RunProcess("chmod", $"+x \"{executablePath}\"", RootDirectory);

        var plan = MacSigningPlanner.Plan(MacSigning);
        Log.Information("macOS packaging plan: {Reason}", plan.Reason);

        try
        {
            ImportMacCertificate(plan);

            SignMacBundle(appBundle, plan);
            NotarizeAndStaple(appBundle, plan);

            var pkgPath = BuildMacInstallerPackage(appBundle, archLabel, version, plan);
            var dmgPath = BuildMacDiskImage(appBundle, archLabel, version, plan);

            WriteChecksum(pkgPath);

            // Keep the historical checksum file name for the DMG (GUIClient-Mac-<arch>-<version>.sha256):
            // the download page and release notes have referenced it since before this track.
            var checksumFile = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}.sha256";
            checksumFile.DeleteFile();
            File.WriteAllText(checksumFile, SHA256CheckSum(dmgPath));
        }
        finally
        {
            RemoveTemporaryKeychain();
        }
    }

    private AbsolutePath BuildMacInstallerPackage(AbsolutePath appBundle, string archLabel, string version,
        MacSigningPlan plan)
    {
        var pkgRoot = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}-pkgroot";
        DeleteDirectoryRobust(pkgRoot);
        Directory.CreateDirectory(pkgRoot / "Applications");

        // ditto rather than cp: it preserves the extended attributes a code signature and a
        // stapled notarization ticket live in.
        RunProcess("ditto", $"\"{appBundle}\" \"{pkgRoot / "Applications" / appBundle.Name}\"", RootDirectory);

        var pkgPath = PublishDirectory / ArtifactNames.MacPkg(archLabel, version);
        pkgPath.DeleteFile();

        var installerIdentity = EffectiveMacInstallerSigningIdentity;
        var unsignedPkg = plan.ShouldSign && !string.IsNullOrWhiteSpace(installerIdentity)
            ? PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}-unsigned.pkg"
            : pkgPath;

        unsignedPkg.DeleteFile();

        RunProcess("pkgbuild",
            $"--root \"{pkgRoot}\" --identifier \"{PackageIdentity.MacBundleIdentifier}\" " +
            $"--version \"{version}\" --install-location / \"{unsignedPkg}\"",
            RootDirectory);

        if (unsignedPkg != pkgPath)
        {
            // A .pkg carries an installer signature from a "Developer ID Installer"
            // certificate, which is a different identity from the application one.
            RunSensitiveProcess("productsign",
                $"--sign \"{installerIdentity}\" \"{unsignedPkg}\" \"{pkgPath}\"",
                RootDirectory,
                TimeSpan.FromMinutes(10));

            unsignedPkg.DeleteFile();
            NotarizeAndStaple(pkgPath, plan);
        }
        else if (plan.ShouldSign)
        {
            Log.Warning(
                "The .pkg is unsigned: no --mac-installer-signing-identity was supplied. " +
                "A 'Developer ID Installer' certificate is required to sign an installer package.");
        }

        return pkgPath;
    }

    /// <summary>
    /// Assembles the drag-and-drop disk image: the app bundle, an /Applications symlink to drop
    /// it on, a branded background and a volume icon.
    ///
    /// The default path is pure `hdiutil`, which needs no Finder and therefore works on a
    /// headless CI runner. Passing --branded-dmg switches to create-dmg, which additionally
    /// positions the icons and shows the background — at the cost of AppleScript and a GUI
    /// session.
    /// </summary>
    private AbsolutePath BuildMacDiskImage(AbsolutePath appBundle, string archLabel, string version,
        MacSigningPlan plan)
    {
        var staging = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}-dmg";
        DeleteDirectoryRobust(staging);
        Directory.CreateDirectory(staging);

        RunProcess("ditto", $"\"{appBundle}\" \"{staging / appBundle.Name}\"", RootDirectory);

        // The drop target. A symlink is what makes the image drag-and-drop rather than a
        // folder the user has to copy out by hand.
        RunProcess("ln", $"-s /Applications \"{staging / "Applications"}\"", RootDirectory);

        Directory.CreateDirectory(staging / ".background");
        (InstallerAssetsDirectory / "dmg-background.png")
            .Copy(staging / ".background" / "background.png", ExistsPolicy.FileOverwrite);
        (InstallerAssetsDirectory / "netrisk.icns").Copy(staging / ".VolumeIcon.icns", ExistsPolicy.FileOverwrite);

        var dmgPath = PublishDirectory / ArtifactNames.MacDmg(archLabel, version);
        dmgPath.DeleteFile();

        var branded = FlagOrEnv(BrandedDmg, "NETRISK_BRANDED_DMG");

        if (branded && ToolOnPath("create-dmg"))
        {
            RunProcess("create-dmg",
                $"--volname \"{PackageIdentity.ProductName} {version}\" " +
                $"--volicon \"{staging / ".VolumeIcon.icns"}\" " +
                $"--background \"{InstallerAssetsDirectory / "dmg-background.png"}\" " +
                "--window-pos 200 120 --window-size 640 400 --icon-size 128 " +
                $"--icon \"{appBundle.Name}\" 160 205 " +
                "--app-drop-link 480 205 " +
                "--no-internet-enable " +
                $"\"{dmgPath}\" \"{staging}\"",
                RootDirectory,
                TimeSpan.FromMinutes(20));
        }
        else
        {
            if (branded)
                Log.Warning(
                    "--branded-dmg was requested but create-dmg is not installed (brew install create-dmg); " +
                    "falling back to the plain hdiutil layout.");

            // Best effort: without SetFile the volume icon is staged but macOS will not show
            // it. SetFile ships with the Xcode command line tools.
            TryRun("SetFile", $"-a C \"{staging}\"");

            RunProcess("hdiutil",
                $"create -volname \"{PackageIdentity.ProductName} {version}\" -srcfolder \"{staging}\" " +
                $"-ov -format UDZO \"{dmgPath}\"",
                RootDirectory,
                TimeSpan.FromMinutes(20));
        }

        SignMacFile(dmgPath, plan);
        NotarizeAndStaple(dmgPath, plan);

        return dmgPath;
    }

    // ---------------------------------------------------------------------------------------
    // Aggregate targets
    // ---------------------------------------------------------------------------------------

    Target PackageWindowsInstallers => _ => _
        .Description("Build every Windows installer (Inno Setup .exe, .msi, .msix + .appinstaller)")
        .DependsOn(PackageWindowsGUI, PackageWindowsMSI, PackageWindowsMSIX)
        .Executes(() => { });

    Target PackageLinuxInstallers => _ => _
        .Description("Build every Linux package (zip archive, Flatpak, Snap)")
        .DependsOn(PackageLinuxGUI, PackageLinuxFlatpak, PackageLinuxSnap)
        .Executes(() => { });

    Target PackageAllInstallers => _ => _
        .Description("Build every desktop installer for every platform this host supports")
        .DependsOn(PackageWindowsInstallers, PackageLinuxInstallers, PackageMacGUI, PackageMacA64GUI)
        .Executes(() =>
        {
            Log.Information("Artifacts are in {Publish}.", PublishDirectory);
        });

    // ---------------------------------------------------------------------------------------
    // Small shared helpers
    // ---------------------------------------------------------------------------------------

    /// <summary>Reads a template from build/installers and substitutes its placeholders.</summary>
    static string RenderTemplate(AbsolutePath templateFile, IReadOnlyDictionary<string, string> values)
    {
        if (!templateFile.FileExists())
            throw new Exception($"Installer template '{templateFile}' is missing.");

        return PackagingTemplate.Render(File.ReadAllText(templateFile), values);
    }

    static void CopyDirectory(AbsolutePath source, AbsolutePath target)
    {
        if (!Directory.Exists(source))
            throw new Exception($"Expected '{source}' to exist. Run the matching publish target first.");

        Directory.CreateDirectory(target);
        source.Copy(target, ExistsPolicy.MergeAndOverwrite);
    }

    void WriteChecksum(AbsolutePath artifact)
    {
        if (!artifact.FileExists())
            return;

        var checksumFile = AbsolutePath.Create(ArtifactNames.Checksum(artifact.ToString()));
        checksumFile.DeleteFile();
        File.WriteAllText(checksumFile, SHA256CheckSum(artifact));
    }
}

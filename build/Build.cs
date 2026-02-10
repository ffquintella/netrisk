using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using InnoSetup.ScriptBuilder;
using Microsoft.Build.Construction;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.Docker;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
//using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;

//using static Nuke.Common.IO.CompressionTasks;

using Serilog;

class Build : NukeBuild
{

    AbsolutePath SourceDirectory => RootDirectory / "src" ;
    AbsolutePath BuildWorkDirectory => RootDirectory / "workdir" ;
    AbsolutePath BuildDirectory => RootDirectory / "build" ;
    AbsolutePath PuppetDirectory => BuildDirectory / "puppet" ;
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath CompileDirectory => OutputDirectory / "compile";
    AbsolutePath ApiCompileDirectory => CompileDirectory / "api";
    AbsolutePath ConsoleClientCompileDirectory => CompileDirectory / "consoleClient";
    AbsolutePath WebSiteCompileDirectory => CompileDirectory / "website";
    AbsolutePath BackgroundJobsCompileDirectory => CompileDirectory / "backgroundjobs";
    AbsolutePath LinuxGuiCompileDirectory => CompileDirectory / "LinuxGUI";
    AbsolutePath WindowsGuiCompileDirectory => CompileDirectory / "WindowsGUI";
    AbsolutePath MacGuiCompileDirectory => CompileDirectory / "MacGUI";
    
    AbsolutePath PublishDirectory => OutputDirectory / "publish";
    //AbsolutePath ApiPublishDirectory => PublishDirectory / "api";
    
    [GitRepository] readonly GitRepository SourceRepository;

    string Version
    {
        get
        {
            // Try to get version from git tags directly using git command
            try
            {
                // Use "git.exe" on Windows, "git" on Unix-like systems
                var gitCommand = EnvironmentInfo.IsWin ? "git.exe" : "git";

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = gitCommand,
                        Arguments = "tag -l \"Releases/*\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = RootDirectory
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var tags = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    Version latestVersion = null;
                    string latestTag = null;

                    foreach (var tag in tags)
                    {
                        var versionString = tag.Replace("Releases/", "").Trim();
                        try
                        {
                            var version = new Version(versionString);
                            if (latestVersion == null || version > latestVersion)
                            {
                                latestVersion = version;
                                latestTag = tag.Trim();
                            }
                        }
                        catch
                        {
                            // Skip invalid version tags
                        }
                    }

                    if (latestTag != null)
                        return latestTag;
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    Log.Warning("Git command failed: {Error}", error);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to get version from git tags: {Message}", ex.Message);
            }

            return "Releases/0.50.1";
        }
    }

    string VersionClean
    {
        get
        {
            /*if(SourceRepository == null) Console.WriteLine($"Null Source repository");
            else Console.WriteLine($"Source repository: {SourceRepository.Identifier} " +
                                   $"Tag count:{SourceRepository.Tags.Count} First tag:{SourceRepository.Tags.FirstOrDefault()}");
            Console.WriteLine($"Version used: {Version}");*/
            var gitVersion = GetVersionFromGitTags();
            var projectVersion = GetLatestProjectVersion();

            Version selected = gitVersion;
            if (selected == null || (projectVersion != null && projectVersion > selected))
                selected = projectVersion;

            if (selected == null)
                return Version.Substring(9);

            return $"{selected.Major}.{selected.Minor}.{selected.Build}";
        }
    }

    public static int Main () => Execute<Build>(x => x.Usage);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Parameter("Version bump type: major, minor, or patch")]
    readonly string BumpType;

    //[Solution("src/netrisk.sln")]
    [Solution("src/netrisk.sln")]
    readonly Solution Solution;
    

    
    // TOOLS DEFINITIONS
    
    [NuGetPackage(
        packageId: "Tools.InnoSetup",
        packageExecutable: "iscc.exe")]
    readonly Tool Iscc;
    

    public Build()
    {
        //DockerTasks.DockerLogger = (type, text) => Log.Debug(text);
        
        //Solution = ProjectModelTasks.ParseSolution(SourceDirectory / "netrisk.sln");
        Solution = SolutionModelTasks.ParseSolution(SourceDirectory / "netrisk.sln");
    }
    
    
    Target Usage => _ => _
        .Description("Show usage help and available commands")
        .Executes(() =>
        {
            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                      NetRisk Build System                              ║");
            Console.WriteLine("║                     Powered by Nuke Build                              ║");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine("USAGE:");
            Console.WriteLine("  ./build.sh [target] [options]");
            Console.WriteLine();
            Console.WriteLine("COMMON TARGETS:");
            Console.WriteLine();
            Console.WriteLine("  Version Management:");
            Console.WriteLine("    BumpMajor              Bump major version (2.1.5 → 3.0.0)");
            Console.WriteLine("    BumpMinor              Bump minor version (2.1.5 → 2.2.0)");
            Console.WriteLine("    BumpPatch              Bump patch version (2.1.5 → 2.1.6)");
            Console.WriteLine("    Bump                   Bump with --bump-type parameter");
            Console.WriteLine();
            Console.WriteLine("  Compilation:");
            Console.WriteLine("    Compile                Compile all projects");
            Console.WriteLine("    CompileApi             Compile API project only");
            Console.WriteLine("    CompileWebsite         Compile WebSite project only");
            Console.WriteLine("    CompileBackgroundJobs  Compile BackgroundJobs project only");
            Console.WriteLine("    CompileGUI             Compile GUI clients (Linux, Windows, Mac)");
            Console.WriteLine();
            Console.WriteLine("  Packaging:");
            Console.WriteLine("    PackageAll             Package all projects for deployment");
            Console.WriteLine("    PackageApi             Package API only");
            Console.WriteLine("    PackageWebSite         Package WebSite only");
            Console.WriteLine("    PackageBackgroundJobs  Package BackgroundJobs only");
            Console.WriteLine("    PackageWindowsGUI      Package Windows GUI with installer");
            Console.WriteLine("    PackageLinuxGUI        Package Linux GUI");
            Console.WriteLine("    PackageMacGUI          Package Mac GUI (Intel)");
            Console.WriteLine("    PackageMacA64GUI       Package Mac GUI (ARM64)");
            Console.WriteLine();
            Console.WriteLine("  Docker:");
            Console.WriteLine("    CreateAllDockerImages  Create all Docker images");
            Console.WriteLine("    PushAllDockerImages    Push all Docker images to registry");
            Console.WriteLine();
            Console.WriteLine("  Utilities:");
            Console.WriteLine("    Clean                  Clean build artifacts");
            Console.WriteLine("    Restore                Restore NuGet packages");
            Console.WriteLine("    Print                  Print build information");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine();
            Console.WriteLine("  # Show this help");
            Console.WriteLine("  ./build.sh Usage");
            Console.WriteLine("  ./build.sh");
            Console.WriteLine();
            Console.WriteLine("  # Bump version");
            Console.WriteLine("  ./build.sh BumpPatch");
            Console.WriteLine("  ./build.sh Bump --bump-type minor");
            Console.WriteLine();
            Console.WriteLine("  # Compile everything");
            Console.WriteLine("  ./build.sh Compile");
            Console.WriteLine();
            Console.WriteLine("  # Package for deployment");
            Console.WriteLine("  ./build.sh PackageAll --configuration Release");
            Console.WriteLine();
            Console.WriteLine("  # Create Docker images");
            Console.WriteLine("  ./build.sh CreateAllDockerImages");
            Console.WriteLine();
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("  --configuration <Debug|Release>  Build configuration (default: Debug)");
            Console.WriteLine("  --bump-type <major|minor|patch>  Version bump type (for Bump target)");
            Console.WriteLine("  --help                           Show all available targets");
            Console.WriteLine();
            Console.WriteLine("For detailed documentation, see: build/README.md");
            Console.WriteLine("For all available targets, run: ./build.sh --help");
            Console.WriteLine();
        });

    Target Print => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            Log.Information("STARTING BUILD...");
            Log.Information("--- PARAMETERS ---");
            Log.Information("CONFIGURATION: {Conf}", Configuration);

            Log.Information("--- DIRECTORIES ---");
            Log.Information("SOURCE DIR: {Source}", SourceDirectory);
            Log.Information("OUTPUT DIR: {Output}", OutputDirectory);

            Log.Information("--- SOLUTION ---");
            Log.Information("Solution path = {Value}", Solution);
            Log.Information("Solution directory = {Value}", Solution.Directory);

            Log.Information("--- VERSION ---");
            Log.Information("Solution path = {Version}", VersionClean);
        });
    
    Target Clean => _ => _
        .Before(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            // Collect and delete all /obj and /bin directories in all sub-directories
            var deletableDirectories = SourceDirectory.GlobDirectories("**/obj", "**/bin");
            foreach (var deletableDirectory in deletableDirectories)
            {
                if(!deletableDirectory.ToString().Contains("build"))
                {
                    Log.Information($"Deleting {deletableDirectory}");
                    Directory.Delete(deletableDirectory, true);
                }
                
            }
            if(Directory.Exists(OutputDirectory))
                Directory.Delete(OutputDirectory, true);

            
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => 
                s.SetProjectFile(Solution)
                    .SetVerbosity(DotNetVerbosity.normal)
                );
        });

    Target CompileApi => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("API");

            Directory.CreateDirectory(ApiCompileDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetVerbosity(DotNetVerbosity.normal)
                    .SetOutputDirectory(ApiCompileDirectory)
                );
        });
    
    Target CompileConsoleClient => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("ConsoleClient");

            Directory.CreateDirectory(ConsoleClientCompileDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetVerbosity(DotNetVerbosity.normal)
                    .SetOutputDirectory(ConsoleClientCompileDirectory)
            );
        });
    
    Target CompileWebsite => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("WebSite");

            Directory.CreateDirectory(WebSiteCompileDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetVerbosity(DotNetVerbosity.normal)
                    .SetOutputDirectory(WebSiteCompileDirectory)
            );
        });
    
    Target CompileBackgroundJobs => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("BackgroundJobs");

            Directory.CreateDirectory(BackgroundJobsCompileDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetVerbosity(DotNetVerbosity.normal)
                    .SetOutputDirectory(BackgroundJobsCompileDirectory)
            );
        });
    
    Target CompileLinuxGUI => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(LinuxGuiCompileDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetRuntime("linux-x64")
                    .SetVerbosity(DotNetVerbosity.normal)
                    .EnablePublishTrimmed()
                    .SetOutputDirectory(LinuxGuiCompileDirectory)
            );
        });
    Target CompileWindowsGUI => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(WindowsGuiCompileDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetRuntime("win-x64")
                    .SetVerbosity(DotNetVerbosity.normal)
                    .EnablePublishTrimmed()
                    .SetOutputDirectory(WindowsGuiCompileDirectory)
            );
        });
    
    Target CompileMacGUI => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(MacGuiCompileDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetRuntime("osx-x64")
                    .SetVerbosity(DotNetVerbosity.normal)
                    .EnablePublishTrimmed()
                    .SetOutputDirectory(MacGuiCompileDirectory)
            );
        });
    
    Target Compile => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .DependsOn(CompileApi, CompileWebsite, CompileBackgroundJobs, CompileConsoleClient, CompileLinuxGUI, CompileWindowsGUI, CompileMacGUI)
        .Executes(() =>
        {
        });
    
    Target PackageApi => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        .OnlyWhenDynamic(() =>
        {
            if (Configuration == Configuration.Release) return true;
            if (Directory.Exists(PublishDirectory / "api")) return false;
            return true;
        })
        .Executes(() =>
        {
            var project = Solution.GetProject("API");

            Directory.CreateDirectory(PublishDirectory);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .EnableSelfContained()
                .SetOutput(PublishDirectory / "api")
                .SetVerbosity(DotNetVerbosity.normal)
            );
            

        });
    
    Target PackageConsoleClient => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        .OnlyWhenDynamic(() =>
        {
            if (Configuration == Configuration.Release) return true;
            if (Directory.Exists(PublishDirectory / "consoleClient")) return false;
            return true;
        })
        .Executes(() =>
        {
            var project = Solution.GetProject("ConsoleClient");

            Directory.CreateDirectory(PublishDirectory);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .EnableSelfContained()
                .SetOutput(PublishDirectory / "consoleClient")
                .SetVerbosity(DotNetVerbosity.normal)
            );

        });
    
    Target PackageWebSite => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        //.OnlyWhenStatic(() => Configuration == Configuration.Release)
        .OnlyWhenDynamic(() =>
        {
            if (Configuration == Configuration.Release) return true;
            if (Directory.Exists(PublishDirectory / "WebSite")) return false;
            return true;
        })
        .Executes(() =>
        {
            var project = Solution.GetProject("WebSite");

            Directory.CreateDirectory(PublishDirectory);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .EnableSelfContained()
                .SetOutput(PublishDirectory / "WebSite")
                .SetVerbosity(DotNetVerbosity.normal)
            );

        });
    
    Target PackageBackgroundJobs => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        //.OnlyWhenStatic(() => Configuration == Configuration.Release)
        .OnlyWhenDynamic(() =>
        {
            if (Configuration == Configuration.Release) return true;
            if (Directory.Exists(PublishDirectory / "backgroundjobs")) return false;
            return true;
        })
        .Executes(() =>
        {
            var project = Solution.GetProject("BackgroundJobs");

            Directory.CreateDirectory(PublishDirectory);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .EnableSelfContained()
                .SetOutput(PublishDirectory / "backgroundjobs")
                .SetVerbosity(DotNetVerbosity.normal)
            );

        });
    
    Target PackageWindowsGUI => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        .OnlyWhenDynamic(() =>
        {
            if (Configuration == Configuration.Release) return true;
            if (Directory.Exists(PublishDirectory / "GUIClient")) return false;
            return true;
        })
        .Executes(() =>
        {
            Directory.CreateDirectory(BuildWorkDirectory);
            
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(PublishDirectory);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .SetRuntime("win-x64")
                .EnableSelfContained()
                .EnablePublishSingleFile()
                .SetOutput(PublishDirectory / "GUIClient-Windows")
                .SetVerbosity(DotNetVerbosity.normal)
            );
            
            // CREATING INNO SETUP SCRIPT

            var innoBuilder = BuilderUtils.CreateBuilder(builder =>
            {
                builder.Setup.Create("NetRisk")
                    .AppId("6D5567D6-4CB9-4060-9BFC-6E3113DD362B")
                    .AppVersion(VersionClean)
                    .AppPublisher("NetRisk")
                    .AppPublisherURL("https://www.netrisk.app/")
                    .AppSupportURL("https://www.netrisk.app/")
                    .LicenseFile(RootDirectory / "LICENSE")
                    .DefaultDirName(@"{userappdata}\NetRisk")
                    .PrivilegesRequired(PrivilegesRequired.Lowest)
                    .PrivilegesRequiredOverridesAllowed(SetupPrivilegesRequiredOverrides.Dialog)
                    .OutputBaseFilename("NetRisk-Setup-" + VersionClean)
                    .SetupIconFile(SourceDirectory / "GUIClient" / "Assets" / "NetRisk.ico")
                    //.UninstallDisplayIcon("ToolsIcon.ico")
                    .DisableProgramGroupPage(YesNo.Yes)
                    .OutputDir(PublishDirectory / "GUIClient-Windows-x64-Releases")
                    .Compression("lzma")
                    .WizardStyle(WizardStyle.Modern)
                    .DisableDirPage(YesNo.Yes);
                
                    // Languages
                    builder.Languages.CreateEntry("english", "compiler:Default.isl");
                    builder.Languages.CreateEntry("brazilianportuguese", "compiler:Languages\\BrazilianPortuguese.isl");
                    
                    //Tasks
                    builder.Tasks.CreateEntry(name:"desktopicon",  description: @"{cm:CreateDesktopIcon}").Flags(TaskFlags.Unchecked);
                    
                    // Icons / shortcuts
                    builder.Icons.CreateEntry( @"{autoprograms}\NetRisk", @"{app}\GUIClient.exe");
                    builder.Icons.CreateEntry( @"{autodesktop}\NetRisk", @"{app}\GUIClient.exe").Tasks("desktopicon");
                    

                builder.Files.CreateEntry(source: PublishDirectory / @"GUIClient-Windows\*", destDir: InnoConstants.Directories.App)
                    .Flags(FileFlags.IgnoreVersion | FileFlags.RecurseSubdirs);

            });

            innoBuilder.Build(BuildWorkDirectory / "windows-gui.iss");

            CompileWindowsInstaller(BuildWorkDirectory / "windows-gui.iss");
            

            var checksum = SHA256CheckSum(PublishDirectory / "GUIClient-Windows-x64-Releases"/ $"NetRisk-Setup-{VersionClean}.exe");
            var checksumFile = PublishDirectory /  $"NetRisk-Setup-{VersionClean}.sha256";

            if(File.Exists(checksumFile)) File.Delete(checksumFile);

            File.WriteAllText(checksumFile, checksum);
            

        });

    Target PackageLinuxGUI => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        .OnlyWhenDynamic(() =>
        {
            if (Configuration == Configuration.Release) return true;
            if (Directory.Exists(PublishDirectory / "GUIClient")) return false;
            return true;
        })
        .Executes(() =>
        {
            Directory.CreateDirectory(BuildWorkDirectory);
            
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(PublishDirectory);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .EnableSelfContained()
                .SetRuntime("linux-x64")
                .SetOutput(PublishDirectory / "GUIClient-Linux")
                .SetVerbosity(DotNetVerbosity.normal)
            );
            
            var archive = PublishDirectory / $"GUIClient-Linux-x64-{VersionClean}.zip";

            if(File.Exists(archive)) File.Delete(archive);

            //CompressZip(PublishDirectory / "GUIClient-Linux", archive);
            
            (PublishDirectory / "GUIClient-Linux").ZipTo(archive);
            
            var checksum = SHA256CheckSum(PublishDirectory / $"GUIClient-Linux-x64-{VersionClean}.zip");
            var checksumFile = PublishDirectory /  $"GUIClient-Linux-x64-{VersionClean}.sha256";

            if(File.Exists(checksumFile)) File.Delete(checksumFile);

            File.WriteAllText(checksumFile, checksum);
            
        });

    Target PackageMacGUI => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        .DependsOn(CheckMacBuildHost)
        .OnlyWhenDynamic(() =>
        {
            if (Configuration == Configuration.Release) return true;
            if (Directory.Exists(PublishDirectory / "GUIClient")) return false;
            return true;
        })
        .Executes(() =>
        {
            if (!IsOsx)
            {
                Log.Warning("PackageMacGUI is only supported on macOS.");
                return;
            }

            Directory.CreateDirectory(BuildWorkDirectory);
            
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(PublishDirectory);
            
            if (IsOsx && RuntimeInformation.OSArchitecture == Architecture.Arm64 && IsDockerAvailable())
            {
                Log.Warning("Building macOS x64 on Apple Silicon using Docker (linux/amd64).");
                PublishMacX64WithDocker(project);
            }
            else
            {
                if (IsOsx && RuntimeInformation.OSArchitecture == Architecture.Arm64)
                    Log.Warning("Building macOS x64 on Apple Silicon without Docker. Ensure Rosetta is installed if needed.");

                DotNetPublish(s => s
                    .SetProject(project)
                    .SetVersion(VersionClean)
                    .SetFileVersion(VersionClean)
                    .SetAssemblyVersion(VersionClean)
                    .SetConfiguration(Configuration)
                    .EnableSelfContained()
                    .SetRuntime("osx-x64")
                    .SetOutput(PublishDirectory / "GUIClient-Mac")
                    .SetVerbosity(DotNetVerbosity.normal)
                );
            }

            CreateMacPkgAndDmg(
                PublishDirectory / "GUIClient-Mac",
                "x64",
                VersionClean
            );
        });
    
    Target PackageMacA64GUI => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        .DependsOn(CheckMacBuildHost)
        .OnlyWhenDynamic(() =>
        {
            if (Configuration == Configuration.Release) return true;
            if (Directory.Exists(PublishDirectory / "GUIClient")) return false;
            return true;
        })
        .Executes(() =>
        {
            if (!IsOsx)
            {
                Log.Warning("PackageMacA64GUI is only supported on macOS.");
                return;
            }

            Directory.CreateDirectory(BuildWorkDirectory);
            
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(PublishDirectory);
            
            if (IsOsx && RuntimeInformation.OSArchitecture == Architecture.X64)
                Log.Warning("Building macOS arm64 on Intel host. Validate output on Apple Silicon.");

            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(VersionClean)
                .SetFileVersion(VersionClean)
                .SetAssemblyVersion(VersionClean)
                .SetConfiguration(Configuration)
                .SetRuntime("osx-arm64")
                .EnableSelfContained()
                .SetOutput(PublishDirectory / "GUIClient-MacA64")
                .SetVerbosity(DotNetVerbosity.normal)
            );

            CreateMacPkgAndDmg(
                PublishDirectory / "GUIClient-MacA64",
                "a64",
                VersionClean
            );
        });
    
    Target PackageAll => _ => _
        .DependsOn(PackageApi, PackageConsoleClient, PackageWebSite, PackageBackgroundJobs)
        .Executes(() =>
        {
            
        });

    Target CheckMacBuildHost => _ => _
        .Executes(() =>
        {
            if (!IsOsx)
                return;

            if (RuntimeInformation.OSArchitecture == Architecture.X64)
                Log.Warning("Intel macOS host detected. macOS arm64 build is cross-compiled and should be validated on Apple Silicon.");

            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                Log.Warning("Apple Silicon host detected. macOS x64 build requires Rosetta or Docker cross-publish.");
        });

    Target CleanWorkDir => _ => _
        .Executes(() =>
        {
            if(Directory.Exists(BuildWorkDirectory))
                Directory.Delete(BuildWorkDirectory, true);
        });
    
    
    Target CreateDockerImageApi => _ => _
        .DependsOn(CleanWorkDir)
        .DependsOn(PackageApi)
        .Executes(() =>
        {
            Directory.CreateDirectory(BuildWorkDirectory);
            
            var dockerFile = RootDirectory / "build" / "Docker" / "Dockerfile-API";
            var dockerFileContent = File.ReadAllText(dockerFile);
            var dockerFileContentNew = dockerFileContent.Replace("{{VERSION}}", VersionClean);
            
            var buildDockerFile = BuildWorkDirectory / "Dockerfile-API";
            
            File.WriteAllText(buildDockerFile, dockerFileContentNew);
            
            var entrypointFile = RootDirectory / "build" / "Docker" / "entrypoint-api.sh";
            
            //CopyDirectoryRecursively(PublishDirectory / "api", BuildWorkDirectory / "api");

            (PublishDirectory / "api" ).CopyToDirectory(BuildWorkDirectory);
            
            //CopyDirectoryRecursively(PuppetDirectory / "api", BuildWorkDirectory / "puppet-api");

            (PuppetDirectory / "api" ).Copy(BuildWorkDirectory / "puppet-api", ExistsPolicy.FileOverwrite);

            if (!Directory.Exists(BuildWorkDirectory / "puppet-modules"))
                (PuppetDirectory / "modules").Copy(BuildWorkDirectory / "puppet-modules");
                //CopyDirectoryRecursively(PuppetDirectory / "modules", BuildWorkDirectory / "puppet-modules");
            
            //CopyFile(entrypointFile, BuildWorkDirectory / "entrypoint-api.sh");

            entrypointFile.Copy(BuildWorkDirectory / "entrypoint-api.sh");
            
            DockerTasks.DockerBuild(s => s
                .SetFile(buildDockerFile)
                .SetTag($"ffquintella/netrisk-api:{VersionClean}")
                .SetPath(BuildWorkDirectory)
            );
        });
    
    Target CreateDockerImageBackgroundJobs => _ => _
        .DependsOn(CleanWorkDir)
        .DependsOn(PackageBackgroundJobs)
        .Executes(() =>
        {
            Directory.CreateDirectory(BuildWorkDirectory);
            
            var dockerFile = RootDirectory / "build" / "Docker" / "Dockerfile-BackgroundJobs";
            var dockerFileContent = File.ReadAllText(dockerFile);
            var dockerFileContentNew = dockerFileContent.Replace("{{VERSION}}", VersionClean);
            
            var buildDockerFile = BuildWorkDirectory / "Dockerfile-BackgroundJobs";
            
            File.WriteAllText(buildDockerFile, dockerFileContentNew);
            
            var entrypointFile = RootDirectory / "build" / "Docker" / "entrypoint-backgroundjobs.sh";
            
            //CopyDirectoryRecursively(PublishDirectory / "backgroundjobs", BuildWorkDirectory / "backgroundjobs");
            
            (PublishDirectory / "backgroundjobs").Copy(BuildWorkDirectory / "backgroundjobs", ExistsPolicy.FileOverwrite);
            
            //CopyDirectoryRecursively(PuppetDirectory / "backgroundjobs", BuildWorkDirectory / "puppet-backgroundjobs");
            
            (PuppetDirectory / "backgroundjobs").Copy(BuildWorkDirectory / "puppet-backgroundjobs", ExistsPolicy.FileOverwrite);
            
            
            
            if(!Directory.Exists(BuildWorkDirectory / "puppet-modules"))
                (PuppetDirectory / "modules").Copy(BuildWorkDirectory / "puppet-modules");
                //CopyDirectoryRecursively(PuppetDirectory / "modules", BuildWorkDirectory / "puppet-modules");
            
            entrypointFile.Copy(BuildWorkDirectory / "entrypoint-backgroundjobs.sh", ExistsPolicy.FileOverwrite);
            
            DockerTasks.DockerBuild(s => s
                .SetFile(buildDockerFile)
                .SetTag($"ffquintella/netrisk-backgroundjobs:{VersionClean}")
                .SetPath(BuildWorkDirectory)
            );
        });
    
    Target CreateDockerImageWebSite => _ => _
        .DependsOn(CleanWorkDir)
        .DependsOn(PackageWindowsGUI)
        .DependsOn(PackageLinuxGUI)
        .DependsOn(PackageMacGUI)
        .DependsOn(PackageMacA64GUI)
        .DependsOn(PackageWebSite)
        .Executes(() =>
        {
            Directory.CreateDirectory(BuildWorkDirectory);
            
            var dockerFile = RootDirectory / "build" / "Docker" / "Dockerfile-WebSite";
            var dockerFileContent = File.ReadAllText(dockerFile);
            var dockerFileContentNew = dockerFileContent.Replace("{{VERSION}}", VersionClean);
            
            var buildDockerFile = BuildWorkDirectory / "Dockerfile-WebSite";
            
            File.WriteAllText(buildDockerFile, dockerFileContentNew);
            
            var entrypointFile = RootDirectory / "build" / "Docker" / "entrypoint-website.sh";
            
            (PublishDirectory / "website").Copy(BuildWorkDirectory / "website", ExistsPolicy.FileOverwrite);
            
            entrypointFile.Copy(BuildWorkDirectory / "entrypoint-website.sh", ExistsPolicy.FileOverwrite);

            (PuppetDirectory / "website").Copy(BuildWorkDirectory / "puppet-website", ExistsPolicy.FileOverwrite);

            if(!Directory.Exists(BuildWorkDirectory / "puppet-modules"))
                (PuppetDirectory / "modules").Copy(BuildWorkDirectory / "puppet-modules");
            
            (PublishDirectory / "GUIClient-Windows-x64-Releases" / $"NetRisk-Setup-{VersionClean}.exe").Copy(BuildWorkDirectory / "website" / "wwwroot" / "installers" / "NetRisk-Setup.exe", ExistsPolicy.FileOverwrite);
            
            (PublishDirectory / $"GUIClient-Linux-x64-{VersionClean}.zip").Copy(BuildWorkDirectory / "website" / "wwwroot" / "installers" / "GUIClient-Linux.zip", ExistsPolicy.FileOverwrite);
            
            (PublishDirectory / $"GUIClient-Mac-x64-{VersionClean}.dmg").Copy(BuildWorkDirectory / "website" / "wwwroot" / "installers" / "GUIClient-Mac-x64.dmg", ExistsPolicy.FileOverwrite);
            
            (PublishDirectory / $"GUIClient-Mac-a64-{VersionClean}.dmg").Copy(BuildWorkDirectory / "website" / "wwwroot" / "installers" / "GUIClient-Mac-a64.dmg", ExistsPolicy.FileOverwrite);
            
            DockerTasks.DockerBuild(s => s
                .SetFile(buildDockerFile)
                .SetTag($"ffquintella/netrisk-website:{VersionClean}")
                .SetPath(BuildWorkDirectory)
            );
        });
    Target CreateDockerImageConsoleClient => _ => _
        .DependsOn(PackageConsoleClient)
        .DependsOn(CleanWorkDir)
        .Executes(() =>
        {
            Directory.CreateDirectory(BuildWorkDirectory);
            
            var dockerFile = RootDirectory / "build" / "Docker" / "Dockerfile-ConsoleClient";
            var dockerFileContent = File.ReadAllText(dockerFile);
            var dockerFileContentNew = dockerFileContent.Replace("{{VERSION}}", VersionClean);
            
            var buildDockerFile = BuildWorkDirectory / "Dockerfile-ConsoleClient";
            
            File.WriteAllText(buildDockerFile, dockerFileContentNew);
            
            var entrypointFile = RootDirectory / "build" / "Docker" / "entrypoint-console.sh";
            
            (PublishDirectory / "consoleClient").Copy(BuildWorkDirectory / "console", ExistsPolicy.FileOverwrite);

            (PuppetDirectory / "console").Copy(BuildWorkDirectory / "puppet-console", ExistsPolicy.FileOverwrite);
            if(!Directory.Exists(BuildWorkDirectory / "puppet-modules"))
                (PuppetDirectory / "modules").Copy(BuildWorkDirectory / "puppet-modules");
            
            entrypointFile.Copy(BuildWorkDirectory / "entrypoint-console.sh", ExistsPolicy.FileOverwrite);
            
            DockerTasks.DockerBuild(s => s
                .SetFile(buildDockerFile)
                .SetTag($"ffquintella/netrisk-console:{VersionClean}")
                .SetPath(BuildWorkDirectory)
            );
        });

    Target CreateAllDockerImages => _ => _
        .DependsOn(PackageAll)
        .DependsOn(CreateDockerImageApi)
        .DependsOn(CreateDockerImageWebSite)
        .DependsOn(CreateDockerImageConsoleClient)
        .DependsOn(CreateDockerImageBackgroundJobs)
        .Executes(() =>
        {
        });
    
    
    Target PushDockerImageApi => _ => _
        .DependsOn(CreateDockerImageApi)
        .Executes(() =>
        {
            DockerTasks.DockerPush(s => s
                .SetName($"ffquintella/netrisk-api:{VersionClean}")
            );
        });
    
    Target PushDockerImageBackgroundJobs => _ => _
        .DependsOn(CreateDockerImageBackgroundJobs)
        .Executes(() =>
        {
            DockerTasks.DockerPush(s => s
                .SetName($"ffquintella/netrisk-backgroundjobs:{VersionClean}")
            );
        });
    
    Target PushDockerImageWebSite => _ => _
        .DependsOn(CreateDockerImageWebSite)
        .Executes(() =>
        {
            DockerTasks.DockerPush(s => s
                .SetName($"ffquintella/netrisk-website:{VersionClean}")
            );
        });
    
    Target PushDockerImageConsoleClient => _ => _
        .DependsOn(CreateDockerImageConsoleClient)
        .Executes(() =>
        {
            DockerTasks.DockerPush(s => s
                .SetName($"ffquintella/netrisk-console:{VersionClean}")
            );
        });
    
    Target PushAllDockerImages => _ => _
        .DependsOn(PushDockerImageApi)
        .DependsOn(PushDockerImageWebSite)
        .DependsOn(PushDockerImageConsoleClient)
        .DependsOn(PushDockerImageBackgroundJobs)
        .Executes(() =>
        {
        });

    Target BumpMajor => _ => _
        .Description("Bump major version (X.0.0)")
        .Executes(() =>
        {
            BumpVersion("major");
        });

    Target BumpMinor => _ => _
        .Description("Bump minor version (x.X.0)")
        .Executes(() =>
        {
            BumpVersion("minor");
        });

    Target BumpPatch => _ => _
        .Description("Bump patch version (x.x.X)")
        .Executes(() =>
        {
            BumpVersion("patch");
        });

    Target Bump => _ => _
        .Description("Bump version based on BumpType parameter (major, minor, or patch)")
        .Requires(() => BumpType)
        .Executes(() =>
        {
            if (!new[] { "major", "minor", "patch" }.Contains(BumpType?.ToLower()))
            {
                throw new ArgumentException("BumpType must be one of: major, minor, patch");
            }
            BumpVersion(BumpType.ToLower());
        });

    private void BumpVersion(string bumpType)
    {
        Log.Information("Bumping {BumpType} version...", bumpType);

        // Find all .csproj files in src directory
        var projectFiles = SourceDirectory.GlobFiles("**/*.csproj")
            .Where(f => !f.ToString().Contains("/obj/") && !f.ToString().Contains("/bin/"))
            .ToList();

        if (!projectFiles.Any())
        {
            Log.Warning("No project files found!");
            return;
        }

        // Get current version from first project file
        var firstProject = projectFiles.First();
        var currentVersion = GetVersionFromProject(firstProject);

        // If no version in project files, try to get it from git tags
        if (currentVersion == null)
        {
            currentVersion = GetVersionFromGitTags();
            if (currentVersion != null)
            {
                Log.Information("Using version from git tag: {Version}", currentVersion);
            }
            else
            {
                Log.Warning("No version found in project files or git tags. Using default 0.0.0");
                currentVersion = new Version(0, 0, 0);
            }
        }

        // Calculate new version
        var newVersion = bumpType switch
        {
            "major" => new Version(currentVersion.Major + 1, 0, 0),
            "minor" => new Version(currentVersion.Major, currentVersion.Minor + 1, 0),
            "patch" => new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build + 1),
            _ => throw new ArgumentException($"Invalid bump type: {bumpType}")
        };

        Log.Information("Current version: {CurrentVersion}", currentVersion);
        Log.Information("New version: {NewVersion}", newVersion);

        // Update all project files
        foreach (var projectFile in projectFiles)
        {
            UpdateProjectVersion(projectFile, newVersion);
            Log.Information("Updated {ProjectFile}", projectFile);
        }

        // Update CHANGELOG.md
        UpdateChangelogVersion(newVersion);

        Log.Information("Version bump completed successfully!");
        Log.Information("Updated {Count} project files", projectFiles.Count);
        Log.Information("Don't forget to commit and tag the new version!");
    }

    private Version GetVersionFromProject(string projectFile)
    {
        try
        {
            var doc = XDocument.Load(projectFile);
            var versionElement = doc.Descendants("AssemblyVersion").FirstOrDefault()
                ?? doc.Descendants("Version").FirstOrDefault()
                ?? doc.Descendants("FileVersion").FirstOrDefault();

            if (versionElement != null && !string.IsNullOrEmpty(versionElement.Value))
            {
                return new Version(versionElement.Value);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Error reading version from {ProjectFile}: {Message}", projectFile, ex.Message);
        }

        return null;
    }

    private Version GetLatestProjectVersion()
    {
        try
        {
            if (Solution == null)
                return null;

            Version latest = null;
            foreach (var project in Solution.AllProjects)
            {
                var version = GetVersionFromProject(project.Path);
                if (version == null)
                    continue;

                if (latest == null || version > latest)
                    latest = version;
            }

            return latest;
        }
        catch (Exception ex)
        {
            Log.Warning("Error reading project versions: {Message}", ex.Message);
            return null;
        }
    }

    private Version GetVersionFromGitTags()
    {
        try
        {
            // Use "git.exe" on Windows, "git" on Unix-like systems
            var gitCommand = EnvironmentInfo.IsWin ? "git.exe" : "git";

            // Get all tags that match the Releases/ pattern
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = gitCommand,
                    Arguments = "tag -l \"Releases/*\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = RootDirectory
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    Log.Warning("Git command failed: {Error}", error);
                }
                return null;
            }

            // Parse tags and find the latest version
            var tags = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            Version latestVersion = null;

            foreach (var tag in tags)
            {
                // Extract version from "Releases/X.Y.Z" format
                var versionString = tag.Replace("Releases/", "").Trim();
                try
                {
                    var version = new Version(versionString);
                    if (latestVersion == null || version > latestVersion)
                    {
                        latestVersion = version;
                    }
                }
                catch
                {
                    // Skip invalid version tags
                }
            }

            return latestVersion;
        }
        catch (Exception ex)
        {
            Log.Warning("Error reading version from git tags: {Message}", ex.Message);
            return null;
        }
    }

    private void UpdateProjectVersion(string projectFile, Version newVersion)
    {
        var versionString = $"{newVersion.Major}.{newVersion.Minor}.{newVersion.Build}";

        var doc = XDocument.Load(projectFile);
        var propertyGroup = doc.Descendants("PropertyGroup").FirstOrDefault();

        if (propertyGroup == null)
        {
            Log.Warning("No PropertyGroup found in {ProjectFile}", projectFile);
            return;
        }

        // Update or add AssemblyVersion
        var assemblyVersion = propertyGroup.Element("AssemblyVersion");
        if (assemblyVersion != null)
            assemblyVersion.Value = versionString;
        else
            propertyGroup.Add(new XElement("AssemblyVersion", versionString));

        // Update or add FileVersion
        var fileVersion = propertyGroup.Element("FileVersion");
        if (fileVersion != null)
            fileVersion.Value = versionString;
        else
            propertyGroup.Add(new XElement("FileVersion", versionString));

        // Update or add Version if it exists
        var version = propertyGroup.Element("Version");
        if (version != null)
            version.Value = versionString;

        doc.Save(projectFile);
    }

    private void UpdateChangelogVersion(Version newVersion)
    {
        var changelogPath = RootDirectory / "CHANGELOG.md";

        if (!File.Exists(changelogPath))
        {
            Log.Warning("CHANGELOG.md not found at {Path}", changelogPath);
            return;
        }

        var content = File.ReadAllText(changelogPath);
        var versionString = $"{newVersion.Major}.{newVersion.Minor}.{newVersion.Build}";
        var today = DateTime.Now.ToString("yyyy-MM-dd");

        // Replace "Unreleased" with the new version and date
        var pattern = @"## \[[\d\.]+\] - Unreleased";
        var replacement = $"## [{versionString}] - {today}";

        if (Regex.IsMatch(content, pattern))
        {
            content = Regex.Replace(content, pattern, replacement);

            // Add new Unreleased section at the top
            var newUnreleasedSection = $@"## [{versionString}] - {today}

{content}";

            // Insert new unreleased section after the header
            var headerEndPattern = @"(and this project adheres to \[Semantic Versioning\]\(http://semver\.org/\)\.\s*\n\s*\n)";
            var match = Regex.Match(content, headerEndPattern);

            if (match.Success)
            {
                var newContent = content.Substring(0, match.Index + match.Length) +
                    $"## [NEXT] - Unreleased\n\n" +
                    "This release includes new features and improvements.\n\n" +
                    "### Added\n\n" +
                    "### Changed\n\n" +
                    "### Fixed\n\n\n\n" +
                    content.Substring(match.Index + match.Length);

                File.WriteAllText(changelogPath, newContent);
                Log.Information("Updated CHANGELOG.md with version {Version}", versionString);
            }
            else
            {
                File.WriteAllText(changelogPath, content);
                Log.Information("Updated CHANGELOG.md unreleased version to {Version}", versionString);
            }
        }
        else
        {
            Log.Warning("Could not find Unreleased version in CHANGELOG.md");
        }
    }

    private void CreateMacPkgAndDmg(AbsolutePath publishDirectory, string archLabel, string version)
    {
        const string appName = "NetRisk";
        const string executableName = "GUIClient";
        var bundleRoot = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}-app";
        var appBundle = bundleRoot / $"{appName}.app";
        var contentsDir = appBundle / "Contents";
        var macosDir = contentsDir / "MacOS";
        var resourcesDir = contentsDir / "Resources";

        if (Directory.Exists(bundleRoot))
            Directory.Delete(bundleRoot, true);

        Directory.CreateDirectory(macosDir);
        Directory.CreateDirectory(resourcesDir);

        RunProcess("cp", $"-R \"{publishDirectory}/.\" \"{macosDir}\"", RootDirectory);

        var infoPlistPath = contentsDir / "Info.plist";
        var infoPlist = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
  <key>CFBundleName</key>
  <string>{appName}</string>
  <key>CFBundleDisplayName</key>
  <string>{appName}</string>
  <key>CFBundleIdentifier</key>
  <string>com.netrisk.client</string>
  <key>CFBundleVersion</key>
  <string>{version}</string>
  <key>CFBundleShortVersionString</key>
  <string>{version}</string>
  <key>CFBundleExecutable</key>
  <string>{executableName}</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
</dict>
</plist>
";
        File.WriteAllText(infoPlistPath, infoPlist);

        var executablePath = macosDir / executableName;
        if (File.Exists(executablePath))
            RunProcess("chmod", $"+x \"{executablePath}\"", RootDirectory);

        var pkgRoot = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}-pkgroot";
        if (Directory.Exists(pkgRoot))
            Directory.Delete(pkgRoot, true);
        Directory.CreateDirectory(pkgRoot / "Applications");

        RunProcess("cp", $"-R \"{appBundle}\" \"{pkgRoot / "Applications"}\"", RootDirectory);

        var pkgPath = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}.pkg";
        if (File.Exists(pkgPath))
            File.Delete(pkgPath);

        RunProcess("pkgbuild", $"--root \"{pkgRoot}\" --identifier \"com.netrisk.client\" --version \"{version}\" --install-location / \"{pkgPath}\"", RootDirectory);

        var dmgStaging = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}-dmg";
        if (Directory.Exists(dmgStaging))
            Directory.Delete(dmgStaging, true);
        Directory.CreateDirectory(dmgStaging);

        File.Copy(pkgPath, dmgStaging / $"{appName}.pkg", true);

        var dmgPath = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}.dmg";
        if (File.Exists(dmgPath))
            File.Delete(dmgPath);

        RunProcess("hdiutil", $"create -volname \"{appName}\" -srcfolder \"{dmgStaging}\" -ov -format UDZO \"{dmgPath}\"", RootDirectory);

        var checksum = SHA256CheckSum(dmgPath);
        var checksumFile = PublishDirectory / $"GUIClient-Mac-{archLabel}-{version}.sha256";
        if (File.Exists(checksumFile))
            File.Delete(checksumFile);
        File.WriteAllText(checksumFile, checksum);
    }

    private void PublishMacX64WithDocker(Project project)
    {
        var projectPath = project.Path;
        var outputPath = PublishDirectory / "GUIClient-Mac";
        Directory.CreateDirectory(PublishDirectory);

        var dockerImage = "mcr.microsoft.com/dotnet/sdk:10.0";
        var root = RootDirectory;
        var rootPath = root.ToString();
        var projectRel = Path.GetRelativePath(rootPath, projectPath.ToString());
        var outputRel = Path.GetRelativePath(rootPath, outputPath.ToString());

        var args =
            $"run --rm --platform linux/amd64 " +
            $"-v \"{root}:/repo\" " +
            $"{dockerImage} " +
            $"dotnet publish \"/repo/{projectRel}\" " +
            $"-c {Configuration} -r osx-x64 --self-contained true " +
            $"-p:Version={VersionClean} -p:FileVersion={VersionClean} -p:AssemblyVersion={VersionClean} " +
            $"-o \"/repo/{outputRel}\"";

        RunProcess("docker", args, RootDirectory);
    }

    private void CompileWindowsInstaller(AbsolutePath issPath)
    {
        if (IsWin)
        {
            Iscc(@"/q windows-gui.iss", BuildWorkDirectory);
            return;
        }

        if (!IsDockerAvailable())
            throw new Exception("Windows installer build requires Docker on non-Windows hosts.");

        // In Docker/Wine, unix absolute paths inside .iss are treated as relative.
        // Convert workspace absolute paths to paths relative to workdir.
        var issContent = File.ReadAllText(issPath);
        var rootPrefix = RootDirectory.ToString().TrimEnd('/') + "/";
        issContent = issContent.Replace(rootPrefix, "../");
        File.WriteAllText(issPath, issContent);

        var args =
            $"run --rm --platform linux/amd64 " +
            $"-v \"{RootDirectory}:{RootDirectory}\" " +
            $"-w \"{BuildWorkDirectory}\" " +
            $"amake/innosetup:latest \"{Path.GetFileName(issPath)}\"";

        RunProcess("docker", args, RootDirectory);
    }

    private bool IsDockerAvailable()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "version --format '{{.Server.Version}}'",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void RunProcess(string fileName, string arguments, AbsolutePath workingDirectory)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(output))
            Log.Information("{Output}", output.Trim());

        if (process.ExitCode != 0)
            throw new Exception($"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}: {error}");
    }

    private string SHA256CheckSum(string filePath)
    {
        using (System.Security.Cryptography.SHA256 SHA256 = System.Security.Cryptography.SHA256.Create())
        {
            using (FileStream fileStream = File.OpenRead(filePath))
            {
                var bytes = SHA256.ComputeHash(fileStream);
                // Convert byte array to a string   
                StringBuilder builder = new StringBuilder();  
                for (int i = 0; i < bytes.Length; i++)  
                {  
                    builder.Append(bytes[i].ToString("x2"));  
                }  
                return builder.ToString();  
            }
            
        }
    }

}

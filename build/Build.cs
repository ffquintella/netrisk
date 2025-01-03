using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;

using static Nuke.Common.IO.CompressionTasks;

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
    
    string Version => SourceRepository?.Tags?.LastOrDefault(r => r.ToString().ToLower().StartsWith("releases/")) ?? "Releases/0.50.1";
    string VersionClean
    {
        get
        {
            /*if(SourceRepository == null) Console.WriteLine($"Null Source repository");
            else Console.WriteLine($"Source repository: {SourceRepository.Identifier} " +
                                   $"Tag count:{SourceRepository.Tags.Count} First tag:{SourceRepository.Tags.FirstOrDefault()}");
            Console.WriteLine($"Version used: {Version}");*/
            return Version.ToLower().Substring(9);
        }
    }

    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

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
                    builder.Icons.CreateEntry( @"{autoprograms}\NetRisk", @"{app}\GuiClient.exe");
                    builder.Icons.CreateEntry( @"{autodesktop}\NetRisk", @"{app}\GuiClient.exe").Tasks("desktopicon");
                    

                builder.Files.CreateEntry(source: PublishDirectory / @"GUIClient-Windows\*", destDir: InnoConstants.Directories.App)
                    .Flags(FileFlags.IgnoreVersion | FileFlags.RecurseSubdirs);

            });

            innoBuilder.Build(BuildWorkDirectory / "windows-gui.iss");

            Iscc(@"/q windows-gui.iss", BuildWorkDirectory);
            

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
                .SetRuntime("osx-x64")
                .SetOutput(PublishDirectory / "GUIClient-Mac")
                .SetVerbosity(DotNetVerbosity.normal)
            );
            
            var archive = PublishDirectory / $"GUIClient-Mac-x64-{VersionClean}.zip";

            if(File.Exists(archive)) File.Delete(archive);

            //CompressZip(PublishDirectory / "GUIClient-Mac", archive);
            
            (PublishDirectory / "GUIClient-Mac").ZipTo(archive);
            
            var checksum = SHA256CheckSum(PublishDirectory / $"GUIClient-Mac-x64-{VersionClean}.zip");
            var checksumFile = PublishDirectory /  $"GUIClient-Mac-x64-{VersionClean}.sha256";

            if(File.Exists(checksumFile)) File.Delete(checksumFile);

            File.WriteAllText(checksumFile, checksum);
            
        });
    
    Target PackageMacA64GUI => _ => _
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
                .SetRuntime("osx-arm64")
                .EnableSelfContained()
                .SetOutput(PublishDirectory / "GUIClient-MacA64")
                .SetVerbosity(DotNetVerbosity.normal)
            );
            
            var archive = PublishDirectory / $"GUIClient-Mac-a64-{VersionClean}.zip";

            if(File.Exists(archive)) File.Delete(archive);

            //CompressZip(PublishDirectory / "GUIClient-MacA64", archive);
            
            (PublishDirectory / "GUIClient-MacA64").ZipTo(archive);
            
            var checksum = SHA256CheckSum(PublishDirectory / $"GUIClient-Mac-a64-{VersionClean}.zip");
            var checksumFile = PublishDirectory /  $"GUIClient-Mac-a64-{VersionClean}.sha256";

            if(File.Exists(checksumFile)) File.Delete(checksumFile);

            File.WriteAllText(checksumFile, checksum);
            
        });
    
    Target PackageAll => _ => _
        .DependsOn(PackageApi, PackageConsoleClient, PackageWebSite, PackageBackgroundJobs)
        .Executes(() =>
        {
            
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
            
            CopyDirectoryRecursively(PublishDirectory / "backgroundjobs", BuildWorkDirectory / "backgroundjobs");
            
            CopyDirectoryRecursively(PuppetDirectory / "backgroundjobs", BuildWorkDirectory / "puppet-backgroundjobs");
            if(!Directory.Exists(BuildWorkDirectory / "puppet-modules"))
                CopyDirectoryRecursively(PuppetDirectory / "modules", BuildWorkDirectory / "puppet-modules");
            
            CopyFile(entrypointFile, BuildWorkDirectory / "entrypoint-backgroundjobs.sh");
            
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
            
            CopyDirectoryRecursively(PublishDirectory / "website", BuildWorkDirectory / "website");
            
            CopyFile(entrypointFile, BuildWorkDirectory / "entrypoint-website.sh");
            
            CopyDirectoryRecursively(PuppetDirectory / "website", BuildWorkDirectory / "puppet-website");
            
            if(!Directory.Exists(BuildWorkDirectory / "puppet-modules")) 
                CopyDirectoryRecursively(PuppetDirectory / "modules", BuildWorkDirectory / "puppet-modules");
            
            CopyFile(PublishDirectory / "GUIClient-Windows-x64-Releases"/ $"NetRisk-Setup-{VersionClean}.exe"
                , BuildWorkDirectory / "website" / "wwwroot" / "installers" / "NetRisk-Setup.exe");
            
            CopyFile(PublishDirectory / $"GUIClient-Linux-x64-{VersionClean}.zip"
                , BuildWorkDirectory / "website" / "wwwroot" / "installers" / "GUIClient-Linux.zip");
            
            CopyFile(PublishDirectory / $"GUIClient-Mac-x64-{VersionClean}.zip"
                , BuildWorkDirectory / "website" / "wwwroot" / "installers" / "GUIClient-Mac-x64.zip");
            
            CopyFile(PublishDirectory / $"GUIClient-Mac-a64-{VersionClean}.zip"
                , BuildWorkDirectory / "website" / "wwwroot" / "installers" / "GUIClient-Mac-a64.zip");
            
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
            
            CopyDirectoryRecursively(PublishDirectory / "consoleClient", BuildWorkDirectory / "console");
            
            CopyDirectoryRecursively(PuppetDirectory / "console", BuildWorkDirectory / "puppet-console");
            if(!Directory.Exists(BuildWorkDirectory / "puppet-modules"))
                CopyDirectoryRecursively(PuppetDirectory / "modules", BuildWorkDirectory / "puppet-modules");
            
            CopyFile(entrypointFile, BuildWorkDirectory / "entrypoint-console.sh");
            
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

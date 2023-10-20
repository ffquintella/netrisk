using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using InnoSetup.ScriptBuilder;
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

    [Solution("src/netrisk.sln")]
    readonly Solution Solution;
    
    // TOOLS DEFINITIONS
    
    [NuGetPackage(
        packageId: "Tools.InnoSetup",
        packageExecutable: "iscc.exe")]
    readonly Tool Iscc;
    

    public Build()
    {
        DockerTasks.DockerLogger = (type, text) => Log.Debug(text);
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
                    .SetVerbosity(DotNetVerbosity.Normal)
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
                    .SetVerbosity(DotNetVerbosity.Normal)
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
                    .SetVerbosity(DotNetVerbosity.Normal)
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
                    .SetVerbosity(DotNetVerbosity.Normal)
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
                    .SetVerbosity(DotNetVerbosity.Normal)
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
                    .SetVerbosity(DotNetVerbosity.Normal)
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
                    .SetVerbosity(DotNetVerbosity.Normal)
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
                    .SetRuntime("osx.10.11-x64")
                    .SetVerbosity(DotNetVerbosity.Normal)
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
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(PublishDirectory / "api")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal)
            );
            
            /*
            var archive = OutputPublishDirectory / $"SRNET-Server-lin-x64-{Version}.zip";
            
            if(File.Exists(archive)) File.Delete(archive);
            
            CompressZip(OutputPublishDirectory / "api", 
                archive);

            var checksum = SHA256CheckSum(archive);
            var checksumFile = OutputPublishDirectory / $"SRNET-Server-lin-x64-{Version}.sha256";
            
            if(File.Exists(checksumFile)) File.Delete(checksumFile);
            
            File.WriteAllText(checksumFile, checksum);
            */

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
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .SetOutput(PublishDirectory / "consoleClient")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal)
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
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(PublishDirectory / "WebSite")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal)
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
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(PublishDirectory / "backgroundjobs")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal)
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
                .SetConfiguration(Configuration)
                .SetRuntime("win10-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(PublishDirectory / "GUIClient-Windows")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal)
            );
            
            // CREATING INNO SETUP SCRIPT

            var innoBuilder = BuilderUtils.CreateBuilder(builder =>
            {
                builder.Setup.Create("NetRisk")
                    .AppVersion(VersionClean)
                    .AppPublisher("NetRisk")
                    .AppPublisherURL("https://www.netrisk.app/")
                    .AppSupportURL("https://www.netrisk.app/")
                    .LicenseFile(RootDirectory / "LICENSE")
                    .DefaultDirName(@"{userappdata}\NetRisk")
                    .PrivilegesRequired(PrivilegesRequired.Lowest)
                    .OutputBaseFilename("NetRisk-Setup-"+VersionClean)
                    .SetupIconFile(SourceDirectory / "GUIClient" / "Assets" / "NetRisk.ico")
                    //.UninstallDisplayIcon("ToolsIcon.ico")
                    .DisableProgramGroupPage(YesNo.Yes)
                    .OutputDir(PublishDirectory / "GUIClient-Windows-x64-Releases")
                    .Compression("lzma")
                    .WizardStyle(WizardStyle.Modern)
                    .DisableDirPage(YesNo.Yes);

                builder.Files.CreateEntry(source: PublishDirectory / @"GUIClient-Windows\*", destDir: InnoConstants.Directories.App)
                    .Flags(FileFlags.IgnoreVersion | FileFlags.RecurseSubdirs);

            });

            innoBuilder.Build(BuildWorkDirectory / "windows-gui.iss");

            Iscc(@"/q windows-gui.iss", BuildWorkDirectory);


            /*var archive = PublishDirectory / $"GUIClient-Windows-x64-{Version}.zip";

            if(File.Exists(archive)) File.Delete(archive);

            CompressZip(PublishDirectory / "GUIClient-Windows",
                archive);*/

            var checksum = SHA256CheckSum(PublishDirectory / "GUIClient-Windows-x64-Releases"/ $"NetRisk-Setup-{VersionClean}.exe");
            var checksumFile = PublishDirectory /  $"NetRisk-Setup-{VersionClean}.sha256";

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
            
            CopyDirectoryRecursively(PublishDirectory / "api", BuildWorkDirectory / "api");
            
            CopyDirectoryRecursively(PuppetDirectory / "api", BuildWorkDirectory / "puppet-api");
            if(!Directory.Exists(BuildWorkDirectory / "puppet-modules"))
                CopyDirectoryRecursively(PuppetDirectory / "modules", BuildWorkDirectory / "puppet-modules");
            
            CopyFile(entrypointFile, BuildWorkDirectory / "entrypoint-api.sh");
            
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
            
            //Directory.Delete(BuildWorkDirectory / "puppet-website");
            //Directory.Delete(BuildWorkDirectory / "puppet-modules", true);
            
            CopyDirectoryRecursively(PuppetDirectory / "website", BuildWorkDirectory / "puppet-website");
            
            if(!Directory.Exists(BuildWorkDirectory / "puppet-modules")) 
                CopyDirectoryRecursively(PuppetDirectory / "modules", BuildWorkDirectory / "puppet-modules");
            
            CopyFile(PublishDirectory / "GUIClient-Windows-x64-Releases"/ $"NetRisk-Setup-{VersionClean}.exe"
                , BuildWorkDirectory / "website" / "wwwroot" / "installers" / "NetRisk-Setup.exe");
            
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

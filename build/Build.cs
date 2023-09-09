using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using Nuke.Common.Tools.DotNet;

using Serilog;

class Build : NukeBuild
{

    AbsolutePath SourceDirectory => RootDirectory / "src" ;
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath CompileDirectory => OutputDirectory / "compile";
    AbsolutePath ApiCompileDirectory => CompileDirectory / "api";
    AbsolutePath ConsoleClientCompileDirectory => CompileDirectory / "consoleClient";
    AbsolutePath WebSiteCompileDirectory => CompileDirectory / "website";
    
    AbsolutePath LinuxGuiCompileDirectory => CompileDirectory / "LinuxGUI";
    AbsolutePath WindowsGuiCompileDirectory => CompileDirectory / "WindowsGUI";
    AbsolutePath MacGuiCompileDirectory => CompileDirectory / "MacGUI";
    
    AbsolutePath PublishDirectory => OutputDirectory / "publish";
    AbsolutePath ApiPublishDirectory => PublishDirectory / "api";
    
    [GitRepository] readonly GitRepository SourceRepository;
    
    string Version => SourceRepository?.Tags?.FirstOrDefault(r => r.ToString().ToLower().StartsWith("Releases/")) ?? "Releases/0.50.1";
    string VersionClean => Version.ToLower().Substring(9);
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution("src/netrisk.sln")]
    readonly Solution Solution;
    
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
        .DependsOn(CompileApi, CompileWebsite, CompileConsoleClient, CompileLinuxGUI, CompileWindowsGUI, CompileMacGUI)
        .Executes(() =>
        {
        });
    
    Target PackageApi => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            var project = Solution.GetProject("API");

            Directory.CreateDirectory(ApiPublishDirectory);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(VersionClean)
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(ApiPublishDirectory / "api")
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
    
    Target PackageAll => _ => _
        .DependsOn(PackageApi)
        .Executes(() =>
        {
            
        });

}

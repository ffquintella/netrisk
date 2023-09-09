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
    
    AbsolutePath ApiOutputDirectory => OutputDirectory / "api";
    AbsolutePath ConsoleClientOutputDirectory => OutputDirectory / "consoleClient";
    AbsolutePath WebSiteOutputDirectory => OutputDirectory / "website";
    
    AbsolutePath LinuxGuiOutputDirectory => OutputDirectory / "LinuxGUI";
    AbsolutePath WindowsGuiOutputDirectory => OutputDirectory / "WindowsGUI";
    AbsolutePath MacGuiOutputDirectory => OutputDirectory / "MacGUI";
    
    [GitRepository] readonly GitRepository SourceRepository;
    
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

            Directory.CreateDirectory(ApiOutputDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetVerbosity(DotNetVerbosity.Normal)
                    .SetOutputDirectory(ApiOutputDirectory)
                );
        });
    
    Target CompileConsoleClient => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("ConsoleClient");

            Directory.CreateDirectory(ConsoleClientOutputDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetVerbosity(DotNetVerbosity.Normal)
                    .SetOutputDirectory(ConsoleClientOutputDirectory)
            );
        });
    
    Target CompileWebsite => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("WebSite");

            Directory.CreateDirectory(WebSiteOutputDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetVerbosity(DotNetVerbosity.Normal)
                    .SetOutputDirectory(WebSiteOutputDirectory)
            );
        });
    
    Target CompileLinuxGUI => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(LinuxGuiOutputDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetRuntime("linux-x64")
                    .SetVerbosity(DotNetVerbosity.Normal)
                    .EnablePublishTrimmed()
                    .SetOutputDirectory(LinuxGuiOutputDirectory)
            );
        });
    Target CompileWindowsGUI => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(WindowsGuiOutputDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetRuntime("win-x64")
                    .SetVerbosity(DotNetVerbosity.Normal)
                    .EnablePublishTrimmed()
                    .SetOutputDirectory(WindowsGuiOutputDirectory)
            );
        });
    
    Target CompileMacGUI => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");

            Directory.CreateDirectory(MacGuiOutputDirectory);
            
            DotNetBuild(s => 
                s.SetProjectFile(project)
                    .SetConfiguration(Configuration)
                    .SetRuntime("osx.10.11-x64")
                    .SetVerbosity(DotNetVerbosity.Normal)
                    .EnablePublishTrimmed()
                    .SetOutputDirectory(MacGuiOutputDirectory)
            );
        });
    
    Target Compile => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .DependsOn(CompileApi, CompileWebsite, CompileConsoleClient, CompileLinuxGUI, CompileWindowsGUI, CompileMacGUI)
        .Executes(() =>
        {
        });

}

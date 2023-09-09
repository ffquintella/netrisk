using System;
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
using Serilog;

class Build : NukeBuild
{

    AbsolutePath SourceDirectory => RootDirectory / "src" ;
    AbsolutePath OutputDirectory => RootDirectory / "output";
    
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
        });

    Target Restore => _ => _
        .Executes(() =>
        {
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
        });

}

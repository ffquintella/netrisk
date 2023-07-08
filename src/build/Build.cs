using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using DefaultNamespace;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using RestSharp;
using Serilog;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.IO.CompressionTasks;

class Build : NukeBuild
{

    AbsolutePath SourceDirectory => RootDirectory / "src"  / "net-extras";
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath OutputBuildDirectory => RootDirectory / "output"  / "build";
    AbsolutePath OutputPublishDirectory => RootDirectory / "output"  / "publish";
    
    [GitRepository] readonly GitRepository Repository;
    
    [Parameter] [Secret] readonly string CloudsmithApiKey;
    
    string Version => Repository?.Tags?.FirstOrDefault() ?? "0.0.0";
    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    
#if Linux
    [Solution("net-extras.sln")]
    readonly Solution Solution;
#else
    [Solution("src/net-extras/net-extras.sln")]
    readonly Solution Solution;
#endif
    
    Target Clean => _ => _
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
    
    Target Prepare => _ => _
        .Before(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            if (!Directory.Exists(OutputDirectory)) Directory.CreateDirectory(OutputDirectory);
            if (!Directory.Exists(OutputBuildDirectory)) Directory.CreateDirectory(OutputBuildDirectory);
            if (!Directory.Exists(OutputPublishDirectory)) Directory.CreateDirectory(OutputPublishDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Print)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution)
                .SetVerbosity(DotNetVerbosity.Normal));
        });

    Target Print => _ => _
        .Executes(() =>
        {
            Log.Information("STARTING BUILD");
            Log.Information("SOURCE DIR: {0}", SourceDirectory);
            Log.Information("OUTPUT DIR: {0}", OutputDirectory);
            
            
            Log.Information("Commit = {Value}", Repository.Commit);
            Log.Information("Branch = {Value}", Repository.Branch);
            Log.Information("Tags = {Value}", Repository.Tags);

            Log.Information("main branch = {Value}", Repository.IsOnMainBranch());
            Log.Information("main/master branch = {Value}", Repository.IsOnMainOrMasterBranch());
            
            Log.Information("VersionInfo = {Value}", Version);
            
            Log.Information("Solution path = {Value}", Solution);
            Log.Information("Solution directory = {Value}", Solution.Directory);

            Log.Information("-- PROJECTS --");
            foreach (var project in Solution.Projects)
            {
                Log.Information("=> {Value}", project.Name);
                Log.Information("=> Frameworks:");
                foreach (var framework in project.GetTargetFrameworks())
                {
                    Log.Information("-=> {Value}", framework);    
                }
                
            }
        });
    
    Target Compile => _ => _
        .DependsOn(Print)
        .DependsOn(Prepare)
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetVersion(Version)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputBuildDirectory)
                .SetVerbosity(DotNetVerbosity.Normal));
            
        });
    
    Target PackageApi => _ => _
        .DependsOn(Clean)
        .DependsOn(Prepare)
        .DependsOn(Restore)
        //.DependsOn(Compile)
        .Executes(() =>
        {
            var project = Solution.GetProject("API");
            
            if(Directory.Exists(OutputPublishDirectory / "api"))
                Directory.Delete(OutputPublishDirectory / "api", true);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(Version)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("linux-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(OutputPublishDirectory / "api")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal));
            
            var archive = OutputPublishDirectory / $"SRNET-Server-lin-x64-{Version}.zip";
            
            if(File.Exists(archive)) File.Delete(archive);
            
            CompressZip(OutputPublishDirectory / "api", 
                archive);

            var checksum = SHA256CheckSum(archive);
            var checksumFile = OutputPublishDirectory / $"SRNET-Server-lin-x64-{Version}.sha256";
            
            if(File.Exists(checksumFile)) File.Delete(checksumFile);
            
            File.WriteAllText(checksumFile, checksum);

        });

    Target PackageConsoleClient => _ => _
        .DependsOn(Clean)
        .DependsOn(Prepare)
        .DependsOn(Restore)
        .Executes(() =>
        {
            var project = Solution.GetProject("ConsoleClient");
            
            if(Directory.Exists(OutputPublishDirectory / "consoleClient"))
                Directory.Delete(OutputPublishDirectory / "consoleClient", true);
            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(Version)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("linux-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(OutputPublishDirectory / "consoleClient")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal));
            
            var archive = OutputPublishDirectory / $"SRNET-ConsoleClient-lin-x64-{Version}.zip";
            
            if(File.Exists(archive)) File.Delete(archive);
            
            CompressZip(OutputPublishDirectory / "consoleClient", 
                archive);

            var checksum = SHA256CheckSum(archive);
            var checksumFile = OutputPublishDirectory / $"SRNET-ConsoleClient-lin-x64-{Version}.sha256";
            
            if(File.Exists(checksumFile)) File.Delete(checksumFile);
            
            File.WriteAllText(checksumFile, checksum);

        });
    
    Target PackageGUIClientLinux => _ => _
        .DependsOn(Clean)
        .DependsOn(Prepare)
        .DependsOn(Restore)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");
            
            if(Directory.Exists(OutputPublishDirectory / "guiClient-lin"))
                Directory.Delete(OutputPublishDirectory / "guiClient-lin", true);

            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(Version)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("linux-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(OutputPublishDirectory / "guiClient-lin")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal));
            
            var archive = OutputPublishDirectory / $"SRNET-GUIClient-lin-x64-{Version}.zip";
            
            if(File.Exists(archive)) File.Delete(archive);
            
            CompressZip(OutputPublishDirectory / "guiClient-lin", 
                archive);

            var checksum = SHA256CheckSum(archive);
            var checksumFile = OutputPublishDirectory / $"SRNET-GUIClient-lin-x64-{Version}.sha256";  
            
            if(File.Exists(checksumFile)) File.Delete(checksumFile);
            
            File.WriteAllText(checksumFile, checksum);
        });   

    Target PackageGUIClientOSX => _ => _
        .DependsOn(Clean)
        .DependsOn(Prepare)
        .DependsOn(Restore)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");
            
            if(Directory.Exists(OutputPublishDirectory / "guiClient-osx"))
                Directory.Delete(OutputPublishDirectory / "guiClient-osx", true);

            
            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(Version)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("osx.10.11-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(OutputPublishDirectory / "guiClient-osx")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal));
            
            var archive = OutputPublishDirectory / $"SRNET-GUIClient-osx-x64-{Version}.zip";
            
            if(File.Exists(archive)) File.Delete(archive);
            
            CompressZip(OutputPublishDirectory / "guiClient-osx", 
                archive);

            var checksum = SHA256CheckSum(archive);
            var checksumFile = OutputPublishDirectory / $"SRNET-GUIClient-osx-x64-{Version}.sha256";  
            
            if(File.Exists(checksumFile)) File.Delete(checksumFile);
            
            File.WriteAllText(checksumFile, checksum);
        });  
    
    
    Target PackageGUIClientWin => _ => _
        .DependsOn(Clean)
        .DependsOn(Prepare)
        .DependsOn(Restore)
        .Executes(() =>
        {
            var project = Solution.GetProject("GUIClient");
            
            if(Directory.Exists(OutputPublishDirectory / "guiClient-win"))
                Directory.Delete(OutputPublishDirectory / "guiClient-win", true);
            
            DotNetPublish(s => s
                .SetProject(project)
            .SetVersion(Version)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("win-x64")
                .EnablePublishTrimmed()
                .EnablePublishSingleFile()
                .SetOutput(OutputPublishDirectory / "guiClient-win")
                .EnablePublishReadyToRun()
                .SetVerbosity(DotNetVerbosity.Normal));
                    
            var archive = OutputPublishDirectory / $"SRNET-GUIClient-win-x64-{Version}.zip";
            
            if(File.Exists(archive)) File.Delete(archive);
                    
            CompressZip(OutputPublishDirectory / "guiClient-win", 
                archive);

            var checksum = SHA256CheckSum(archive);
            var checksumFile = OutputPublishDirectory / $"SRNET-GUIClient-win-x64-{Version}.sha256";  
            
            if(File.Exists(checksumFile)) File.Delete(checksumFile);
            
            File.WriteAllText(checksumFile, checksum);
        });

    Target PackageGUIClient => _ => _
        .DependsOn(PackageGUIClientWin, PackageGUIClientLinux, PackageGUIClientOSX)
        .Executes(() =>
        {
        });
    
    Target PackageAll => _ => _
        .DependsOn(PackageApi, PackageConsoleClient, PackageGUIClient)
        .Executes(() =>
        {
        });
    
    Target PublishGUIClientWin => _ => _
        .DependsOn(PackageGUIClientWin)
        .Requires(() => CloudsmithApiKey)
        .Executes(() =>
        {
            var filepath = OutputPublishDirectory / $"SRNET-GUIClient-win-x64-{Version}.zip";
            //var filepath = OutputPublishDirectory / $"SRNET-GUIClient-lin-x64-{Version}.zip";
            UploadFile($"SRNET-GUIClient-win-x64.zip", filepath, 
                "SRNET GUI client for Windows x64", 
                "This is the SRNET GUI client binary for windows x64. The sugested installation method is using docker but if you know what you are doing you can use this binary.",
                Version);
            
        });
    
    Target PublishGUIClientLinux => _ => _
        .DependsOn(PackageGUIClientLinux)
        .Requires(() => CloudsmithApiKey)
        .Executes(() =>
        {
            var filepath = OutputPublishDirectory / $"SRNET-GUIClient-lin-x64-{Version}.zip";
            UploadFile($"SRNET-GUIClient-lin-x64.zip", filepath, 
                "SRNET GUI client for OSX x64", 
                "This is the SRNET GUI client binary for osx x64. The sugested installation method is using docker but if you know what you are doing you can use this binary.",
                Version);
            
        });

    Target PublishGUIClientOSX => _ => _
        .DependsOn(PackageGUIClientOSX)
        .Requires(() => CloudsmithApiKey)
        .Executes(() =>
        {
            var filepath = OutputPublishDirectory / $"SRNET-GUIClient-osx-x64-{Version}.zip";
            UploadFile($"SRNET-GUIClient-osx-x64.zip", filepath, 
                "SRNET GUI client for OSX x64", 
                "This is the SRNET GUI client binary for osx x64. The sugested installation method is using docker but if you know what you are doing you can use this binary.",
                Version);
            
        });
    
    Target PublishGUIClient => _ => _
        .DependsOn(PublishGUIClientLinux, PublishGUIClientWin, PublishGUIClientOSX)
        .Executes(() =>
        {
        });

    Target PublishConsoleClient => _ => _
        .DependsOn(PackageConsoleClient)
        .Requires(() => CloudsmithApiKey)
        .Executes(() =>
        {
            var filepath = OutputPublishDirectory / $"SRNET-ConsoleClient-lin-x64-{Version}.zip";
            UploadFile($"SRNET-ConsoleClient-lin-x64.zip", filepath, 
                "SRNET Console client for Linux x64", 
                "This is the SRNET console client binary for linux x64. The sugested installation method is using docker but if you know what you are doing you can use this binary.",
                Version);
            
        });

    Target PublishApi => _ => _
        .DependsOn(PackageApi)
        .Requires(() => CloudsmithApiKey)
        .Executes(() =>
        {
            var filepath = OutputPublishDirectory / $"SRNET-Server-lin-x64-{Version}.zip";
            UploadFile($"SRNET-Server-lin-x64.zip", filepath, 
                "SRNET Server Linux x64", 
                "This is the SRNET Server binary for linux x64. The sugested installation method is using docker but if you know what you are doing you can use this binary.",
                Version);
            
        });
    
    Target PublishAll => _ => _
        .DependsOn(PublishGUIClient, PublishApi, PublishConsoleClient)
        .Executes(() =>
        {
        });

    private void UploadFile(string filename, string filePath, string summary, string description, string version)
    {
        var restOptions  = new RestClientOptions("https://upload.cloudsmith.io") {
            //RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
        };
        
        var client = new RestClient(restOptions);
        client.AddDefaultHeader("X-Api-Key", CloudsmithApiKey);
        
        
        var request = new RestRequest($"/uox/srnet/{filename}", Method.Put);
        request.AddHeader("Content-Sha256", SHA256CheckSum(filePath));
        var documentBytes = File.ReadAllBytes(filePath);
        request.AddParameter("application/zip", documentBytes, ParameterType.RequestBody);
        //request.RequestFormat = DataFormat.Binary;
        //request.AddFile("content", filePath);

        try
        {
            var uploadResponse = client.Execute<CloudsmithResponse>(request);
            
            if(uploadResponse.StatusCode != HttpStatusCode.OK) throw new Exception($"Upload failed: {uploadResponse.Content}");

            client = new RestClient(new RestClientOptions("https://api-prd.cloudsmith.io"));
            client.AddDefaultHeader("X-Api-Key", CloudsmithApiKey);
            
            var syncRequest = new RestRequest("/v1/packages/uox/srnet/upload/raw/", Method.Post);
            syncRequest.AddHeader("Content-Type", "application/json");
            syncRequest.RequestFormat = DataFormat.Json;
            string body = @"{ ""package_file"": """ + uploadResponse.Data.Identifier + @""",
                ""name"": """ + filename + @""",
                ""description"": """ + description + @""",
                ""summary"": """ + summary + @""",
                ""version"": """ + version + @"""
                }";
            
            syncRequest.AddParameter("text/json", body, ParameterType.RequestBody);            
            
            var syncResponse = client.Execute(syncRequest);
            if(syncResponse.StatusCode != HttpStatusCode.Created) throw new Exception($"Upload failed: {uploadResponse.Content}");
        }
        catch (HttpRequestException exception)
        {
            Log.Error("Error uploading file {0}", exception.Message);
        }
        
        
    }
    
    private string SHA256CheckSum(string filePath)
    {
        using (SHA256 SHA256 = SHA256.Create())
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
                
                
                //return Convert.ToBase64String(SHA256.ComputeHash(fileStream));
                //return BitConverter.ToString(SHA256.ComputeHash(fileStream)).Replace("-", String.Empty).ToLowerInvariant();
        }
    }
}

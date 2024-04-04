using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Cleanup;

public class TmpCleanup: BaseJob, IJob
{
    private IFilesService _filesService;
    
    public TmpCleanup(ILogger logger, DALService dal, IFilesService filesService): base(logger, dal)
    {
        _filesService = filesService;
    }

    public void Run()
    {

        var tmpPath = _filesService.GetUploadDirectory();
        var oldFiles = GetOldFiles(tmpPath);
        
        foreach (var file in oldFiles)
        {
            System.IO.File.Delete(file);
        }
        
        
        Log.Information("Cleaned old files in tmp folder");
    }
    
    private List<string> GetOldFiles(string directoryPath)
    {
        var directoryInfo = new DirectoryInfo(directoryPath);
        var cutoffTime = DateTime.Now.AddHours(-48);

        var oldFiles = directoryInfo.GetFiles()
            .Where(file => file.LastWriteTime <= cutoffTime)
            .Select(file => file.FullName)
            .ToList();

        return oldFiles;
    }
}
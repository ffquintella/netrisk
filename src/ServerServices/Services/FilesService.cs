using DAL;
using Serilog;
using ServerServices.Interfaces;
using File = DAL.Entities.File;

namespace ServerServices.Services;

public class FilesService: ServiceBase, IFilesService
{
    
    public FilesService(ILogger logger, DALManager dalManager): base(logger, dalManager)
    {
        
    }
    
    public List<File> GetAll()
    {
        var dbContext = DALManager.GetContext();
        var files = dbContext.Files.ToList();
        return files;
    }
}
using DAL;
using Model.DTO;
using Serilog;
using ServerServices.Interfaces;
using File = DAL.Entities.File;

namespace ServerServices.Services;

public class FilesService: ServiceBase, IFilesService
{
    
    public FilesService(ILogger logger, DALManager dalManager): base(logger, dalManager)
    {
        
    }
    
    public List<FileListing> GetAll()
    {
        using var dbContext = DALManager.GetContext();
        var files = dbContext.Files.Join(dbContext.FileTypes, file => file.Type,
            fileType => fileType.Value.ToString(),
            (file, fileType) => new FileListing()
            {
                Name = file.Name,
                UniqueName = file.UniqueName,
                Type = fileType.Name,
                Timestamp = file.Timestamp,
                OwnerId = file.User
            }).ToList();
        
        
        /*var innerJoin = from f in dbContext.Files  
            join ft in dbContext.FileTypes on f.Type equals ft.Value  
            select new  
            {  
                Name = f.Name,
                UniqueName = f.UniqueName,
                Type = ft.Name,
                Timestamp = f.Timestamp,
                OwnerId = f.User 
            }; */
        
        return files;
    }
}
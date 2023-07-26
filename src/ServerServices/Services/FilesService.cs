using AutoMapper;
using DAL;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using Tools;
using Tools.Security;
using File = DAL.Entities.File;

namespace ServerServices.Services;

public class FilesService: ServiceBase, IFilesService
{

    private IMapper _mapper;
    
    public FilesService(ILogger logger, DALManager dalManager,
    IMapper mapper
    ): base(logger, dalManager)
    {
        _mapper = mapper;
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

    public File GetByUniqueName(string name)
    {
        using var dbContext = DALManager.GetContext();

        var file = dbContext.Files.FirstOrDefault(f => f.UniqueName == name);
        
        if(file == null) throw new DataNotFoundException("files",name, new Exception("File not found"));

        return file;
    }

    public File Create(File file, User creatingUser)
    {
        var key = RandomGenerator.RandomString(15);
        var hash = HashTool.CreateSha1(file.Name + key);
        
        using var context = DALManager.GetContext();
        file.Id = 0;
        file.Timestamp = DateTime.Now;
        file.User = creatingUser.Value;
        file.UniqueName = hash;
        
        
        try
        {
            var newFile = context.Files.Add(file);
            context.SaveChanges();
            
            return newFile.Entity;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error creating file");
            throw new Exception("Error creating file");
        }
    }

    public void Save(File file)
    {
        using var dbContext = DALManager.GetContext();
        var dbFile = dbContext.Files.FirstOrDefault(f=> f.Id == file.Id);
        
        if(dbFile == null) throw new DataNotFoundException("file", file.Id.ToString());
        
        if(dbFile.UniqueName != file.UniqueName) throw new InvalidOperationException("Cannot change unique name of file");
        
        if(dbFile.Id != file.Id) throw new InvalidOperationException("Cannot change id of file");

        _mapper.Map(file, dbFile);
        dbContext.SaveChanges();
    }

    public List<FileType> GetFileTypes()
    {
        using var dbContext = DALManager.GetContext();
        
        var filestypes = dbContext.FileTypes.ToList();

        return filestypes;

    }
    
    public void DeleteByUniqueName(string name)
    {
        using var dbContext = DALManager.GetContext();
        
        var file = dbContext.Files.FirstOrDefault(f => f.UniqueName == name);
        if(file == null) throw new DataNotFoundException("file", name, new Exception("File not found"));
        dbContext.Files.Remove(file);
        dbContext.SaveChanges();
    }
    
    public List<FileListing> GetRiskFiles(int riskId)
    {
        using var dbContext = DALManager.GetContext();
        
        var files = dbContext.Files.Where(f => f.RiskId == riskId).Join(dbContext.FileTypes, file => file.Type,
            fileType => fileType.Value.ToString(),
            (file, fileType) => new FileListing()
            {
                Name = file.Name,
                UniqueName = file.UniqueName,
                Type = fileType.Name,
                Timestamp = file.Timestamp,
                OwnerId = file.User
            }).ToList();

        return files;
    }
}
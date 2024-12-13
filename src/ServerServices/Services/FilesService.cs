using System.Runtime.InteropServices;
using AutoMapper;
using DAL;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using Serilog;
using ServerServices.Interfaces;
using Tools;
using Tools.Helpers;
using Tools.Security;


namespace ServerServices.Services;

public class FilesService: ServiceBase, IFilesService
{

    private IMapper _mapper;
    private string _baseUploadPath = "";
    
    public FilesService(ILogger logger, IDalService dalService,
    IMapper mapper
    ): base(logger, dalService)
    {
        _mapper = mapper;
        
        
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _baseUploadPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/netrisk-api";
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            _baseUploadPath = Path.Combine( "/tmp/" , "netrisk-api");
        if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            _baseUploadPath = Path.Combine( "/tmp/" , "netrisk-api");
        
        if (!Directory.Exists(_baseUploadPath))
        {
            Directory.CreateDirectory(_baseUploadPath);
        }

    }

    public string GetUploadDirectory()
    {
        return _baseUploadPath;
    }
    
    public void SaveChunk(FileChunk chunk)
    {
        try
        {
            var uploadPath = Path.Combine(_baseUploadPath, chunk.FileId);
            
            var chunkNumber = chunk.ChunkNumber;
            
            var chunkFilePath = Path.Combine(uploadPath, $"{chunkNumber}.part");
            
            // Ensure the upload directory exists
            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }
            
            // Write the chunk to the file system
            System.IO.File.WriteAllBytes(chunkFilePath, Convert.FromBase64String(chunk.ChunkData));
            
        }
        catch (Exception ex)
        {
            throw new Exception("Error saving chunk", ex);
        }
    }
    
    public void CombineChunks(string fileId, int totalChunks)
    {
        var uploadPath = Path.Combine(_baseUploadPath, fileId);
        var finalFilePath = uploadPath + ".dat";
        using (var finalStream = File.Create(finalFilePath))
        {
            for (int i = 1; i <= totalChunks; i++)
            {
                var chunkFilePath = Path.Combine(uploadPath, $"{i}.part");
                using (var chunkStream = File.OpenRead(chunkFilePath))
                {
                    chunkStream.CopyTo(finalStream);
                }
            }
        }
    }

    public void DeleteChunks(string fileId, int totalChunks)
    {
        var uploadPath = Path.Combine(_baseUploadPath, fileId);
        for (int i = 1; i <= totalChunks; i++)
        {
            var chunkFilePath = Path.Combine(uploadPath, $"{i}.part");
            File.Delete(chunkFilePath);
        }
    }

    public int CountChunks(string fileId)
    {
        var uploadPath = Path.Combine(_baseUploadPath, fileId);
        // Get all files in the directory
        var files = System.IO.Directory.GetFiles(uploadPath);

        // Return the number of files
        return files.Length;
    }
    
    public List<FileListing> GetAll()
    {
        using var dbContext = DalService.GetContext();
        var files = dbContext.NrFiles.Join(dbContext.FileTypes, file => file.Type,
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

    public NrFile GetByUniqueName(string name)
    {
        using var dbContext = DalService.GetContext();

        var file = dbContext.NrFiles.FirstOrDefault(f => f.UniqueName == name);
        
        if(file == null) throw new DataNotFoundException("files",name, new Exception("File not found"));

        return file;
    }

    public FileListing Create(NrFile file, User creatingUser)
    {
        var key = RandomGenerator.RandomString(15);
        var hash = HashTool.CreateSha1(file.Name + key);
        
        using var context = DalService.GetContext();
        file.Id = 0;
        file.Timestamp = DateTime.Now;
        file.User = creatingUser.Value;
        file.UniqueName = hash;

        if (file.Name.Length >= 100) file.Name = file.Name.Substring(0, 99);
        
        
        try
        {
            var newFile = context.NrFiles.Add(file);
            context.SaveChanges();
            
            //_mapper.Map<File,FileListing>(newFile.Entity);

            var newFileObj = newFile.Entity;

            var fileListing = new FileListing()
            {
                Name = newFileObj.Name,
                UniqueName = newFileObj.UniqueName,
                OwnerId = newFileObj.User,
                Timestamp = newFileObj.Timestamp,
                Type = GetFileTypes().FirstOrDefault(ft => ft.Value.ToString() == newFileObj.Type)!.Name
            };


            return fileListing;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error creating file");
            throw new Exception("Error creating file");
        }
    }

    public void Save(NrFile file)
    {
        using var dbContext = DalService.GetContext();
        var dbFile = dbContext.NrFiles.FirstOrDefault(f=> f.Id == file.Id);
        
        if(dbFile == null) throw new DataNotFoundException("file", file.Id.ToString());
        
        if(dbFile.UniqueName != file.UniqueName) throw new InvalidOperationException("Cannot change unique name of file");
        
        if(dbFile.Id != file.Id) throw new InvalidOperationException("Cannot change id of file");

        _mapper.Map(file, dbFile);
        dbContext.SaveChanges();
    }

    public List<FileType> GetFileTypes()
    {
        using var dbContext = DalService.GetContext();
        
        var filestypes = dbContext.FileTypes.ToList();

        return filestypes;

    }
    
    public void DeleteByUniqueName(string name)
    {
        using var dbContext = DalService.GetContext();
        
        var file = dbContext.NrFiles.FirstOrDefault(f => f.UniqueName == name);
        if(file == null) throw new DataNotFoundException("file", name, new Exception("File not found"));
        dbContext.NrFiles.Remove(file);
        dbContext.SaveChanges();
    }

    public NrFile GetById(int id)
    {
        using var dbContext = DalService.GetContext();
        
        var file = dbContext.NrFiles.FirstOrDefault(f => f.Id ==id);
        if(file == null) throw new DataNotFoundException("file", id.ToString(), new Exception("File not found"));
        return file;
    }
    
    public List<FileListing> GetRiskFiles(int riskId)
    {
        return AsyncHelper.RunSync(async () =>
            await GetObjectFileListingsAsync(riskId, FileCollectionType.RiskFile));
        
        
        /*using var dbContext = DalService.GetContext();
        
        var files = dbContext.NrFiles.Where(f => f.RiskId == riskId).Join(dbContext.FileTypes, file => file.Type,
            fileType => fileType.Value.ToString(),
            (file, fileType) => new FileListing()
            {
                Name = file.Name,
                UniqueName = file.UniqueName,
                Type = fileType.Name,
                Timestamp = file.Timestamp,
                OwnerId = file.User
            }).ToList();

        return files;*/
    }

    public List<FileListing> GetMitigationFiles(int mittigationId)
    {

        return AsyncHelper.RunSync(async () =>
            await GetObjectFileListingsAsync(mittigationId, FileCollectionType.MitigationFile));

        /*using var dbContext = DalService.GetContext();

        var files = dbContext.NrFiles.Where(f => f.MitigationId == mittigationId).Join(dbContext.FileTypes, file => file.Type,
            fileType => fileType.Value.ToString(),
            (file, fileType) => new FileListing()
            {
                Name = file.Name,
                UniqueName = file.UniqueName,
                Type = fileType.Name,
                Timestamp = file.Timestamp,
                OwnerId = file.User
            }).ToList();

        return files;*/
    }

    public async Task<List<FileListing>> GetObjectFileListingsAsync(int baseId, FileCollectionType collectionType)
    {
        await using var dbContext = DalService.GetContext();

        List<FileListing> result;
        
        switch (collectionType)
        {
            case FileCollectionType.MitigationFile:
                result = await dbContext.NrFiles.Where(f => f.MitigationId == baseId).Join(dbContext.FileTypes, file => file.Type,
                    fileType => fileType.Value.ToString(),
                    (file, fileType) => new FileListing()
                    {
                        Name = file.Name,
                        UniqueName = file.UniqueName,
                        Type = fileType.Name,
                        Timestamp = file.Timestamp,
                        OwnerId = file.User
                    }).ToListAsync();
                break;
            
            case FileCollectionType.RiskFile:
                result = await dbContext.NrFiles.Where(f => f.RiskId == baseId).Join(dbContext.FileTypes, file => file.Type,
                    fileType => fileType.Value.ToString(),
                    (file, fileType) => new FileListing()
                    {
                        Name = file.Name,
                        UniqueName = file.UniqueName,
                        Type = fileType.Name,
                        Timestamp = file.Timestamp,
                        OwnerId = file.User
                    }).ToListAsync();
                break;
            
            case FileCollectionType.IncidentResponsePlanFile:
                result = await dbContext.NrFiles.Where(f => f.IncidentResponsePlanId == baseId).Join(dbContext.FileTypes, file => file.Type,
                    fileType => fileType.Value.ToString(),
                    (file, fileType) => new FileListing()
                    {
                        Name = file.Name,
                        UniqueName = file.UniqueName,
                        Type = fileType.Name,
                        Timestamp = file.Timestamp,
                        OwnerId = file.User
                    }).ToListAsync();
                break;
            
            default:
                throw new ArgumentOutOfRangeException(nameof(collectionType), collectionType, null);
                break;
        }

        return result;
    }
}
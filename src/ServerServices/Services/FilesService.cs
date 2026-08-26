using System.Runtime.InteropServices;
using Mapster;
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

    private string _baseUploadPath = "";

    /// <summary>
    /// Upper bound on chunk numbers. At the client's chunk size this is far more than any real
    /// upload needs; its job is to stop a caller from asking the server to create a million files.
    /// </summary>
    private const int MaxChunksPerUpload = 100_000;
    
    public FilesService(ILogger logger, IDalService dalService
    ): base(logger, dalService)
    {
        
        
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            _baseUploadPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/netrisk-api";
        // Track 7 finding NR-2026-020: staging uploads under /tmp put attacker-readable scan
        // reports in a directory every local account can write to, which invites both disclosure and
        // a symlink swap between the chunk write and the reassembly. /var/netrisk mirrors where
        // EnvironmentService already keeps the installation's key material.
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            _baseUploadPath = Path.Combine("/var/netrisk", "netrisk-api", "uploads");
        if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            _baseUploadPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "netrisk-api", "uploads");

        try
        {
            EnsureUploadRoot();
        }
        catch (Exception ex)
        {
            // A packaged install may not own /var/netrisk yet. Falling back keeps uploads working,
            // but the operator has to know the staging area is now world-writable.
            var fallback = Path.Combine(Path.GetTempPath(), "netrisk-api");
            Logger.Warning(ex,
                "Could not use {Preferred} as the upload staging directory; falling back to {Fallback}, "
                + "which is world-writable. Create {Preferred} owned by the API user",
                _baseUploadPath, fallback, _baseUploadPath);
            _baseUploadPath = fallback;
            EnsureUploadRoot();
        }
    }

    /// <summary>
    /// Creates the staging directory and, on Unix, restricts it to the owning account.
    /// </summary>
    private void EnsureUploadRoot()
    {
        if (!Directory.Exists(_baseUploadPath))
            Directory.CreateDirectory(_baseUploadPath);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            File.SetUnixFileMode(_baseUploadPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    /// <summary>
    /// Resolves the staging directory for one upload.
    ///
    /// Track 7 finding NR-2026-006: the file id arrives in the request body and used to be passed
    /// straight to <c>Path.Combine</c>, so <c>"../../.."</c> let any authenticated user create
    /// directories and write chunk files anywhere the API process could reach — and then have them
    /// reassembled into a <c>.dat</c> file of their choosing. The id is a GUID by construction, so
    /// requiring one path segment of GUID-shaped characters costs nothing.
    /// </summary>
    private string UploadPathFor(string? fileId)
    {
        if (!SafePathTool.IsSafeSegment(fileId))
            throw new InvalidParameterException(nameof(fileId),
                "The file id must be a single path segment of letters, digits, dashes or underscores.");

        return SafePathTool.CombineWithin(_baseUploadPath, fileId!);
    }

    /// <summary>The reassembled-file path for one upload, kept beside its chunk directory.</summary>
    private string CombinedPathFor(string? fileId) => UploadPathFor(fileId) + ".dat";

    public string GetUploadDirectory()
    {
        return _baseUploadPath;
    }
    
    public void SaveChunk(FileChunk chunk)
    {
        try
        {
            var uploadPath = UploadPathFor(chunk.FileId);

            var chunkNumber = chunk.ChunkNumber;

            // A negative or absurd chunk number would still be a safe segment as a string, but it
            // could never be reassembled, so it is rejected here rather than leaving a stray file.
            if (chunkNumber < 1 || chunkNumber > MaxChunksPerUpload)
                throw new InvalidParameterException(nameof(chunk.ChunkNumber),
                    $"The chunk number must be between 1 and {MaxChunksPerUpload}.");

            var chunkFilePath = SafePathTool.CombineWithin(uploadPath, $"{chunkNumber}.part");
            
            // Ensure the upload directory exists
            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }
            
            // Write the chunk to the file system
            System.IO.File.WriteAllBytes(chunkFilePath, Convert.FromBase64String(chunk.ChunkData));
            
        }
        catch (InvalidParameterException)
        {
            // A rejected file id or chunk number is the caller's mistake, not an internal failure,
            // so it must not be flattened into a generic 500.
            throw;
        }
        catch (Exception ex)
        {
            throw new Exception("Error saving chunk", ex);
        }
    }

    public void CombineChunks(string fileId, int totalChunks)
    {
        var uploadPath = UploadPathFor(fileId);
        var finalFilePath = CombinedPathFor(fileId);
        using (var finalStream = File.Create(finalFilePath))
        {
            for (int i = 1; i <= totalChunks; i++)
            {
                var chunkFilePath = SafePathTool.CombineWithin(uploadPath, $"{i}.part");
                using (var chunkStream = File.OpenRead(chunkFilePath))
                {
                    chunkStream.CopyTo(finalStream);
                }
            }
        }
    }

    public void DeleteChunks(string fileId, int totalChunks)
    {
        var uploadPath = UploadPathFor(fileId);
        for (int i = 1; i <= totalChunks; i++)
        {
            var chunkFilePath = SafePathTool.CombineWithin(uploadPath, $"{i}.part");
            File.Delete(chunkFilePath);
        }
    }

    public int CountChunks(string fileId)
    {
        var uploadPath = UploadPathFor(fileId);
        // Get all files in the directory
        var files = System.IO.Directory.GetFiles(uploadPath);

        // Return the number of files
        return files.Length;
    }

    public FileListing CompleteChunkedUpload(NrFile file, string fileId, int totalChunks, User creatingUser)
    {
        var uploadPath = UploadPathFor(fileId);
        var finalFilePath = CombinedPathFor(fileId);

        try
        {
            if (!Directory.Exists(uploadPath))
                throw new DataNotFoundException("file-chunks", fileId,
                    new Exception($"No chunks found for file {fileId}"));

            var receivedChunks = Directory.GetFiles(uploadPath, "*.part").Length;
            if (receivedChunks != totalChunks)
                throw new DataNotFoundException("file-chunks", fileId,
                    new Exception($"Expected {totalChunks} chunks but found {receivedChunks} for file {fileId}"));

            // Reassemble the chunks (1-based, in order) into the final file and load its content.
            CombineChunks(fileId, totalChunks);
            file.Content = System.IO.File.ReadAllBytes(finalFilePath);
            file.Size = file.Content.Length;

            // Persist the DB record and entity association the same way a single-shot upload would.
            return Create(file, creatingUser);
        }
        finally
        {
            // Best-effort cleanup of the temporary chunk directory and combined file.
            try { if (Directory.Exists(uploadPath)) Directory.Delete(uploadPath, true); } catch { /* ignore */ }
            try { if (System.IO.File.Exists(finalFilePath)) System.IO.File.Delete(finalFilePath); } catch { /* ignore */ }
        }
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
        // Track 7 finding NR-2026-017: the unique name is the only thing standing between a user and
        // somebody else's attachment, because Files/{name} has no per-file ownership check. It used
        // to be SHA-1 of the (known) file name plus 15 characters from a predictable generator. A
        // 256-bit CSPRNG token makes the capability itself unguessable, which is what that download
        // route actually relies on until a per-file ACL exists.
        var hash = HashTool.CreateSha256(RandomGenerator.RandomToken(32));
        
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

        file.Adapt(dbFile);
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
            
            case FileCollectionType.IncidentResponsePlanTaskFile:
                result = await dbContext.NrFiles.Where(f => f.IncidentResponsePlanTaskId == baseId).Join(dbContext.FileTypes, file => file.Type,
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
            
            case FileCollectionType.IncidentFile:
                result = await dbContext.NrFiles.Where(f => f.IncidentId == baseId).Join(dbContext.FileTypes, file => file.Type,
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
        }

        return result;
    }
}
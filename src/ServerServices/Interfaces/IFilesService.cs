using DAL.Entities;
using Model.DTO;
using Model.File;


namespace ServerServices.Interfaces;

public interface IFilesService
{

    /// <summary>
    /// Save a file chunk
    /// </summary>
    /// <param name="chunk"></param>
    /// <param name="uploadPath"></param>
    public void SaveChunk(FileChunk chunk);
    
    /// <summary>
    /// Combine all chunks into a single file
    /// </summary>
    /// <param name="fileId"></param>
    /// <param name="totalChunks"></param>
    public void CombineChunks(string fileId, int totalChunks);
    
    /// <summary>
    /// Delete all file chuncks
    /// </summary>
    /// <param name="fileId"></param>
    /// <param name="totalChunks"></param>
    public void DeleteChunks(string fileId, int totalChunks);
    
    /// <summary>
    /// Count how many chunks exists
    /// </summary>
    /// <param name="fileId"></param>
    /// <returns></returns>
    public int CountChunks(string fileId);

    /// <summary>
    /// Reassemble a chunked upload into a single file, persist it (content + entity association)
    /// and clean up the temporary chunks.
    /// </summary>
    /// <param name="file">File metadata (content is taken from the reassembled chunks)</param>
    /// <param name="fileId">The chunk group / upload id</param>
    /// <param name="totalChunks">Number of chunks expected</param>
    /// <param name="creatingUser">The user performing the upload</param>
    /// <returns>The created file listing</returns>
    public FileListing CompleteChunkedUpload(NrFile file, string fileId, int totalChunks, User creatingUser);

    /// <summary>
    /// <see cref="CompleteChunkedUpload(NrFile,string,int,User)"/> with a size bound: the staged chunks
    /// are measured on disk before anything is reassembled, and an upload that is empty or larger than
    /// <paramref name="maxBytes"/> is refused with <see cref="Model.Exceptions.InvalidParameterException"/>
    /// (parameter <c>file</c>). The staged chunks are removed either way.
    /// </summary>
    public FileListing CompleteChunkedUpload(NrFile file, string fileId, int totalChunks, User creatingUser,
        long maxBytes);

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public string GetUploadDirectory();
    
    /// <summary>
    /// List all files
    /// </summary>
    /// <returns>List of files</returns>
    public List<FileListing> GetAll();
    
    /// <summary>
    /// Gets all file types
    /// </summary>
    /// <returns></returns>
    public List<FileType> GetFileTypes();
    
    /// <summary>
    /// Get´s the file by unique name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public NrFile GetByUniqueName(string name);
    
    /// <summary>
    /// Get´s the file by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public NrFile GetById(int id);
    
    /// <summary>
    /// Delete file by unique name
    /// </summary>
    /// <param name="name"></param>
    public void DeleteByUniqueName(string name);
    
    /// <summary>
    /// Creates a new file.
    ///
    /// Only the name, type, view type, content and one parent FK are taken from <paramref name="file"/>;
    /// the id, owner, timestamp, unique name, size and <c>entity_id</c> are set by the server, the entity
    /// always derived from the parent (finding NR-2026-035). It does <b>not</b> decide whether the caller
    /// may attach to that parent — call <see cref="IFileAccessAuthorizer.EnsureCanAttachAsync"/> first.
    /// Throws <see cref="Model.Exceptions.InvalidParameterException"/> for a blank name or more than one
    /// parent.
    /// </summary>
    /// <param name="file">the file object</param>
    /// <param name="creatingUser">The user creating the file</param>
    /// <returns></returns>
    public FileListing Create(NrFile file, User creatingUser);

    /// <summary>
    /// Renames a file — the only change an update may make (finding NR-2026-034).
    ///
    /// The file is located by <c>Id</c> and <c>UniqueName</c> together
    /// (<see cref="Model.Exceptions.DataNotFoundException"/> when they do not match a stored row), and
    /// <paramref name="user"/> has to be the stored row's owner or an administrator
    /// (<see cref="Model.Exceptions.UserNotAuthorizedException"/>) — never the owner the body claims.
    /// Every other field in <paramref name="file"/> is ignored. Assessment evidence is refused with
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public void Save(NrFile file, User user);
    
    
    /// <summary>
    /// List all files associated to a risk
    /// </summary>
    /// <returns>List of files</returns>
    public List<FileListing> GetRiskFiles(int riskId);
    
    /// <summary>
    /// Gets all files associated to a mitigation
    /// </summary>
    /// <param name="mittigationId"></param>
    /// <returns></returns>
    public List<FileListing> GetMitigationFiles(int mittigationId);
    
    
    /// <summary>
    /// Gets all files associated to an object and a collection type
    /// </summary>
    /// <param name="baseId"></param>
    /// <param name="collectionType"></param>
    /// <returns></returns>
    public Task<List<FileListing>> GetObjectFileListingsAsync(int baseId, FileCollectionType collectionType);
}
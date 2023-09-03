using DAL.Entities;
using Model.DTO;
using File = DAL.Entities.File;

namespace ServerServices.Interfaces;

public interface IFilesService
{
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
    public File GetByUniqueName(string name);
    
    /// <summary>
    /// Delete file by unique name
    /// </summary>
    /// <param name="name"></param>
    public void DeleteByUniqueName(string name);
    
    /// <summary>
    /// Creates a new file
    /// </summary>
    /// <param name="file">the file object</param>
    /// <param name="creatingUser">The user creating the file</param>
    /// <returns></returns>
    public FileListing Create(File file, User creatingUser);
    
    /// <summary>
    /// Updates a file
    /// </summary>
    /// <param name="file"></param>
    public void Save(File file);
    
    
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
}
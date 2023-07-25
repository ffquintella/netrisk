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
    /// Get´s the file by unique name
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public File GetByUniqueName(string name);
}
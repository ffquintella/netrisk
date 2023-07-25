using File = DAL.Entities.File;

namespace ServerServices.Interfaces;

public interface IFilesService
{
    /// <summary>
    /// List all files
    /// </summary>
    /// <returns>List of files</returns>
    public List<File> GetAll();
}
using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IFilesService
{
    /// <summary>
    /// Downloads the file from the server
    /// </summary>
    /// <param name="uniqueName"></param>
    /// <param name="filePath"></param>
    public void DownloadFile(string uniqueName, Uri filePath);

    /// <summary>
    /// Deletes the file from the server
    /// </summary>
    /// <param name="uniqueName"></param>
    public void DeleteFile(string uniqueName);
    
    
    /// <summary>
    /// Converts the string representing the mime type to a string representing the extension
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public string ConvertTypeToExtension(string type);
    
    
    /// <summary>
    /// Converts the extenstion back to the mime type
    /// </summary>
    /// <param name="extension"></param>
    /// <returns></returns>
    public string ConvertExtensionToType(string extension);

    /// <summary>
    /// A list of allowed types
    /// </summary>
    public List<FileType> AllowedTypes { get; }
}
using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IFilesService
{
    public void DownloadFile(string uniqueName, Uri filePath);

    public string ConvertTypeToExtension(string type);
    
    public string ConvertExtensionToType(string extension);

    public List<FileType> AllowedTypes { get; }
}
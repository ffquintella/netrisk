namespace ClientServices.Interfaces;

public interface IFilesService
{
    public void DownloadFile(string uniqueName, string filePath);
}
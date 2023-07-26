using ClientServices.Interfaces;
using Model.Exceptions;
using RestSharp;

namespace ClientServices.Services;

public class FilesService: ServiceBase, IFilesService
{
    
    public FilesService(IRestService restService) : base(restService)
    {
        
    }
    
    public void DownloadFile(string uniqueName, string filePath)
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Files/{uniqueName}");

        try
        {
            var response = client.Get<DAL.Entities.File>(request);

            if (response == null)
            {
                _logger.Error("Error downloading file");
                throw new RestComunicationException($"Error downloading file {uniqueName}");
            }
            
            File.WriteAllBytes(filePath, response.Content);
            
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error downloading file message: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
    }
}
using ClientServices.Interfaces;
using DAL.Entities;
using Model.Exceptions;
using RestSharp;
using File = System.IO.File;

namespace ClientServices.Services;

public class FilesService: ServiceBase, IFilesService
{
    private List<FileType> _allowedTypes = new List<FileType>();

    public List<FileType> AllowedTypes => _allowedTypes;

    private readonly IAuthenticationService _authenticationService;
    public FilesService(IRestService restService, IAuthenticationService authenticationService) : base(restService)
    {
        _authenticationService = authenticationService;
        
        _authenticationService.AuthenticationSucceeded += (obj, args) =>
        {
            _allowedTypes = GetTypes();
        };

    }

    public string ConvertExtensionToType(string extension)
    {
        switch (extension)
        {
            case ".csv":
                return "application/csv";
            case ".docx":
                return "application/msword";
            case ".doc":
                return "application/msword";
            case ".bin":
                return "application/octet-stream";
            case ".pdf":
                return "application/pdf";
            case ".odt":
                return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            case ".ods":
                return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            case ".gz":
                return "application/x-gzip";
            case ".gzip":
                return "application/x-gzip";
            case ".pdfx":
                return "application/x-pdf";
            case ".zip":
                return "application/zip";
            case ".gif":
                return "image/gif";
            case ".jpeg":
                return "image/jpeg";
            case ".jpg":
                return "image/jpg";
            case ".png":
                return "image/png";
            case ".pngx":
                return "image/x-png";
            case ".csvs":
                return "text/comma-separated-values";
            case ".txt":
                return "text/plain";
            case ".rtf":
                return "text/rtf";
            case ".xml":
                return "text/xml";
            default:
                return "application/force-download";
                
        }
    }
    
    public string ConvertTypeToExtension(string type)
    {
        switch (type)
        {
            case "application/csv":
                return ".cvs";
            case "application/msword":
                return ".docx" ;
            case "application/octet-stream":
                return ".bin";
            case "application/pdf":
                return ".pdf";
            case "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":
                return ".odt" ;
            case "application/vnd.openxmlformats-officedocument.wordprocessingml.document":
                return ".ods";
            case "application/x-gzip":
                return ".gz";
            case "application/x-pdf":
                return ".gzip";
            case "application/zip":
                return ".zip";
            case "image/gif":
                return ".gif";
            case "image/jpeg":
                return ".jpeg";
            case "image/jpg":
                return ".jpg";
            case "image/png":
                return ".png";
            case "image/x-png":
                return ".pngx";
            case "text/comma-separated-values":
                return ".csv";
            case "text/plain":
                return ".txt";
            case "text/rtf":
                return ".rtf";
            case "text/xml":
                return ".xml";
            default:
                return ".bin";
                
        }
    }
    
    private List<FileType> GetTypes()
    {
        var client = _restService.GetClient();
        
        var request = new RestRequest($"/Files/Types");

        try
        {
            var response = client.Get<List<FileType>>(request);

            if (response == null)
            {
                _logger.Error("Error listing file types");
                throw new RestComunicationException($"Error listing file types ");
            }

            return response;
            
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error downloading file message: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
    }

    public void DeleteFile(string uniqueName)
    {
        var client = _restService.GetClient();
        var request = new RestRequest($"/Files/{uniqueName}");
        
        try
        {
            var response = client.Delete(request);
            if (response == null)
            {
                _logger.Error("Error deleting file");
                throw new RestComunicationException($"Error deleting file {uniqueName}");
            }
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error downloading file message: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
    }
    
    public void DownloadFile(string uniqueName, Uri filePath)
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

            if (Path.HasExtension(filePath.AbsolutePath))
            {
                var extension = Path.GetExtension(filePath.AbsolutePath);
                var type = ConvertExtensionToType(extension);
               
                var fType = AllowedTypes.FirstOrDefault(at => at.Value.ToString() == response.Type);
                if (fType == null) throw new Exception("File type not allowed");
                
                if (type != fType.Name) filePath = new Uri(Path.ChangeExtension(filePath.AbsolutePath, ConvertTypeToExtension(fType.Name)));
            }
            else
            {
                var fType = AllowedTypes.FirstOrDefault(at => at.Value.ToString() == response.Type);
                if (fType == null) throw new Exception("File type not allowed");
                filePath = new Uri(Path.ChangeExtension(filePath.AbsolutePath, ConvertTypeToExtension(fType.Name)));
            }
            
            File.WriteAllBytes(filePath.AbsolutePath, response.Content);
            
            
        }
        catch (HttpRequestException ex)
        {
            _logger.Error("Error downloading file message: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
    }
}
﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using ClientServices.Interfaces;
using DAL.Entities;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Tools.String;
using File = System.IO.File;

namespace ClientServices.Services;

public class FilesRestService: RestServiceBase, IFilesService
{
    private List<FileType> _allowedTypes = new List<FileType>();

    public List<FileType> AllowedTypes => _allowedTypes;

    private readonly IAuthenticationService _authenticationService;
    public FilesRestService(IRestService restService, IAuthenticationService authenticationService) : base(restService)
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
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Files/Types");

        try
        {
            var response = client.Get<List<FileType>>(request);

            if (response == null)
            {
                Logger.Error("Error listing file types");
                throw new RestComunicationException($"Error listing file types ");
            }

            return response;
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error downloading file message: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
    }

    public void DeleteFile(string uniqueName)
    {
        var client = RestService.GetClient();
        var request = new RestRequest($"/Files/{uniqueName}");
        
        try
        {
            var response = client.Delete(request);
            if (response == null)
            {
                Logger.Error("Error deleting file");
                throw new RestComunicationException($"Error deleting file {uniqueName}");
            }
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error downloading file message: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
    }

    public FileListing UploadFile(Uri filePath, int id, int userId, FileUploadType type)
    {
        if (!filePath.IsFile || !File.Exists(filePath.LocalPath)) 
            throw new ArgumentException("Uri is not a file", nameof(filePath));

        
        var content = File.ReadAllBytes(filePath.LocalPath);

        var extension = "";
        if (Path.HasExtension(filePath.AbsolutePath)) extension = Path.GetExtension(filePath.AbsolutePath);

        var ftype = ConvertExtensionToType(extension);

        var typeObj = AllowedTypes.FirstOrDefault(at => at.Name == ftype) ?? AllowedTypes.FirstOrDefault(at => at.Value == 18);

        var newFile = new DAL.Entities.NrFile()
        {
            Id = 0,
            ViewType = 1,
            Name = StringCleaner.CleanEmptyChars(Path.GetFileName(filePath.LocalPath)),
            Size = content.Length,
            Timestamp = DateTime.Now,
            User = userId,
            UniqueName = "",
            Type = typeObj!.Value.ToString(),
            Content = content
        };
        
        switch(type)
        {
            case(FileUploadType.MitigationFile):
                newFile.MitigationId = id;
                break;
            case(FileUploadType.RiskFile):
                newFile.RiskId = id;
                break;
        }
        
        var client = RestService.GetClient();
        var request = new RestRequest($"/Files");

        request.AddJsonBody(newFile);
        
        try
        {
            var response = client.Post<FileListing>(request);
            if (response == null)
            {
                Logger.Error("Error adding file");
                throw new RestComunicationException($"Error adding file");
            }

            return response;

        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error downloading file message: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
    }

    public async Task<NrFile> GetByIdAsync(int id)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Files/Id/{id}");
        try
        {
            var response = await client.GetAsync<NrFile>(request);

            if (response == null)
            {
                Logger.Error("Error getting file");
                throw new RestComunicationException($"Error getting file {id}");
            }

            return response;
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error getting file message: {Message}", ex.Message);
            throw new RestComunicationException("Error getting file", ex);
        }
    }
    
    public void DownloadFile(string uniqueName, Uri filePath)
    {
        var client = RestService.GetClient();
        
        var request = new RestRequest($"/Files/{uniqueName}");

        try
        {
            var response = client.Get<NrFile>(request);

            if (response == null)
            {
                Logger.Error("Error downloading file");
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
            
            File.WriteAllBytes(filePath.LocalPath, response.Content);
            
            
        }
        catch (HttpRequestException ex)
        {
            Logger.Error("Error downloading file message: {Message}", ex.Message);
            throw new RestComunicationException("Error downloading file", ex);
        }
    }
}
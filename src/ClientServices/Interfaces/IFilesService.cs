using System;
using System.Collections.Generic;
using DAL.Entities;
using Model.DTO;
using Model.File;

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
    ///  Get's the file by id
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Task<NrFile> GetByIdAsync(int id);
    
    
    /// <summary>
    /// Uploads a file to the server
    /// </summary>
    /// <param name="filePath"></param>
    /// <param name="id"></param>
    /// <param name="userId"></param>
    /// <param name="type"></param>
    public FileListing UploadFile(Uri filePath, int id, int userId, FileUploadType type);
    
    
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
    
    /// <summary>
    /// Gets a unique local ID
    /// </summary>
    /// <returns></returns>
    public Task<string> GetLocalIdAsync();
    
    /// <summary>
    /// Creates a file chunk
    /// </summary>
    /// <param name="chunk"></param>
    /// <returns></returns>
    public Task CreateChunkAsync(FileChunk chunk);
}
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
    public Task DownloadFileAsync(string uniqueName, Uri filePath);

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
    public Task<FileListing> UploadFileAsync(Uri filePath, int id, int userId, FileCollectionType type);
    
    
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
    //public List<FileType> AllowedTypes { get; }

    /// <summary>
    /// Gets a list of allowed types
    /// </summary>
    public Task<List<FileType>> GetAllowedTypesAsync();
    
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

    /// <summary>
    /// The allowed file type a file is uploaded as, from its extension: the type whose name matches the
    /// extension's MIME type, else the server's generic download type.
    /// </summary>
    /// <exception cref="ClientServices.Exceptions.TypeNotAllowedException">The server allows neither.</exception>
    public Task<FileType> ResolveUploadTypeAsync(string fileName);

    /// <summary>
    /// Stages <paramref name="content"/> on the server in chunks under a fresh upload id, for an endpoint
    /// that completes the upload (<c>/Files/local/complete</c>, or the assessment evidence endpoint).
    /// </summary>
    public Task<StagedUpload> StageUploadAsync(byte[] content);
}
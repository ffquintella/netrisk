using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Model.DTO;
using Model.File;
using ServerServices.Interfaces;
using NrFile = DAL.Entities.NrFile;

namespace ServerServices.Tests.Mock;

public class FilesServiceMock: IFilesService
{
    public void SaveChunk(FileChunk chunk)
    {
       Console.WriteLine("Chunk saved");
    }

    public void CombineChunks(string fileId, int totalChunks)
    {
        Console.WriteLine("Chunks combined");
    }

    public void DeleteChunks(string fileId, int totalChunks)
    {
        Console.WriteLine("Chunks deleted");
    }

    public int CountChunks(string fileId)
    {
        return 1;
    }

    public string GetUploadDirectory()
    {
        return "/tmp/dir1";
    }

    public List<FileListing> GetAll()
    {
        return new System.Collections.Generic.List<FileListing>();
    }

    public List<FileType> GetFileTypes()
    {
        return new System.Collections.Generic.List<FileType>();
    }

    public NrFile GetByUniqueName(string name)
    {
        return new NrFile()
        {
            Id = 1,
            UniqueName = "ABC"
        };
    }

    public NrFile GetById(int id)
    {
        return new NrFile()
        {
            Id = 1,
            UniqueName = "ABC"
        };
    }

    public void DeleteByUniqueName(string name)
    {
        Console.WriteLine("File deleted");
    }

    public FileListing Create(NrFile file, User creatingUser)
    {
        return new FileListing()
        {
            Name = "1",
            OwnerId = 1,
            UniqueName = "ABC"
        };
    }

    public void Save(NrFile file)
    {
        Console.WriteLine("File Saved");
    }

    public List<FileListing> GetRiskFiles(int riskId)
    {
        return new List<FileListing>();
    }

    public List<FileListing> GetMitigationFiles(int mittigationId)
    {
        return new List<FileListing>();
    }

    public Task<List<FileListing>> GetObjectFileListingsAsync(int baseId, FileCollectionType collectionType)
    {
        return Task.FromResult(new List<FileListing>());
    }
}
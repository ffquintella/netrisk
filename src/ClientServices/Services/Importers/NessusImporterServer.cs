using System.Globalization;
using ClientServices.Events;
using ClientServices.Interfaces;
using ClientServices.Interfaces.Importers;
using DAL.Entities;
using Model;
using Model.DTO;
using nessus_tools;
using Tools.Math;
using Tools.Security;
using System.Linq;
using Model.File;

namespace ClientServices.Services.Importers;

public class NessusImporterServer: BaseImporter, IVulnerabilityImporter
{
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    
    private IFilesService FilesService { get; } = GetService<IFilesService>();

    
    public async Task<int> Import(string filePath, bool ignoreNegligible = true)
    {
        const int chunkSize = 10 * 1024 * 1024; // 10 MB
        byte[] buffer = new byte[chunkSize];
        
        var importedVulnerabilities = 0;
        
        // First we need to check if the file exists and read it.
        if (!File.Exists(filePath))
        {
            Logger.Error("File not found: {FilePath}", filePath);
            throw new FileNotFoundException("File not found");
        }
        
        
        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        {
            int bytesRead;
            int filePartNumber = 1;

            var fileUid = await FilesService.GetLocalIdAsync();
            
            FileInfo fileInfo = new FileInfo(filePath);
            long fileSizeInBytes = fileInfo.Length;
            
            var numberOfChunks = (int)(fileSizeInBytes / chunkSize);
            if (fileSizeInBytes % chunkSize > 0)
            {
                numberOfChunks++; // Add one if there is a remainder
            }

            int chunkNumber = 1;
            while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                var chunk = buffer.Take(bytesRead).ToArray();

                var fileChunk = new FileChunk()
                {
                    ChunkNumber = chunkNumber,
                    TotalChunks = numberOfChunks,
                    FileId = fileUid,
                    ChunkData = Convert.ToBase64String(chunk)
                };

                chunkNumber++;

                await FilesService.CreateChunkAsync(fileChunk);
                
            }
        }
        
        
        return importedVulnerabilities;
    }
    
    
}
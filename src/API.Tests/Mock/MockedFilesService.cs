using System.IO;
using DAL.Entities;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

public static class MockedFilesService
{
    public static IFilesService Create()
    {
        var filesService = Substitute.For<IFilesService>();

        /*filesService.GetFileAsync("testFile").Returns(new NrFile()
        {
            Name = "testFile",
            Content = System.Text.Encoding.UTF8.GetBytes("testFile"),
            Size = 1
        });*/

        return filesService;
    }
}
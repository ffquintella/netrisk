using System;
using System.IO;
using BackgroundJobs.Jobs.Cleanup;
using BackgroundJobs.Tests.DI;
using JetBrains.Annotations;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Cleanup;

[TestSubject(typeof(TmpCleanup))]
public class TmpCleanupTest : IDisposable
{
    private readonly DirectoryInfo _uploadDirectory = Directory.CreateTempSubdirectory("netrisk-tmpcleanup");
    private readonly IFilesService _filesService = Substitute.For<IFilesService>();
    private readonly TmpCleanup _job;

    public TmpCleanupTest()
    {
        _filesService.GetUploadDirectory().Returns(_uploadDirectory.FullName);
        _job = new TmpCleanup(TestDoubles.Logger(), TestDoubles.DalService(), _filesService);
    }

    private string WriteFile(string name, TimeSpan age)
    {
        var path = Path.Combine(_uploadDirectory.FullName, name);
        File.WriteAllText(path, "content");
        File.SetLastWriteTime(path, DateTime.Now - age);
        return path;
    }

    [Fact]
    public void TestRunDeletesFilesOlderThanTheCutoff()
    {
        var stale = WriteFile("stale.tmp", TimeSpan.FromHours(72));

        _job.Run();

        Assert.False(File.Exists(stale));
    }

    [Fact]
    public void TestRunKeepsFilesInsideTheCutoff()
    {
        var recent = WriteFile("recent.tmp", TimeSpan.FromHours(1));

        _job.Run();

        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void TestRunKeepsFilesExactlyAtTheBoundary()
    {
        // The cutoff is 48 hours; a file a minute younger than that must survive.
        var borderline = WriteFile("borderline.tmp", TimeSpan.FromHours(48) - TimeSpan.FromMinutes(1));

        _job.Run();

        Assert.True(File.Exists(borderline));
    }

    [Fact]
    public void TestRunOnlyDeletesTheStaleFiles()
    {
        var stale = WriteFile("stale.tmp", TimeSpan.FromDays(10));
        var recent = WriteFile("recent.tmp", TimeSpan.Zero);

        _job.Run();

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent));
        Assert.Single(_uploadDirectory.GetFiles());
    }

    [Fact]
    public void TestRunOnAnEmptyDirectoryIsANoOp()
    {
        _job.Run();

        Assert.Empty(_uploadDirectory.GetFiles());
    }

    [Fact]
    public void TestRunLeavesSubdirectoriesAlone()
    {
        var nested = _uploadDirectory.CreateSubdirectory("keep-me");
        Directory.SetLastWriteTime(nested.FullName, DateTime.Now.AddDays(-30));

        _job.Run();

        Assert.True(Directory.Exists(nested.FullName));
    }

    [Fact]
    public void TestRunFailsWhenTheUploadDirectoryDoesNotExist()
    {
        _filesService.GetUploadDirectory()
            .Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        Assert.Throws<DirectoryNotFoundException>(() => _job.Run());
    }

    public void Dispose()
    {
        if (_uploadDirectory.Exists) _uploadDirectory.Delete(true);
    }
}

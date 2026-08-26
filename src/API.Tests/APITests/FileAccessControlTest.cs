using System;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Per-file access control at the endpoint (security finding NR-2026-017).
///
/// Before this, any authenticated caller who knew a file's <c>unique_name</c> — or, on the
/// <c>id/{id}</c> route, could count to it — could download it regardless of which risk, mitigation,
/// incident or entity it belonged to.
///
/// The authorization *rules* are covered by
/// <c>ServerServices.Tests.Track8.DeferredSecurityFixesInMemoryTest</c>. What is only observable here
/// is that both read routes actually consult the authorizer, and that they consult it **before** the
/// download is logged — otherwise the log records reads that were refused as reads that happened,
/// which is worse than not logging at all.
/// </summary>
[TestSubject(typeof(FilesController))]
public class FileAccessControlTest : BaseControllerTest
{
    private static NrFile NewFile(int id) => new()
    {
        Id = id, Name = $"file-{id}.pdf", UniqueName = $"u-{id}", Size = 3,
        Content = [1, 2, 3], User = 99, Timestamp = DateTime.UtcNow
    };

    /// <summary>
    /// The controller takes an <c>IWebHostEnvironment</c> to decide where uploads land. Nothing in
    /// these tests writes a file, so a substitute is enough — but it has to be registered, because
    /// the container that resolves controllers here has no web host behind it.
    /// </summary>
    private static IWebHostEnvironment NewEnvironment()
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("Development");
        environment.ContentRootPath.Returns(System.IO.Path.GetTempPath());
        environment.WebRootPath.Returns(System.IO.Path.GetTempPath());
        return environment;
    }

    private static (FilesController Controller, IFileAccessAuthorizer Authorizer) Build(NrFile file)
    {
        var files = Substitute.For<IFilesService>();
        files.GetByUniqueName(file.UniqueName).Returns(file);
        files.GetById(file.Id).Returns(file);

        var authorizer = MockedFileAccessAuthorizer.Create();

        var controller = ResolveController<FilesController>(services =>
        {
            services.AddSingleton(files);
            services.AddSingleton(authorizer);
            services.AddSingleton(NewEnvironment());
        });

        return (controller, authorizer);
    }

    [Fact]
    public async Task TestTheUniqueNameRouteConsultsTheAuthorizer()
    {
        var file = NewFile(1);
        var (controller, authorizer) = Build(file);

        var result = await controller.GetByUniqueName(file.UniqueName);

        Assert.IsType<OkObjectResult>(result.Result);
        await authorizer.Received(1).EnsureCanReadAsync(
            Arg.Is<NrFile>(f => f.Id == 1), Arg.Any<User>());
    }

    [Fact]
    public async Task TestTheIdRouteConsultsTheAuthorizer()
    {
        var file = NewFile(2);
        var (controller, authorizer) = Build(file);

        var result = await controller.GetById(file.Id);

        Assert.IsType<OkObjectResult>(result.Result);
        await authorizer.Received(1).EnsureCanReadAsync(
            Arg.Is<NrFile>(f => f.Id == 2), Arg.Any<User>());
    }

    /// <summary>
    /// The negative case, which is the finding. A refusal is a 401 and no file content comes back.
    /// </summary>
    [Fact]
    public async Task TestAFileTheCallerCannotReachIsRefusedOnTheUniqueNameRoute()
    {
        var file = NewFile(MockedFileAccessAuthorizer.ForbiddenFileId);
        var (controller, _) = Build(file);

        var result = await controller.GetByUniqueName(file.UniqueName);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// The enumerable route matters more, because guessing an integer needs no prior knowledge at
    /// all.
    /// </summary>
    [Fact]
    public async Task TestAFileTheCallerCannotReachIsRefusedOnTheEnumerableIdRoute()
    {
        var file = NewFile(MockedFileAccessAuthorizer.ForbiddenFileId);
        var (controller, _) = Build(file);

        var result = await controller.GetById(file.Id);

        Assert.IsType<UnauthorizedResult>(result.Result);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// The id route strips the content even on success — it is a metadata route, and returning the
    /// bytes there made the download log and the actual download disagree.
    /// </summary>
    [Fact]
    public async Task TestTheIdRouteReturnsMetadataWithoutTheContent()
    {
        var file = NewFile(3);
        var (controller, _) = Build(file);

        var ok = Assert.IsType<OkObjectResult>((await controller.GetById(file.Id)).Result);

        Assert.Empty(Assert.IsType<NrFile>(ok.Value).Content);
    }

    /// <summary>
    /// A file that does not exist and a file the caller may not read return the same status. The
    /// alternative distinguishes "no such file" from "not yours", which is an enumeration oracle on a
    /// route whose whole protection is that the name is unguessable.
    /// </summary>
    [Fact]
    public async Task TestAMissingFileAndARefusedFileAreIndistinguishable()
    {
        var files = Substitute.For<IFilesService>();
        files.GetByUniqueName(Arg.Any<string>())
            .Returns(_ => throw new Model.Exceptions.DataNotFoundException("local", "files"));

        var controller = ResolveController<FilesController>(services =>
        {
            services.AddSingleton(files);
            services.AddSingleton(MockedFileAccessAuthorizer.Create());
            services.AddSingleton(NewEnvironment());
        });

        Assert.IsType<UnauthorizedResult>((await controller.GetByUniqueName("u-absent")).Result);
    }
}

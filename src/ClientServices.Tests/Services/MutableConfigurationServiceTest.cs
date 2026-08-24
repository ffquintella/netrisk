using System;
using System.IO;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using JetBrains.Annotations;
using Model.Authentication;
using NSubstitute;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Exercises the real LiteDB-backed store. <see cref="IEnvironmentService"/> is substituted to point
/// at a throwaway directory, so the service writes a genuine encrypted database file — which is the
/// part worth testing, since the encryption password is derived from the device identity.
/// </summary>
[TestSubject(typeof(MutableConfigurationService))]
public class MutableConfigurationServiceTest : IDisposable
{
    private readonly DirectoryInfo _dataFolder = Directory.CreateTempSubdirectory("netrisk-mutableconfig");
    private readonly IMutableConfigurationService _service;

    public MutableConfigurationServiceTest()
    {
        var environment = Substitute.For<IEnvironmentService>();
        environment.ApplicationDataFolder.Returns(_dataFolder.FullName);
        environment.DeviceID.Returns("device-1");
        environment.DeviceToken.Returns("token-1");

        _service = new MutableConfigurationService(environment);
    }

    [Fact]
    public void TestIsInitializedIsFalseBeforeTheDatabaseExists()
    {
        Assert.False(_service.IsInitialized);
    }

    [Fact]
    public void TestInitializeCreatesTheDatabaseAndSeedsTheDeviceId()
    {
        _service.Initialize();

        Assert.True(_service.IsInitialized);
        Assert.True(File.Exists(Path.Combine(_dataFolder.FullName, "configuration.db")));
        Assert.Equal("device-1", _service.GetConfigurationValue("DeviceID"));
    }

    [Fact]
    public void TestInitializeCreatesTheDataFolderWhenItIsMissing()
    {
        var missing = Path.Combine(_dataFolder.FullName, "nested", "deeper");
        var environment = Substitute.For<IEnvironmentService>();
        environment.ApplicationDataFolder.Returns(missing);
        environment.DeviceID.Returns("device-2");
        environment.DeviceToken.Returns("token-2");

        var service = new MutableConfigurationService(environment);
        service.Initialize();

        Assert.True(Directory.Exists(missing));
        Assert.True(service.IsInitialized);
    }

    [Fact]
    public void TestSetConfigurationValueInsertsThenReads()
    {
        _service.SetConfigurationValue("Server", "https://example.invalid");

        Assert.Equal("https://example.invalid", _service.GetConfigurationValue("Server"));
    }

    [Fact]
    public void TestSetConfigurationValueOverwritesAnExistingName()
    {
        _service.SetConfigurationValue("Server", "first");
        _service.SetConfigurationValue("Server", "second");

        Assert.Equal("second", _service.GetConfigurationValue("Server"));
    }

    [Fact]
    public void TestGetConfigurationValueReturnsNullForAnUnknownName()
    {
        _service.Initialize();

        Assert.Null(_service.GetConfigurationValue("NoSuchKey"));
    }

    [Fact]
    public void TestGetConfigurationValueInitializesOnFirstUse()
    {
        // No explicit Initialize(): reading has to create the store, or a fresh install cannot read
        // its own device id.
        Assert.Equal("device-1", _service.GetConfigurationValue("DeviceID"));
        Assert.True(_service.IsInitialized);
    }

    [Fact]
    public async Task TestGetConfigurationValueAsyncReadsTheSameStore()
    {
        _service.SetConfigurationValue("Server", "https://async.invalid");

        Assert.Equal("https://async.invalid", await _service.GetConfigurationValueAsync("Server"));
    }

    [Fact]
    public async Task TestGetConfigurationValueAsyncReturnsNullForAnUnknownName()
    {
        _service.Initialize();

        Assert.Null(await _service.GetConfigurationValueAsync("NoSuchKey"));
    }

    [Fact]
    public void TestRemoveConfigurationValue()
    {
        _service.SetConfigurationValue("Server", "https://example.invalid");
        _service.RemoveConfigurationValue("Server");

        Assert.Null(_service.GetConfigurationValue("Server"));
    }

    [Fact]
    public void TestRemoveConfigurationValueIsANoOpForAnUnknownName()
    {
        _service.SetConfigurationValue("Keep", "kept");
        _service.RemoveConfigurationValue("NoSuchKey");

        Assert.Equal("kept", _service.GetConfigurationValue("Keep"));
    }

    [Fact]
    public void TestSaveAuthenticatedUserThenGet()
    {
        _service.SaveAuthenticatedUser(new AuthenticatedUserInfo
        {
            UserId = 1,
            UserName = "Test User",
            UserAccount = "testUser",
            UserEmail = "testUser@teste.com",
            IsAdmin = true
        });

        var user = _service.GetAuthenticatedUser();

        Assert.NotNull(user);
        Assert.Equal(1, user.UserId);
        Assert.Equal("testUser", user.UserAccount);
        Assert.True(user.IsAdmin);
    }

    [Fact]
    public void TestGetAuthenticatedUserIsNullBeforeOneIsSaved()
    {
        _service.Initialize();

        Assert.Null(_service.GetAuthenticatedUser());
    }

    [Fact]
    public void TestSaveAuthenticatedUserReplacesThePreviousOne()
    {
        _service.SaveAuthenticatedUser(new AuthenticatedUserInfo { UserId = 1, UserAccount = "first" });
        _service.SaveAuthenticatedUser(new AuthenticatedUserInfo { UserId = 1, UserAccount = "second" });

        Assert.Equal("second", _service.GetAuthenticatedUser()!.UserAccount);
    }

    public void Dispose()
    {
        try
        {
            if (_dataFolder.Exists) _dataFolder.Delete(true);
        }
        catch (IOException)
        {
            // LiteDB may still hold the file on some platforms; a leftover temp directory is
            // harmless and must not fail the test run.
        }
    }
}

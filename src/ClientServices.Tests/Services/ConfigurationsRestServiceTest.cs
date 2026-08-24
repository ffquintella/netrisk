using System.Net;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

[TestSubject(typeof(ConfigurationsRestService))]
public class ConfigurationsRestServiceTest : BaseServiceTest
{
    private const string BackupPasswordPath = "/Configurations/BackupPassword";
    private const string WebsiteSyncPath = "/Configurations/WebsiteSync";

    private readonly StubRestBackend _backend = new();
    private readonly IConfigurationsService _service;

    public ConfigurationsRestServiceTest()
    {
        _service = ResolveWith<IConfigurationsService>(_backend);
    }

    // ---------------- BackupPasswordIsSet ----------------

    [Fact]
    public void TestBackupPasswordIsSetReturnsTrueOnOk()
    {
        _backend.OnStatus(Method.Get, BackupPasswordPath, HttpStatusCode.OK);

        Assert.True(_service.BackupPasswordIsSet());
        Assert.Equal("GET " + BackupPasswordPath, _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestBackupPasswordIsSetReturnsFalseOnNotFound()
    {
        _backend.OnStatus(Method.Get, BackupPasswordPath, HttpStatusCode.NotFound);

        Assert.False(_service.BackupPasswordIsSet());
        Assert.True(_backend.Sent(Method.Get, BackupPasswordPath));
    }

    [Fact]
    public void TestBackupPasswordIsSetReturnsFalseOnAnyOtherSuccessStatus()
    {
        // 204 is neither the OK nor the NotFound the method special-cases, so it reaches the
        // final `return false`.
        _backend.OnStatus(Method.Get, BackupPasswordPath, HttpStatusCode.NoContent);

        Assert.False(_service.BackupPasswordIsSet());
    }

    [Fact]
    public void TestBackupPasswordIsSetWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, BackupPasswordPath, HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(() => _service.BackupPasswordIsSet());
        Assert.Equal("checking backup password status", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestBackupPasswordIsSetWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, BackupPasswordPath);

        Assert.Throws<RestComunicationException>(() => _service.BackupPasswordIsSet());
    }

    // ---------------- SetBackupPassword ----------------

    [Fact]
    public void TestSetBackupPasswordPutsThePasswordAsJson()
    {
        _backend.OnStatus(Method.Put, BackupPasswordPath, HttpStatusCode.OK);

        _service.SetBackupPassword("s3cr3t");

        Assert.Equal("PUT", _backend.LastRequest.Method);
        Assert.Equal(BackupPasswordPath, _backend.LastRequest.Path);
        Assert.Contains("s3cr3t", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestSetBackupPasswordThrowsWhenTheServerAnswersSomethingElse()
    {
        // NotFound is a non-throwing status for RestSharp, so the service's own status check is
        // what raises here.
        _backend.OnStatus(Method.Put, BackupPasswordPath, HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(() => _service.SetBackupPassword("x"));
        Assert.Equal("Error setting backup password", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestSetBackupPasswordWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, BackupPasswordPath, HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(() => _service.SetBackupPassword("x"));
        Assert.Equal("checking backup password status", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestSetBackupPasswordWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, BackupPasswordPath);

        Assert.Throws<RestComunicationException>(() => _service.SetBackupPassword("x"));
    }

    // ---------------- GetWebsiteSyncConfig ----------------

    [Fact]
    public void TestGetWebsiteSyncConfigDeserializesTheAnswer()
    {
        _backend.OnGet(WebsiteSyncPath, new WebsiteSyncConfigDto
        {
            IntervalMinutes = 15,
            FastIntervalMinutes = 1,
            Url = "https://site:6443",
            Insecure = true
        });

        var config = _service.GetWebsiteSyncConfig();

        Assert.Equal(15, config.IntervalMinutes);
        Assert.Equal(1, config.FastIntervalMinutes);
        Assert.Equal("https://site:6443", config.Url);
        Assert.True(config.Insecure);
        Assert.Equal("GET " + WebsiteSyncPath, _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetWebsiteSyncConfigFallsBackToDefaultsOnAnEmptyBody()
    {
        _backend.OnStatus(Method.Get, WebsiteSyncPath, HttpStatusCode.OK);

        var config = _service.GetWebsiteSyncConfig();

        Assert.Equal(60, config.IntervalMinutes);
        Assert.Equal(2, config.FastIntervalMinutes);
        Assert.Equal(string.Empty, config.Url);
    }

    [Fact]
    public void TestGetWebsiteSyncConfigFallsBackToDefaultsWhenNotConfiguredOnTheServer()
    {
        _backend.OnStatus(Method.Get, WebsiteSyncPath, HttpStatusCode.NotFound);

        var config = _service.GetWebsiteSyncConfig();

        Assert.Equal(60, config.IntervalMinutes);
    }

    [Fact]
    public void TestGetWebsiteSyncConfigWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, WebsiteSyncPath, HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(() => _service.GetWebsiteSyncConfig());
        Assert.Equal("fetching website sync config", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestGetWebsiteSyncConfigWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, WebsiteSyncPath);

        Assert.Throws<RestComunicationException>(() => _service.GetWebsiteSyncConfig());
    }

    // ---------------- SetWebsiteSyncConfig ----------------

    [Fact]
    public void TestSetWebsiteSyncConfigPutsTheConfig()
    {
        _backend.OnStatus(Method.Put, WebsiteSyncPath, HttpStatusCode.OK);

        _service.SetWebsiteSyncConfig(new WebsiteSyncConfigDto { IntervalMinutes = 30, Url = "https://site:6443" });

        Assert.Equal("PUT", _backend.LastRequest.Method);
        Assert.Equal(WebsiteSyncPath, _backend.LastRequest.Path);
        Assert.Contains("30", _backend.LastRequest.Body);
        Assert.Contains("https://site:6443", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestSetWebsiteSyncConfigThrowsWhenTheServerAnswersSomethingElse()
    {
        _backend.OnStatus(Method.Put, WebsiteSyncPath, HttpStatusCode.NotFound);

        var ex = Assert.Throws<RestComunicationException>(
            () => _service.SetWebsiteSyncConfig(new WebsiteSyncConfigDto()));
        Assert.Equal("Error setting website sync config", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestSetWebsiteSyncConfigWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, WebsiteSyncPath, HttpStatusCode.InternalServerError);

        var ex = Assert.Throws<RestComunicationException>(
            () => _service.SetWebsiteSyncConfig(new WebsiteSyncConfigDto()));
        Assert.Equal("setting website sync config", ex.RestExceptionMessage);
    }

    [Fact]
    public void TestSetWebsiteSyncConfigWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, WebsiteSyncPath);

        Assert.Throws<RestComunicationException>(
            () => _service.SetWebsiteSyncConfig(new WebsiteSyncConfigDto()));
    }
}

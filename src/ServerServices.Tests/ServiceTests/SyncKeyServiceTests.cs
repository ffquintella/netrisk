using System;
using System.IO;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using SyncContracts;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class SyncKeyServiceTests : IDisposable
{
    private sealed class TempEnvironment : IEnvironmentService
    {
        public TempEnvironment(string folder) => ApplicationDataFolder = folder;
        public string ServerSecretToken => "test";
        public string ApplicationDataFolder { get; }
    }

    private readonly string _folder;
    private readonly SyncKeyService _service;

    public SyncKeyServiceTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "nrsynckeys_" + Guid.NewGuid().ToString("N"));
        var logger = new LoggerConfiguration().CreateLogger();
        _service = new SyncKeyService(logger, new TempEnvironment(_folder));
    }

    [Fact]
    public void CreateThenSignVerifiesWithPublicKey()
    {
        _service.CreateKeyPair();
        Assert.True(_service.KeyExists());

        var body = new byte[] { 1, 2, 3 };
        var sig = _service.Sign("POST", "/sync/push", 42, "n", body);

        Assert.True(SyncSignature.Verify(_service.GetPublicKeyPem(), "POST", "/sync/push", 42, "n", body, sig));
    }

    [Fact]
    public void CreateTwiceWithoutForceThrows()
    {
        _service.CreateKeyPair();
        Assert.Throws<InvalidOperationException>(() => _service.CreateKeyPair());
        // With force it succeeds and replaces the key.
        var oldId = _service.GetKeyId();
        _service.CreateKeyPair(force: true);
        Assert.NotEqual(oldId, _service.GetKeyId());
    }

    [Fact]
    public void RotationPromotesNewKey()
    {
        _service.CreateKeyPair();
        var oldId = _service.GetKeyId();

        var next = _service.PrepareRotation();
        // Until committed, the active key is unchanged.
        Assert.Equal(oldId, _service.GetKeyId());

        _service.CommitRotation(next.KeyId, next.PublicKeyPem, next.PrivateKeyPem);
        Assert.Equal(next.KeyId, _service.GetKeyId());
        Assert.Equal(next.PublicKeyPem, _service.GetPublicKeyPem());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }
}

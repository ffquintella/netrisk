using System.Security.Cryptography;
using Serilog;
using ServerServices.Interfaces;
using SyncContracts;

namespace ServerServices.Services;

public class SyncKeyService : ISyncKeyService
{
    private readonly ILogger _logger;
    private readonly IEnvironmentService _environmentService;

    public SyncKeyService(ILogger logger, IEnvironmentService environmentService)
    {
        _logger = logger;
        _environmentService = environmentService;
    }

    private string KeyFolder => Path.Combine(_environmentService.ApplicationDataFolder, "sync");
    private string PrivateKeyPath => Path.Combine(KeyFolder, "sync_signing_key.pem");
    private string PublicKeyPath => Path.Combine(KeyFolder, "sync_signing_pub.pem");
    private string KeyIdPath => Path.Combine(KeyFolder, "sync_key_id.txt");

    public bool KeyExists() => File.Exists(PrivateKeyPath) && File.Exists(KeyIdPath);

    public string GetKeyId()
    {
        if (!KeyExists()) throw new InvalidOperationException("Sync signing key has not been created. Run 'keys create'.");
        return File.ReadAllText(KeyIdPath).Trim();
    }

    public string GetPublicKeyPem()
    {
        if (!File.Exists(PublicKeyPath)) throw new InvalidOperationException("Sync public key not found. Run 'keys create'.");
        return File.ReadAllText(PublicKeyPath);
    }

    private string GetPrivateKeyPem()
    {
        if (!File.Exists(PrivateKeyPath)) throw new InvalidOperationException("Sync private key not found. Run 'keys create'.");
        return File.ReadAllText(PrivateKeyPath);
    }

    public void CreateKeyPair(bool force = false)
    {
        if (KeyExists() && !force)
            throw new InvalidOperationException("A sync signing key already exists. Use --force to overwrite (this invalidates the current enrollment).");

        var (keyId, publicPem, privatePem) = Generate();
        Persist(keyId, publicPem, privatePem);
        _logger.Information("Created sync signing key {KeyId}", keyId);
    }

    public (string KeyId, string PublicKeyPem, string PrivateKeyPem) PrepareRotation()
    {
        return Generate();
    }

    public void CommitRotation(string keyId, string publicKeyPem, string privateKeyPem)
    {
        Persist(keyId, publicKeyPem, privateKeyPem);
        _logger.Information("Rotated sync signing key to {KeyId}", keyId);
    }

    public string Sign(string method, string path, long timestamp, string nonce, byte[] body)
    {
        return SyncSignature.Sign(GetPrivateKeyPem(), method, path, timestamp, nonce, body);
    }

    private static (string KeyId, string PublicKeyPem, string PrivateKeyPem) Generate()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = Guid.NewGuid().ToString("N")[..16];
        return (keyId, ecdsa.ExportSubjectPublicKeyInfoPem(), ecdsa.ExportPkcs8PrivateKeyPem());
    }

    private void Persist(string keyId, string publicPem, string privatePem)
    {
        Directory.CreateDirectory(KeyFolder);
        File.WriteAllText(PrivateKeyPath, privatePem);
        File.WriteAllText(PublicKeyPath, publicPem);
        File.WriteAllText(KeyIdPath, keyId);
        TryRestrictPermissions(PrivateKeyPath);
    }

    private void TryRestrictPermissions(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Could not restrict permissions on {Path}", path);
        }
    }
}

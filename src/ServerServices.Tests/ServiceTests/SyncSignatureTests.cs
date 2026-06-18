using System.Security.Cryptography;
using System.Text;
using SyncContracts;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class SyncSignatureTests
{
    private static (string priv, string pub) NewKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportPkcs8PrivateKeyPem(), ecdsa.ExportSubjectPublicKeyInfoPem());
    }

    [Fact]
    public void ValidSignatureVerifies()
    {
        var (priv, pub) = NewKeyPair();
        var body = Encoding.UTF8.GetBytes("{\"hello\":1}");
        var sig = SyncSignature.Sign(priv, "POST", "/sync/push", 100, "nonce1", body);

        Assert.True(SyncSignature.Verify(pub, "POST", "/sync/push", 100, "nonce1", body, sig));
    }

    [Fact]
    public void TamperedBodyFailsVerification()
    {
        var (priv, pub) = NewKeyPair();
        var body = Encoding.UTF8.GetBytes("{\"hello\":1}");
        var sig = SyncSignature.Sign(priv, "POST", "/sync/push", 100, "nonce1", body);

        var tampered = Encoding.UTF8.GetBytes("{\"hello\":2}");
        Assert.False(SyncSignature.Verify(pub, "POST", "/sync/push", 100, "nonce1", tampered, sig));
    }

    [Fact]
    public void DifferentPathFailsVerification()
    {
        var (priv, pub) = NewKeyPair();
        var body = Encoding.UTF8.GetBytes("payload");
        var sig = SyncSignature.Sign(priv, "POST", "/sync/push", 100, "nonce1", body);

        Assert.False(SyncSignature.Verify(pub, "POST", "/sync/fast", 100, "nonce1", body, sig));
    }

    [Fact]
    public void WrongKeyFailsVerification()
    {
        var (priv, _) = NewKeyPair();
        var (_, otherPub) = NewKeyPair();
        var body = Encoding.UTF8.GetBytes("payload");
        var sig = SyncSignature.Sign(priv, "POST", "/sync/push", 100, "nonce1", body);

        Assert.False(SyncSignature.Verify(otherPub, "POST", "/sync/push", 100, "nonce1", body, sig));
    }
}

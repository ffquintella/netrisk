using System;
using System.Text;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace Tools.Criptography;

public static class ECC
{
    
    public static (byte[] ciphertext, byte[] ephemeralPublicKey) Encrypt(string plaintext, byte[] passwordBytes)
    {
        // Get curve and domain parameters
        var curve = ECNamedCurveTable.GetByName("secp256r1");
        var domainParams = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);

        // Derive recipient key from password using SHA-256
        var digest = new Sha256Digest();
        digest.BlockUpdate(passwordBytes, 0, passwordBytes.Length);
        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        var d = new Org.BouncyCastle.Math.BigInteger(1, hash).Mod(curve.N);

        var recipientPrivateKey = new ECPrivateKeyParameters(d, domainParams);
        var recipientPublicKey = new ECPublicKeyParameters(domainParams.G.Multiply(d), domainParams);

        // Generate ephemeral key pair
        var keyGen = new ECKeyPairGenerator();
        var keyGenParams = new ECKeyGenerationParameters(domainParams, new SecureRandom());
        keyGen.Init(keyGenParams);
        AsymmetricCipherKeyPair ephemeralKeyPair = keyGen.GenerateKeyPair();
        var ephemeralPrivateKey = (ECPrivateKeyParameters)ephemeralKeyPair.Private;
        var ephemeralPublicKey = (ECPublicKeyParameters)ephemeralKeyPair.Public;

        // Encrypt using ECIES
        var iesEngine = new IesEngine(
            new ECDHBasicAgreement(),
            new Kdf2BytesGenerator(new Sha256Digest()),
            new HMac(new Sha256Digest())
        );

        //var iesParams = new IesParameters(null, null, 128);
        
        // Example non-null vectors (can be empty or random for your use case)
        byte[] derivation = Encoding.UTF8.GetBytes("shared-derivation");
        byte[] encoding = Encoding.UTF8.GetBytes("shared-encoding");
        var iesParams = new IesParameters(derivation, encoding, 128);
        
        iesEngine.Init(true, ephemeralPrivateKey, recipientPublicKey, iesParams);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] ciphertext = iesEngine.ProcessBlock(plaintextBytes, 0, plaintextBytes.Length);

        return (ciphertext, ephemeralPublicKey.Q.GetEncoded());
    }
    
    public static (byte[] ciphertext, byte[] ephemeralPublicKey) Encrypt(string plaintext, string password)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        return Encrypt(plaintext, passwordBytes);
    }
    
    public static string Decrypt(byte[] passwordBytes, byte[] ephemeralPublicKeyBytes, byte[] ciphertext)
    {

        
        var curve = ECNamedCurveTable.GetByName("secp256r1");
        var domainParams = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H);

        // Derive recipient private key from password
        var digest = new Sha256Digest();
        digest.BlockUpdate(passwordBytes, 0, passwordBytes.Length);
        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        var d = new Org.BouncyCastle.Math.BigInteger(1, hash).Mod(curve.N);
        var recipientPrivateKey = new ECPrivateKeyParameters(d, domainParams);

        // Reconstruct ephemeral public key
        var q = curve.Curve.DecodePoint(ephemeralPublicKeyBytes);
        var ephemeralPublicKey = new ECPublicKeyParameters(q, domainParams);

        // Setup ECIES engine for decryption
        var iesEngine = new IesEngine(
            new ECDHBasicAgreement(),
            new Kdf2BytesGenerator(new Sha256Digest()),
            new HMac(new Sha256Digest())
        );

        // Example non-null vectors (can be empty or random for your use case)
        byte[] derivation = Encoding.UTF8.GetBytes("shared-derivation");
        byte[] encoding = Encoding.UTF8.GetBytes("shared-encoding");
        
        if (ciphertext == null || ciphertext.Length == 0)
            throw new ArgumentException("Ciphertext is empty or null.");

        if (ephemeralPublicKeyBytes == null || ephemeralPublicKeyBytes.Length == 0)
            throw new ArgumentException("Ephemeral key is missing.");

        if (!Encoding.UTF8.GetString(derivation).Equals("shared-derivation"))
            throw new InvalidOperationException("Derivation vector mismatch.");
        
        var iesParams = new IesParameters(derivation, encoding, 128);
        //var iesParams = new IesParameters(null, null, 128);
        
        iesEngine.Init(false, recipientPrivateKey, ephemeralPublicKey, iesParams);

        byte[] plaintextBytes = iesEngine.ProcessBlock(ciphertext, 0, ciphertext.Length);
        return Encoding.UTF8.GetString(plaintextBytes);
    }
    
    public static string Decrypt(string password, byte[] ephemeralPublicKeyBytes, byte[] ciphertext)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        return Decrypt(passwordBytes, ephemeralPublicKeyBytes, ciphertext);
    }
}
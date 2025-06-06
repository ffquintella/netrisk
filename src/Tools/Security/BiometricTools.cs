using System.Security.Cryptography;
using System.Text;

namespace Tools.Security;

public static class BiometricTools
{
    /// <summary>
    /// Creates a unique “biometric anchor” from:
    ///   1) A cryptographic seed (used as HMAC key).
    ///   2) A biometric template (e.g., a fingerprint hash).
    ///   3) A string containing transaction data (e.g., transaction ID, timestamp, operation type).
    ///
    /// The result is an HMAC-SHA256 over the concatenation:
    ///     HMAC_SHA256(
    ///         key = seed,
    ///         message = biometricTemplate ∥ UTF8(transactionData)
    ///     )
    ///
    /// Returns the result encoded in Base64 for easy storage/transmission.
    /// </summary>
    /// <param name="seed">
    ///   The cryptographic seed (HMAC key). Must be kept secret.
    /// </param>
    /// <param name="biometricTemplate">
    ///   A byte array containing the biometric template (e.g., a precomputed hash of the biometric data).
    /// </param>
    /// <param name="transactionData">
    ///   A string with the transaction data (e.g., "OrderId=12345;Timestamp=2025-06-10T14:30:00Z;User=ABC").
    ///   It is recommended to use a canonical format (JSON, XML, or similar) to ensure consistency.
    /// </param>
    /// <returns>
    ///   A Base64 string representing the HMAC-SHA256 of the input, serving as the “biometric anchor.”
    ///   Throws ArgumentException if any input is null or empty.
    /// </returns>
    public static string CreateBiometricAnchor(
        byte[] seed,
        byte[] biometricTemplate,
        string transactionData)
    {
        // Input validations
        if (seed == null || seed.Length == 0)
            throw new ArgumentException("Seed cannot be null or empty.", nameof(seed));
        if (biometricTemplate == null || biometricTemplate.Length == 0)
            throw new ArgumentException("Biometric template cannot be null or empty.", nameof(biometricTemplate));
        if (string.IsNullOrWhiteSpace(transactionData))
            throw new ArgumentException("Transaction data cannot be null or empty.", nameof(transactionData));

        // Convert transaction data to UTF-8 bytes
        byte[] transactionBytes = Encoding.UTF8.GetBytes(transactionData);

        // Calculate total length: [biometricTemplate][transactionBytes]
        int totalLength = biometricTemplate.Length + transactionBytes.Length;
        byte[] message = new byte[totalLength];

        // Copy the biometric template at the beginning of the buffer
        Buffer.BlockCopy(biometricTemplate, 0, message, 0, biometricTemplate.Length);

        // Copy the transaction bytes right after the biometric template
        Buffer.BlockCopy(transactionBytes, 0, message, biometricTemplate.Length, transactionBytes.Length);

        // Compute HMAC-SHA256 using the seed as key
        byte[] hmacResult;
        using (var hmac = new HMACSHA256(seed))
        {
            hmacResult = hmac.ComputeHash(message);
        }

        // Return the result as a Base64 string (hex could be used instead if preferred)
        return Convert.ToBase64String(hmacResult);
    }
}
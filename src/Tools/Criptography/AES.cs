using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Tools.Criptography;

    /// <summary>
    /// Symmetric encryption using AES and passphrase.
    /// </summary>
    public class AES
    {
        #region Decrypt
        

        /// <summary>
        /// Decrypt the input string.
        /// </summary>
        /// <param name="input">Input string.</param>
        /// <param name="passphrase">Passphrase to use.</param>
        /// <param name="keySize">Key size to set.</param>
        /// <returns>Decrypted string.</returns>
        public static string Decrypt(
            string input,
            string passphrase)
        {
            byte[] passwordBytes = Encoding.UTF8.GetBytes(passphrase);
            byte[] aesKey = SHA256.Create().ComputeHash(passwordBytes);
            byte[] aesIV = MD5.Create().ComputeHash(passwordBytes);

            return DecryptByte(Convert.FromBase64String(input), aesKey, aesIV);
        }

        public static string DecryptByte(byte[] ciphertext, byte[] key, byte[] iv)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = key;
                aesAlg.IV = iv;
                ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV);
                byte[] decryptedBytes;
                using (var msDecrypt = new System.IO.MemoryStream(ciphertext))
                {
                    using (var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                    {
                        using (var msPlain = new System.IO.MemoryStream())
                        {
                            csDecrypt.CopyTo(msPlain);
                            decryptedBytes = msPlain.ToArray();
                        }
                    }
                }
                return Encoding.UTF8.GetString(decryptedBytes);
            }
        }

        #endregion

        #region Encrypt

        public static string Encrypt(
            string input,
            string passphrase)
        {
            
            byte[] passwordBytes = Encoding.UTF8.GetBytes(passphrase);
            byte[] aesKey = SHA256.Create().ComputeHash(passwordBytes);
            byte[] aesIV = MD5.Create().ComputeHash(passwordBytes);

            return Convert.ToBase64String(EncryptByte(input, aesKey, aesIV));
            
        }
        
        static byte[] EncryptByte(string plainText, byte[] Key, byte[] IV) {
            byte[] encrypted;
            // Create a new AesManaged.
            using(var aes = Aes.Create()) {
                // Create encryptor
                ICryptoTransform encryptor = aes.CreateEncryptor(Key, IV);
                // Create MemoryStream
                using(MemoryStream ms = new MemoryStream()) {
                    // Create crypto stream using the CryptoStream class. This class is the key to encryption
                    // and encrypts and decrypts data from any given stream. In this case, we will pass a memory stream
                    // to encrypt
                    using(CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write)) {
                        // Create StreamWriter and write data to a stream
                        using(StreamWriter sw = new StreamWriter(cs))
                            sw.Write(plainText);
                        encrypted = ms.ToArray();
                    }
                }
            }
            // Return encrypted data
            return encrypted;
        }


        /// <summary>
        /// Encrypt the input string.
        /// </summary>
        /// <param name="stream">Input stream.</param>
        /// <param name="passphrase">Passphrase to use.</param>
        /// <param name="keySize">Key size to set.</param>
        /// <returns>Encrypted string.</returns>
        public static string EncryptStream(
            MemoryStream stream,
            string passphrase)
        {
            stream.Position = 0; // Reset the stream position to the beginning
            using (StreamReader reader = new StreamReader(stream))
            {
                string text = reader.ReadToEnd();
                // Now 'text' contains the string representation of the data in the memoryStream

                return Encrypt(text, passphrase);
            }
        }

        #endregion

        #region Helper functions

        /// <summary>
        /// Create a MD5 hash of the input string.
        /// </summary>
        /// <param name="input">Input string.</param>
        /// <returns>Created MD5 hash.</returns>
        private static byte[] CreateMd5Hash(string input)
        {
            using var md5 = MD5.Create();

            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = md5.ComputeHash(inputBytes);
            var sb = new StringBuilder();

            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        #endregion
    }
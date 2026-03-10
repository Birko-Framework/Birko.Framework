using System;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Security.Encryption;

/// <summary>
/// AES-256-GCM encryption. No external NuGet dependencies.
/// Output format: [12-byte nonce][16-byte tag][ciphertext]
/// </summary>
public class AesEncryptionProvider : IEncryptionProvider
{
    private const int NonceSize = 12;  // 96-bit nonce (GCM standard)
    private const int TagSize = 16;    // 128-bit authentication tag
    private const int KeySize = 32;    // 256-bit key

    public byte[] Encrypt(byte[] data, byte[] key)
    {
        ValidateKey(key);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[data.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, data, ciphertext, tag);

        // Output: [nonce][tag][ciphertext]
        var result = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize + TagSize, ciphertext.Length);
        return result;
    }

    public byte[] Decrypt(byte[] encryptedData, byte[] key)
    {
        ValidateKey(key);

        if (encryptedData.Length < NonceSize + TagSize)
            throw new CryptographicException("Encrypted data is too short.");

        var nonce = encryptedData.AsSpan(0, NonceSize);
        var tag = encryptedData.AsSpan(NonceSize, TagSize);
        var ciphertext = encryptedData.AsSpan(NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }

    public string EncryptString(string plaintext, byte[] key)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = Encrypt(data, key);
        return Convert.ToBase64String(encrypted);
    }

    public string DecryptString(string encrypted, byte[] key)
    {
        var data = Convert.FromBase64String(encrypted);
        var decrypted = Decrypt(data, key);
        return Encoding.UTF8.GetString(decrypted);
    }

    /// <summary>
    /// Generates a random 256-bit key suitable for AES-256-GCM.
    /// </summary>
    public static byte[] GenerateKey()
    {
        return RandomNumberGenerator.GetBytes(KeySize);
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != KeySize)
            throw new ArgumentException($"Key must be exactly {KeySize} bytes (256 bits) for AES-256-GCM.", nameof(key));
    }
}

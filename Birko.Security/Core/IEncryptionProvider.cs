namespace Birko.Security;

/// <summary>
/// Encrypts and decrypts data using symmetric encryption.
/// </summary>
public interface IEncryptionProvider
{
    /// <summary>
    /// Encrypts plaintext data. Output includes nonce/IV and authentication tag.
    /// </summary>
    byte[] Encrypt(byte[] data, byte[] key);

    /// <summary>
    /// Decrypts data previously encrypted with <see cref="Encrypt"/>.
    /// </summary>
    byte[] Decrypt(byte[] encryptedData, byte[] key);

    /// <summary>
    /// Encrypts a string and returns a base64-encoded result.
    /// </summary>
    string EncryptString(string plaintext, byte[] key);

    /// <summary>
    /// Decrypts a base64-encoded string previously encrypted with <see cref="EncryptString"/>.
    /// </summary>
    string DecryptString(string encrypted, byte[] key);
}

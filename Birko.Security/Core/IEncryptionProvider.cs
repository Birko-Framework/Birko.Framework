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

    /// <summary>
    /// A new random key of the size and kind <see cref="Encrypt"/> and <see cref="Decrypt"/> require.
    /// </summary>
    /// <remarks>
    /// SH-L230: the only way to make one was a static on the concrete class (<c>AesEncryptionProvider.GenerateKey()</c>),
    /// so a caller injected with this interface had to cast or hand-roll the bytes, knowing the implementation.
    /// </remarks>
    byte[] CreateKey();
}

using System;

namespace Birko.MessageQueue.Serialization
{
    /// <summary>
    /// Decorator that encrypts/decrypts message payloads around an inner serializer.
    /// Accepts encrypt/decrypt functions so it stays independent of any specific encryption library.
    ///
    /// Usage with Birko.Security:
    /// <code>
    /// var aes = new AesEncryptionProvider();
    /// var key = AesEncryptionProvider.GenerateKey();
    /// var serializer = new EncryptingMessageSerializer(
    ///     new JsonMessageSerializer(),
    ///     plaintext => aes.EncryptString(plaintext, key),
    ///     ciphertext => aes.DecryptString(ciphertext, key));
    /// </code>
    /// </summary>
    public class EncryptingMessageSerializer : IMessageSerializer
    {
        private readonly IMessageSerializer _inner;
        private readonly Func<string, string> _encrypt;
        private readonly Func<string, string> _decrypt;

        /// <summary>
        /// Creates an encrypting serializer wrapping an inner serializer.
        /// </summary>
        /// <param name="inner">The serializer to wrap (e.g., JsonMessageSerializer).</param>
        /// <param name="encrypt">Function that encrypts a plaintext string and returns ciphertext (e.g., base64-encoded).</param>
        /// <param name="decrypt">Function that decrypts ciphertext back to plaintext.</param>
        public EncryptingMessageSerializer(IMessageSerializer inner, Func<string, string> encrypt, Func<string, string> decrypt)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _encrypt = encrypt ?? throw new ArgumentNullException(nameof(encrypt));
            _decrypt = decrypt ?? throw new ArgumentNullException(nameof(decrypt));
        }

        public string ContentType => _inner.ContentType + "+encrypted";

        public string Serialize(object payload)
        {
            var json = _inner.Serialize(payload);
            return _encrypt(json);
        }

        public object? Deserialize(string data, Type type)
        {
            var json = _decrypt(data);
            return _inner.Deserialize(json, type);
        }

        public T? Deserialize<T>(string data) where T : class
        {
            var json = _decrypt(data);
            return _inner.Deserialize<T>(json);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Security.Encryption;

/// <summary>
/// Encrypts one field value at rest (an API key in a database row) under a key derived from a master secret
/// (TASK-532). Replaces what DraCode's <c>ProviderKeyCipher</c> did, without its three defects: the key was
/// <c>SHA256(master)</c>, a hash and not a key-derivation function; a ciphertext carried no key id, so rotating the master
/// made every stored value unreadable; and there was no associated data, so a ciphertext could be copied into another
/// row and decrypt there.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys.</b> Each master secret has a key id. The AES-256 key is <c>HKDF-SHA256(master, info: "Birko.FieldCipher/v1/"
/// + keyId)</c>. A master must be at least <see cref="MinimumMasterSecretBytes"/> bytes and random
/// (<see cref="GenerateMasterSecret"/>); HKDF stretches entropy, it does not create it, so a passphrase is refused by
/// length rather than accepted weakly.
/// </para>
/// <para>
/// <b>Envelope.</b> <c>bfc1:{keyId}:{base64url(nonce ‖ ciphertext ‖ tag)}</c>, AES-GCM with a 96-bit random nonce and a
/// 128-bit tag. The authenticated data is the header <c>bfc1:{keyId}</c>, a zero byte, and the caller's associated data,
/// so neither the key id nor the context can be changed without the tag failing.
/// </para>
/// <para>
/// <b>Associated data</b> names where the value belongs, for example <c>"provider-config/{rowId}/apiKey"</c>; decrypting
/// with any other associated data throws. It is not secret and is not stored.
/// </para>
/// <para>
/// <b>Rotation.</b> Encryption uses <see cref="CurrentKeyId"/>; decryption uses the key id in the envelope, so a cipher
/// holding the old and the new key reads both. <see cref="NeedsReencryption"/> / <see cref="Reencrypt"/> move a value to
/// the current key.
/// </para>
/// </remarks>
public sealed class FieldCipher
{
    /// <summary>The envelope's version prefix.</summary>
    public const string Prefix = "bfc1";

    /// <summary>The shortest master secret accepted: 256 bits.</summary>
    public const int MinimumMasterSecretBytes = 32;

    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly Regex KeyIdPattern = new(@"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, byte[]> _keys = new(StringComparer.Ordinal);

    /// <summary>The key id new values are encrypted under.</summary>
    public string CurrentKeyId { get; }

    /// <summary>Every key id this cipher can decrypt.</summary>
    public IReadOnlyCollection<string> KeyIds => _keys.Keys;

    /// <param name="currentKeyId">The key id to encrypt under; must be one of <paramref name="masterSecrets"/>.</param>
    /// <param name="masterSecrets">Every known master secret by key id: the current one and any still to be read.</param>
    /// <exception cref="ArgumentException">A key id is malformed, the current one is missing, or a master is too short.</exception>
    public FieldCipher(string currentKeyId, IReadOnlyDictionary<string, byte[]> masterSecrets)
    {
        ArgumentNullException.ThrowIfNull(currentKeyId);
        ArgumentNullException.ThrowIfNull(masterSecrets);
        foreach (var (keyId, master) in masterSecrets)
        {
            RequireKeyId(keyId);
            if (master is null || master.Length < MinimumMasterSecretBytes)
                throw new ArgumentException(
                    $"The master secret for key id '{keyId}' is {master?.Length ?? 0} bytes; at least {MinimumMasterSecretBytes} random bytes are required (FieldCipher.GenerateMasterSecret).",
                    nameof(masterSecrets));
            _keys[keyId] = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32,
                salt: null, info: Encoding.UTF8.GetBytes("Birko.FieldCipher/v1/" + keyId));
        }
        RequireKeyId(currentKeyId);
        if (!_keys.ContainsKey(currentKeyId))
            throw new ArgumentException($"No master secret is given for the current key id '{currentKeyId}'.", nameof(currentKeyId));
        CurrentKeyId = currentKeyId;
    }

    /// <summary>
    /// Builds a cipher from master secrets stored in <paramref name="secrets"/> under <c>{secretNamePrefix}{keyId}</c>,
    /// each a base64 string of at least <see cref="MinimumMasterSecretBytes"/> bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">A listed secret is missing or empty, or not base64. There is no fallback.</exception>
    public static async Task<FieldCipher> FromSecretProviderAsync(ISecretProvider secrets, string currentKeyId,
        IEnumerable<string>? previousKeyIds = null, string secretNamePrefix = "field-cipher-", CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(currentKeyId);
        var masters = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var keyId in new[] { currentKeyId }.Concat(previousKeyIds ?? []).Distinct(StringComparer.Ordinal))
        {
            var name = secretNamePrefix + keyId;
            var value = await secrets.GetSecretAsync(name, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException($"The field-cipher master secret '{name}' is not configured.");
            try
            {
                masters[keyId] = Convert.FromBase64String(value);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"The field-cipher master secret '{name}' is not base64.", ex);
            }
        }
        return new FieldCipher(currentKeyId, masters);
    }

    /// <summary>A new random master secret, base64-encoded, for a secret store.</summary>
    public static string GenerateMasterSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(MinimumMasterSecretBytes));

    /// <summary>Encrypts <paramref name="plaintext"/> under the current key, bound to <paramref name="associatedData"/>.</summary>
    public string Encrypt(string plaintext, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(associatedData);
        var header = $"{Prefix}:{CurrentKeyId}";
        var data = Encoding.UTF8.GetBytes(plaintext);
        var payload = new byte[NonceSize + data.Length + TagSize];
        var nonce = payload.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(_keys[CurrentKeyId], TagSize))
        {
            aes.Encrypt(nonce, data, payload.AsSpan(NonceSize, data.Length), payload.AsSpan(NonceSize + data.Length, TagSize),
                Aad(header, associatedData));
        }
        return header + ":" + Base64UrlEncode(payload);
    }

    /// <summary>Decrypts an envelope made by <see cref="Encrypt"/> with the same <paramref name="associatedData"/>.</summary>
    /// <exception cref="FormatException">Not a <c>bfc1</c> envelope.</exception>
    /// <exception cref="CryptographicException">An unknown key id, a different associated data, or a tampered envelope.</exception>
    public string Decrypt(string envelope, string associatedData)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(associatedData);
        var (keyId, payload) = Parse(envelope);
        if (!_keys.TryGetValue(keyId, out var key))
            throw new CryptographicException($"The value was encrypted under key id '{keyId}', which this cipher does not hold.");
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("The encrypted value is too short.");
        var length = payload.Length - NonceSize - TagSize;
        var plaintext = new byte[length];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize, length), payload.AsSpan(NonceSize + length, TagSize),
                plaintext, Aad($"{Prefix}:{keyId}", associatedData));
        }
        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>True when <paramref name="value"/> is an envelope of this format (so a stored plaintext can be told apart).</summary>
    public static bool IsEnvelope(string? value) => value != null && value.StartsWith(Prefix + ":", StringComparison.Ordinal);

    /// <summary>The key id an envelope was encrypted under.</summary>
    public static string GetKeyId(string envelope) => Parse(envelope).KeyId;

    /// <summary>True when <paramref name="envelope"/> is under a key other than <see cref="CurrentKeyId"/>.</summary>
    public bool NeedsReencryption(string envelope) => GetKeyId(envelope) != CurrentKeyId;

    /// <summary>Decrypts under its own key and encrypts again under the current one.</summary>
    public string Reencrypt(string envelope, string associatedData) => Encrypt(Decrypt(envelope, associatedData), associatedData);

    private static (string KeyId, byte[] Payload) Parse(string envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var parts = envelope.Split(':');
        if (parts.Length != 3 || parts[0] != Prefix || !KeyIdPattern.IsMatch(parts[1]))
            throw new FormatException($"Not a {Prefix} field-cipher envelope.");
        try
        {
            return (parts[1], Base64UrlDecode(parts[2]));
        }
        catch (FormatException ex)
        {
            throw new FormatException($"Not a {Prefix} field-cipher envelope: the payload is not base64url.", ex);
        }
    }

    private static byte[] Aad(string header, string associatedData)
        => Encoding.UTF8.GetBytes(header + "\0" + associatedData);

    private static void RequireKeyId(string keyId)
    {
        if (keyId is null || !KeyIdPattern.IsMatch(keyId))
            throw new ArgumentException($"Key id '{keyId}' must be 1-64 characters of A-Z, a-z, 0-9, '_' or '-'.", nameof(keyId));
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("Invalid base64url length.");
        }
        return Convert.FromBase64String(s);
    }
}

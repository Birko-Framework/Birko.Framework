using System;
using System.Security.Cryptography;

namespace Birko.Random;

/// <summary>
/// Generates cryptographically secure tokens for API keys, reset tokens, and session identifiers.
/// </summary>
public static class TokenGenerator
{
    private const string HexAlphabet = "0123456789abcdef";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const string UrlSafeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    /// <summary>
    /// Generates a hex-encoded token (e.g., for API keys).
    /// </summary>
    /// <param name="byteLength">Number of random bytes. Output is 2x this length.</param>
    public static string NewHex(int byteLength = 32)
    {
        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "Byte length must be positive.");
        }

        Span<byte> bytes = stackalloc byte[byteLength];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Generates a Base64 URL-safe token (no padding).
    /// </summary>
    /// <param name="byteLength">Number of random bytes.</param>
    public static string NewBase64Url(int byteLength = 32)
    {
        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "Byte length must be positive.");
        }

        Span<byte> bytes = stackalloc byte[byteLength];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Generates a URL-safe token using a 64-character alphabet.
    /// </summary>
    /// <param name="length">Token length in characters.</param>
    public static string NewUrlSafe(int length = 43)
    {
        return GenerateFromAlphabet(UrlSafeAlphabet, length);
    }

    /// <summary>
    /// Generates a token from a custom alphabet.
    /// </summary>
    public static string NewCustom(string alphabet, int length)
    {
        if (string.IsNullOrEmpty(alphabet))
        {
            throw new ArgumentException("Alphabet must not be empty.", nameof(alphabet));
        }

        return GenerateFromAlphabet(alphabet, length);
    }

    /// <summary>
    /// Generates a prefixed API key (e.g., "sk_live_...").
    /// </summary>
    /// <param name="prefix">Prefix to prepend (e.g., "sk_live").</param>
    /// <param name="byteLength">Number of random bytes for the key portion.</param>
    public static string NewApiKey(string prefix, int byteLength = 24)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            throw new ArgumentException("Prefix must not be empty.", nameof(prefix));
        }

        var token = NewBase64Url(byteLength);
        return $"{prefix}_{token}";
    }

    private static string GenerateFromAlphabet(string alphabet, int length)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");
        }

        int mask = (2 << (int)Math.Floor(Math.Log(alphabet.Length - 1) / Math.Log(2))) - 1;
        int step = (int)Math.Ceiling(1.6 * mask * length / alphabet.Length);

        Span<byte> bytes = stackalloc byte[step];
        Span<char> result = stackalloc char[length];
        int count = 0;

        while (count < length)
        {
            RandomNumberGenerator.Fill(bytes);
            for (int i = 0; i < step && count < length; i++)
            {
                int index = bytes[i] & mask;
                if (index < alphabet.Length)
                {
                    result[count++] = alphabet[index];
                }
            }
        }

        return new string(result);
    }
}

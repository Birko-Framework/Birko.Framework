using System;
using System.Security.Cryptography;

namespace Birko.Random;

/// <summary>
/// Generates URL-safe, compact, unique identifiers (NanoID).
/// Default alphabet: A-Za-z0-9_- (64 chars), default size: 21 characters.
/// Collision probability comparable to UUID v4 at default settings.
/// </summary>
public static class NanoIdGenerator
{
    public const string DefaultAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";
    public const int DefaultSize = 21;

    /// <summary>
    /// Generates a NanoID with default alphabet and size.
    /// </summary>
    public static string New() => New(DefaultAlphabet, DefaultSize);

    /// <summary>
    /// Generates a NanoID with the specified size and default alphabet.
    /// </summary>
    public static string New(int size) => New(DefaultAlphabet, size);

    /// <summary>
    /// Generates a NanoID with the specified alphabet and size.
    /// </summary>
    public static string New(string alphabet, int size)
    {
        if (string.IsNullOrEmpty(alphabet))
        {
            throw new ArgumentException("Alphabet must not be empty.", nameof(alphabet));
        }

        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Size must be positive.");
        }

        if (alphabet.Length > 256)
        {
            throw new ArgumentException("Alphabet must not exceed 256 characters.", nameof(alphabet));
        }

        // Calculate mask for uniform distribution over alphabet
        int mask = (2 << (int)Math.Floor(Math.Log(alphabet.Length - 1) / Math.Log(2))) - 1;
        int step = (int)Math.Ceiling(1.6 * mask * size / alphabet.Length);

        Span<byte> bytes = stackalloc byte[step];
        Span<char> result = stackalloc char[size];
        int count = 0;

        while (count < size)
        {
            RandomNumberGenerator.Fill(bytes);

            for (int i = 0; i < step && count < size; i++)
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

    /// <summary>
    /// Generates a NanoID using a custom <see cref="IRandomProvider"/> for testability.
    /// </summary>
    public static string New(IRandomProvider provider, string alphabet, int size)
    {
        if (provider == null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        if (string.IsNullOrEmpty(alphabet))
        {
            throw new ArgumentException("Alphabet must not be empty.", nameof(alphabet));
        }

        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Size must be positive.");
        }

        int mask = (2 << (int)Math.Floor(Math.Log(alphabet.Length - 1) / Math.Log(2))) - 1;
        int step = (int)Math.Ceiling(1.6 * mask * size / alphabet.Length);

        Span<byte> bytes = stackalloc byte[step];
        Span<char> result = stackalloc char[size];
        int count = 0;

        while (count < size)
        {
            provider.NextBytes(bytes);

            for (int i = 0; i < step && count < size; i++)
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

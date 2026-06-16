using System;
using System.Security.Cryptography;

namespace Birko.Security.OAuth.Server.Internal;

/// <summary>
/// Helpers for generating cryptographically random tokens, codes, and identifiers.
/// </summary>
internal static class RandomStringGenerator
{
    /// <summary>
    /// Generates a base64url-encoded string from <paramref name="byteLength"/> random bytes.
    /// 32 bytes → 43 chars, suitable for authorization codes / refresh tokens.
    /// </summary>
    public static string Base64Url(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Generates a user-visible code with characters chosen to be unambiguous
    /// (no 0/O, no 1/I/L). Used for RFC 8628 user codes.
    /// </summary>
    public static string UserCode(int length = 8)
    {
        const string alphabet = "BCDFGHJKMNPQRSTVWXYZ23456789";
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }
        return new string(chars);
    }
}

using System;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Security.Grants.Internal;

/// <summary>Invitation tokens: 32 random bytes, base64url; stored only as their SHA-256.</summary>
internal static class TokenHasher
{
    public static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Upper-case hex SHA-256 of the token's UTF-8 bytes. Lookups go by this value, so the plaintext is never stored.</summary>
    public static string Hash(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

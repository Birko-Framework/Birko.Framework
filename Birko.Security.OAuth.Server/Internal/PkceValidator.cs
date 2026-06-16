using System;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Security.OAuth.Server.Internal;

/// <summary>
/// Validates PKCE code verifiers against stored code challenges per RFC 7636.
/// </summary>
internal static class PkceValidator
{
    public const string MethodPlain = "plain";
    public const string MethodS256 = "S256";

    /// <summary>
    /// Verifies <paramref name="codeVerifier"/> against <paramref name="codeChallenge"/>
    /// using the specified <paramref name="method"/> (S256 or plain).
    /// </summary>
    public static bool Verify(string codeVerifier, string codeChallenge, string method)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
        {
            return false;
        }

        if (string.Equals(method, MethodPlain, StringComparison.Ordinal))
        {
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(codeVerifier),
                Encoding.ASCII.GetBytes(codeChallenge));
        }

        if (string.Equals(method, MethodS256, StringComparison.Ordinal))
        {
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
            var computed = Base64UrlEncode(hash);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(computed),
                Encoding.ASCII.GetBytes(codeChallenge));
        }

        return false;
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

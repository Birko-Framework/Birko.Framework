using System;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Security.OAuth.Server.Internal;

/// <summary>
/// Hashes and verifies stored client secrets using SHA-256.
/// Client secrets are high-entropy values issued at registration; SHA-256 (rather than
/// PBKDF2/bcrypt) is sufficient and keeps the per-request token-endpoint check cheap.
/// </summary>
internal static class ClientSecretHasher
{
    /// <summary>
    /// Returns the hex-encoded SHA-256 hash of <paramref name="secret"/>.
    /// </summary>
    public static string Hash(string secret)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// Fixed-time comparison of a presented secret against a stored hash.
    /// </summary>
    public static bool Verify(string secret, string storedHash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }
        var presented = Hash(secret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(presented),
            Encoding.ASCII.GetBytes(storedHash));
    }
}

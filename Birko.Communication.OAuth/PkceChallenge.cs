using System;
using System.Security.Cryptography;
using System.Text;

namespace Birko.Communication.OAuth;

/// <summary>
/// Generates PKCE (Proof Key for Code Exchange) challenges for OAuth2 Authorization Code flow.
/// </summary>
public class PkceChallenge
{
    /// <summary>
    /// The code verifier — a random string sent in the token request.
    /// </summary>
    public string CodeVerifier { get; }

    /// <summary>
    /// The code challenge — SHA-256 hash of the verifier, base64url-encoded.
    /// </summary>
    public string CodeChallenge { get; }

    /// <summary>
    /// The code challenge method (always "S256").
    /// </summary>
    public string CodeChallengeMethod => "S256";

    private PkceChallenge(string verifier, string challenge)
    {
        CodeVerifier = verifier;
        CodeChallenge = challenge;
    }

    /// <summary>
    /// Generates a new PKCE challenge pair.
    /// </summary>
    public static PkceChallenge Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(bytes);

        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(challengeBytes);

        return new PkceChallenge(verifier, challenge);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

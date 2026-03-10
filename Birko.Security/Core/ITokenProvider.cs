using System.Collections.Generic;

namespace Birko.Security;

/// <summary>
/// Generates and validates authentication tokens (JWT, opaque, etc.).
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Generates a token containing the specified claims.
    /// </summary>
    TokenResult GenerateToken(IDictionary<string, string> claims, TokenOptions? options = null);

    /// <summary>
    /// Generates a random refresh token (not a JWT — opaque string).
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Validates a token and extracts claims.
    /// </summary>
    TokenValidationResult ValidateToken(string token, TokenOptions? options = null);
}

/// <summary>
/// Result of token generation.
/// </summary>
public class TokenResult
{
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public string? RefreshToken { get; init; }
}

/// <summary>
/// Result of token validation.
/// </summary>
public class TokenValidationResult
{
    public bool IsValid { get; init; }
    public IDictionary<string, string> Claims { get; init; } = new Dictionary<string, string>();
    public string? Error { get; init; }

    public static TokenValidationResult Success(IDictionary<string, string> claims) =>
        new() { IsValid = true, Claims = claims };

    public static TokenValidationResult Failure(string error) =>
        new() { IsValid = false, Error = error };
}

/// <summary>
/// Options for token generation/validation.
/// </summary>
public class TokenOptions
{
    public string Secret { get; set; } = string.Empty;
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public int ExpirationMinutes { get; set; } = 60;
    public int RefreshExpirationDays { get; set; } = 7;
}

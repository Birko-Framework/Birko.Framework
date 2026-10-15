using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// One value per claim type. A type the token carries more than once has its values joined with <c>';'</c>;
    /// <see cref="ClaimValues"/> holds them separately.
    /// </summary>
    public IDictionary<string, string> Claims { get; init; } = new Dictionary<string, string>();

    /// <summary>Every value of every claim type, in token order (SH-M248).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ClaimValues { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public string? Error { get; init; }

    public static TokenValidationResult Success(IDictionary<string, string> claims) =>
        new()
        {
            IsValid = true,
            Claims = claims,
            ClaimValues = claims.ToDictionary(c => c.Key, c => (IReadOnlyList<string>)new[] { c.Value }),
        };

    public static TokenValidationResult Success(IReadOnlyDictionary<string, IReadOnlyList<string>> claimValues) =>
        new()
        {
            IsValid = true,
            Claims = claimValues.ToDictionary(c => c.Key, c => string.Join(";", c.Value)),
            ClaimValues = claimValues,
        };

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

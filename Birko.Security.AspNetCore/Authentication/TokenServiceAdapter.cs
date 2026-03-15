using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Result of structured token generation.
/// </summary>
public sealed record TokenRequest(
    Guid UserId,
    string Email,
    Guid? TenantId = null,
    IReadOnlySet<string>? Roles = null,
    IReadOnlySet<string>? Permissions = null);

/// <summary>
/// Result of structured token validation.
/// </summary>
public sealed record TokenValidationInfo(
    bool IsValid,
    Guid? UserId = null,
    string? Email = null,
    Guid? TenantId = null,
    IReadOnlySet<string>? Roles = null,
    IReadOnlySet<string>? Permissions = null);

/// <summary>
/// Wraps <see cref="ITokenProvider"/> with structured claims for web applications.
/// Translates between typed TokenRequest and generic IDictionary claims.
/// </summary>
public sealed class TokenServiceAdapter
{
    private readonly ITokenProvider _tokenProvider;
    private readonly JwtAuthenticationOptions _options;

    public TokenServiceAdapter(ITokenProvider tokenProvider, JwtAuthenticationOptions options)
    {
        _tokenProvider = tokenProvider;
        _options = options;
    }

    /// <summary>
    /// Generates a JWT access token with structured claims.
    /// </summary>
    public TokenResult GenerateAccessToken(TokenRequest request)
    {
        var claims = new Dictionary<string, string>
        {
            [ClaimTypes.NameIdentifier] = request.UserId.ToString(),
            [ClaimTypes.Email] = request.Email,
        };

        if (request.TenantId.HasValue)
            claims[_options.Claims.TenantIdClaim] = request.TenantId.Value.ToString();

        // Roles and permissions are multi-value — encode as semicolon-separated
        // (ITokenProvider uses IDictionary which doesn't support multi-value keys;
        //  JwtTokenProvider expands these to multiple JWT claims internally)
        if (request.Roles is { Count: > 0 })
            claims[_options.Claims.RoleClaim] = string.Join(";", request.Roles);

        if (request.Permissions is { Count: > 0 })
            claims[_options.Claims.PermissionClaim] = string.Join(";", request.Permissions);

        var tokenOptions = new TokenOptions
        {
            Secret = _options.Secret,
            Issuer = _options.Issuer,
            Audience = _options.EffectiveAudience,
            ExpirationMinutes = _options.ExpirationMinutes,
            RefreshExpirationDays = _options.RefreshExpirationDays,
        };

        return _tokenProvider.GenerateToken(claims, tokenOptions);
    }

    /// <summary>
    /// Generates a random opaque refresh token.
    /// </summary>
    public string GenerateRefreshToken() => _tokenProvider.GenerateRefreshToken();

    /// <summary>
    /// Validates a token and extracts structured user information.
    /// </summary>
    public TokenValidationInfo ValidateToken(string token)
    {
        var tokenOptions = new TokenOptions
        {
            Secret = _options.Secret,
            Issuer = _options.Issuer,
            Audience = _options.EffectiveAudience,
        };

        var result = _tokenProvider.ValidateToken(token, tokenOptions);
        if (!result.IsValid)
            return new TokenValidationInfo(false);

        var userId = result.Claims.TryGetValue(ClaimTypes.NameIdentifier, out var uid) && Guid.TryParse(uid, out var parsedUid)
            ? parsedUid : (Guid?)null;
        result.Claims.TryGetValue(ClaimTypes.Email, out var email);
        var tenantId = result.Claims.TryGetValue(_options.Claims.TenantIdClaim, out var tid) && Guid.TryParse(tid, out var parsedTid)
            ? parsedTid : (Guid?)null;

        var roles = result.Claims.TryGetValue(_options.Claims.RoleClaim, out var r)
            ? r.Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
            : new HashSet<string>();

        var permissions = result.Claims.TryGetValue(_options.Claims.PermissionClaim, out var p)
            ? p.Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
            : new HashSet<string>();

        return new TokenValidationInfo(true, userId, email, tenantId, roles, permissions);
    }
}

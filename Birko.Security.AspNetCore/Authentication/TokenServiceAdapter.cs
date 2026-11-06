using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Result of structured token generation.
/// </summary>
public sealed record TokenRequest(
    Guid UserGuid,
    string Email,
    Guid? TenantGuid = null,
    IReadOnlySet<string>? Roles = null,
    IReadOnlySet<string>? Permissions = null);

/// <summary>
/// Result of structured token validation.
/// </summary>
public sealed record TokenValidationInfo(
    bool IsValid,
    Guid? UserGuid = null,
    string? Email = null,
    Guid? TenantGuid = null,
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
        // SH-L237: unguarded, unlike JwtTokenProvider and OidcIdTokenVerifier; a null failed later, far from here.
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Generates a JWT access token with structured claims.
    /// </summary>
    /// <exception cref="ArgumentException">An empty email, or a role or permission holding <c>','</c> or <c>';'</c>.</exception>
    public TokenResult GenerateAccessToken(TokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        // SH-L237: a null Email reached `new Claim(type, null)` inside the token provider, which named neither.
        if (string.IsNullOrEmpty(request.Email))
            throw new ArgumentException("TokenRequest.Email is required.", nameof(request));
        RequireNoSeparator(request.Roles, "role");
        RequireNoSeparator(request.Permissions, "permission");

        // SH-L218: the registered JWT names JwtClaimNames advertises ("sub", "email"), as Symbio's own issuer writes; the
        // ClaimTypes URIs were written into the token verbatim. Inbound, the JWT handlers map them back to
        // ClaimTypes.NameIdentifier / ClaimTypes.Email, which ClaimMappingOptions' defaults and ValidateToken read.
        var claims = new Dictionary<string, string>
        {
            [JwtClaimNames.UserId] = request.UserGuid.ToString(),
            [JwtClaimNames.Email] = request.Email,
        };

        if (request.TenantGuid.HasValue)
            claims[_options.Claims.TenantGuidClaim] = request.TenantGuid.Value.ToString();

        // Roles and permissions are multi-value — encode as a single semicolon-separated claim value
        // (ITokenProvider uses IDictionary, which doesn't support multi-value keys). CR-L337: JwtTokenProvider
        // does NOT expand these — GenerateToken emits exactly one JWT claim per dictionary key, so the joined
        // string is stored verbatim and consumers must split the claim on ';' to recover the individual values.
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

        // The provider's handler maps "sub" / "email" to the ClaimTypes URIs; a provider that does not map is read too.
        var userId = Claim(result, ClaimTypes.NameIdentifier, JwtClaimNames.UserId) is { } uid && Guid.TryParse(uid, out var parsedUid)
            ? parsedUid : (Guid?)null;
        var email = Claim(result, ClaimTypes.Email, JwtClaimNames.Email);
        var tenantGuid = result.Claims.TryGetValue(_options.Claims.TenantGuidClaim, out var tid) && Guid.TryParse(tid, out var parsedTid)
            ? parsedTid : (Guid?)null;

        var roles = result.Claims.TryGetValue(_options.Claims.RoleClaim, out var r)
            ? r.Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
            : new HashSet<string>();

        var permissions = result.Claims.TryGetValue(_options.Claims.PermissionClaim, out var p)
            ? p.Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet()
            : new HashSet<string>();

        return new TokenValidationInfo(true, userId, email, tenantGuid, roles, permissions);
    }

    private static string? Claim(TokenValidationResult result, string mapped, string registered) =>
        result.Claims.TryGetValue(mapped, out var value) || result.Claims.TryGetValue(registered, out value) ? value : null;

    /// <summary>
    /// SH-L236: roles and permissions travel joined with <c>';'</c>, and the readers split on <c>';'</c>
    /// (<see cref="ValidateToken"/>) or on <c>','</c> and <c>';'</c> (<c>ClaimsCurrentUser</c>,
    /// <c>ResolvedPermissionsCurrentUser</c>). A value holding either character became two grants for one reader and one
    /// for another, so it is refused here rather than corrupted.
    /// </summary>
    private static void RequireNoSeparator(IReadOnlySet<string>? values, string kind)
    {
        if (values == null) return;
        foreach (var value in values)
        {
            if (value.IndexOfAny(Separators) >= 0)
                throw new ArgumentException($"The {kind} '{value}' contains ',' or ';', which separate {kind}s in the token.", nameof(values));
        }
    }

    private static readonly char[] Separators = [',', ';'];
}

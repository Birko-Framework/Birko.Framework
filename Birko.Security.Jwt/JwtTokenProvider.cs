using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Birko.Time;
using Microsoft.IdentityModel.Tokens;

namespace Birko.Security.Jwt;

/// <summary>
/// JWT implementation of ITokenProvider using System.IdentityModel.Tokens.Jwt.
/// The consuming project must reference the Microsoft.AspNetCore.Authentication.JwtBearer
/// or System.IdentityModel.Tokens.Jwt NuGet package.
/// </summary>
public class JwtTokenProvider : ITokenProvider
{
    private readonly TokenOptions _defaultOptions;
    private readonly IDateTimeProvider _clock;

    public JwtTokenProvider(TokenOptions options, IDateTimeProvider? clock = null)
    {
        _defaultOptions = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? new SystemDateTimeProvider();
        EnsureSecretPresent(_defaultOptions);
    }

    // CR-L343: the constructor only validates _defaultOptions.Secret; the per-call 'options' override was
    // unchecked, so an empty override Secret produced an opaque signing exception (GenerateToken) or a
    // masked generic validation failure (ValidateToken) instead of this clear fail-fast.
    private static void EnsureSecretPresent(TokenOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Secret))
            throw new ArgumentException("Secret must be provided.", nameof(options));
    }

    public TokenResult GenerateToken(IDictionary<string, string> claims, TokenOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(claims); // CR-M238: fail fast like the ctor, not a bare NRE from the LINQ projection
        var opts = options ?? _defaultOptions;
        EnsureSecretPresent(opts);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        // CR-L344: read the clock once so iat and exp are derived from a single instant (a custom
        // IDateTimeProvider may return inconsistent values across UtcNow / OffsetUtcNow).
        var now = _clock.OffsetUtcNow;
        var expiresAt = now.AddMinutes(opts.ExpirationMinutes).UtcDateTime;

        var claimsList = claims.Select(c => new Claim(c.Key, c.Value)).ToList();

        // Add standard claims if not present
        if (!claims.ContainsKey(JwtRegisteredClaimNames.Jti))
            claimsList.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        if (!claims.ContainsKey(JwtRegisteredClaimNames.Iat))
            claimsList.Add(new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));

        var token = new JwtSecurityToken(
            issuer: opts.Issuer,
            audience: opts.Audience,
            claims: claimsList,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return new TokenResult
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiresAt
        };
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public TokenValidationResult ValidateToken(string token, TokenOptions? options = null)
    {
        var opts = options ?? _defaultOptions;
        EnsureSecretPresent(opts); // CR-L343: fail fast on a misconfigured override rather than masking it as a generic failure

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Secret));

            var parameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = !string.IsNullOrEmpty(opts.Issuer),
                ValidIssuer = opts.Issuer,
                ValidateAudience = !string.IsNullOrEmpty(opts.Audience),
                ValidAudience = opts.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = handler.ValidateToken(token, parameters, out _);
            var claims = principal.Claims.ToDictionary(c => c.Type, c => c.Value);

            return TokenValidationResult.Success(claims);
        }
        catch (SecurityTokenExpiredException)
        {
            return TokenValidationResult.Failure("Token has expired.");
        }
        catch (SecurityTokenException ex)
        {
            return TokenValidationResult.Failure($"Token validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return TokenValidationResult.Failure($"Unexpected error: {ex.Message}");
        }
    }
}

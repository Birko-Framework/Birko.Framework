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

    // SH-M257: validation needs an issuer and an audience to check. Without them it skipped both checks and
    // accepted any token signed with the secret, whichever service minted it, while AddBirkoJwtBearer always
    // checks both: one token, two answers. Missing ones are a configuration error, so they fail fast like the secret.
    private static void EnsureIssuerAndAudiencePresent(TokenOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
            throw new ArgumentException("Issuer and Audience must be provided to validate a token.", nameof(options));
    }

    public TokenValidationResult ValidateToken(string token, TokenOptions? options = null)
    {
        var opts = options ?? _defaultOptions;
        EnsureSecretPresent(opts); // CR-L343: fail fast on a misconfigured override rather than masking it as a generic failure
        EnsureIssuerAndAudiencePresent(opts);

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Secret));

            var parameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = opts.Issuer,
                ValidateAudience = true,
                ValidAudience = opts.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = handler.ValidateToken(token, parameters, out _);
            // SH-M248: a claim type may repeat (several roles, an aud array, and the inbound mapping folds "sub" into
            // NameIdentifier). ToDictionary threw on the second one, so a valid token was reported invalid. Each type
            // keeps every distinct value: joined with ';' in Claims (the separator TokenServiceAdapter splits roles and
            // permissions on), and as a list in ClaimValues.
            var values = principal.Claims
                .GroupBy(c => c.Type, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList(), StringComparer.Ordinal);

            return TokenValidationResult.Success(values);
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

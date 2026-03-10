using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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

    public JwtTokenProvider(TokenOptions options)
    {
        _defaultOptions = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_defaultOptions.Secret))
            throw new ArgumentException("Secret must be provided.", nameof(options));
    }

    public TokenResult GenerateToken(IDictionary<string, string> claims, TokenOptions? options = null)
    {
        var opts = options ?? _defaultOptions;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(opts.ExpirationMinutes);

        var claimsList = claims.Select(c => new Claim(c.Key, c.Value)).ToList();

        // Add standard claims if not present
        if (!claims.ContainsKey(JwtRegisteredClaimNames.Jti))
            claimsList.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        if (!claims.ContainsKey(JwtRegisteredClaimNames.Iat))
            claimsList.Add(new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));

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

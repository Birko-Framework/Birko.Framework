using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Birko.Security.AspNetCore;

/// <summary>
/// DI extensions for configuring JWT Bearer authentication with Birko.Security.
/// </summary>
public static class JwtBearerExtensions
{
    /// <summary>
    /// Adds JWT Bearer authentication using Birko.Security.Jwt.JwtTokenProvider.
    /// Configures ASP.NET Core Authentication + Authorization middleware.
    /// </summary>
    public static IServiceCollection AddBirkoJwtBearer(
        this IServiceCollection services,
        Action<JwtAuthenticationOptions> configure)
    {
        var options = new JwtAuthenticationOptions();
        configure(options);

        if (string.IsNullOrEmpty(options.Secret))
            throw new ArgumentException("JWT Secret is required.", nameof(configure));

        // Register options as singleton
        services.AddSingleton(options);
        services.AddSingleton(options.Claims);

        // Register ITokenProvider (Birko.Security.Jwt)
        var tokenOptions = new TokenOptions
        {
            Secret = options.Secret,
            Issuer = options.Issuer,
            Audience = options.EffectiveAudience,
            ExpirationMinutes = options.ExpirationMinutes,
            RefreshExpirationDays = options.RefreshExpirationDays,
        };
        services.AddSingleton<ITokenProvider>(new Jwt.JwtTokenProvider(tokenOptions));

        // Register TokenServiceAdapter (structured claims wrapper)
        services.AddSingleton<TokenServiceAdapter>();

        // Configure ASP.NET Core JWT Bearer authentication
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));

        services.AddAuthentication(authOptions =>
        {
            authOptions.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            authOptions.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(jwt =>
        {
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.EffectiveAudience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
            };

            // Allow token from query string for SSE (EventSource can't set headers)
            // and WebSocket connections
            jwt.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var path = context.HttpContext.Request.Path;
                    if (path.StartsWithSegments("/api/sse") || path.StartsWithSegments("/ws"))
                    {
                        var token = context.Request.Query["token"];
                        if (!string.IsNullOrEmpty(token))
                        {
                            context.Token = token;
                        }
                    }
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();

        return services;
    }
}

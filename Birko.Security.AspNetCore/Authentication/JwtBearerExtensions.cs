using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

namespace Birko.Security.AspNetCore;

/// <summary>
/// DI extensions for configuring JWT Bearer authentication with Birko.Security.
/// </summary>
public static class JwtBearerExtensions
{
    /// <summary>The shortest JWT secret <see cref="AddBirkoJwtBearer"/> accepts (SH-L222).</summary>
    public const int MinSecretLength = 32;

    /// <summary>
    /// Adds JWT Bearer authentication using Birko.Security.Jwt.JwtTokenProvider.
    /// Configures ASP.NET Core Authentication + Authorization middleware.
    /// </summary>
    public static IServiceCollection AddBirkoJwtBearer(
        this IServiceCollection services,
        Action<JwtAuthenticationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure); // SH-L227: was a bare NullReferenceException from inside
        var options = new JwtAuthenticationOptions();
        configure(options);

        if (string.IsNullOrEmpty(options.Secret))
            throw new ArgumentException("JWT Secret is required.", nameof(configure));
        // SH-L222: the secret is the HMAC key, and its own doc asks for 32 characters. Only "non-empty" was checked, so
        // a 4-character key passed startup and failed at the first signing (IdentityModel's 128-bit floor), and a
        // 16-31 character one signed with a guessable key.
        if (options.Secret.Length < MinSecretLength)
            throw new ArgumentException(
                $"JWT Secret must be at least {MinSecretLength} characters (it is {options.Secret.Length}).", nameof(configure));

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
        // SH-L228: built when first resolved, so a registered IDateTimeProvider (a test clock, a custom one) stamps iat/exp.
        // Constructed here at registration it always fell back to the system clock.
        services.AddSingleton<ITokenProvider>(sp => new Jwt.JwtTokenProvider(tokenOptions, sp.GetService<Birko.Time.IDateTimeProvider>()));

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
                // SH-L221: only the algorithm the provider signs with (HS256); HS384/HS512 with the same secret validated.
                ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            };

            // A token from the query string for SSE (EventSource can't set headers) and WebSocket connections, and on
            // the configured paths only (SH-M256: it was read on every request).
            jwt.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var token = context.Request.Query["token"];
                    if (!string.IsNullOrEmpty(token) && AcceptsQueryToken(context.HttpContext, options))
                    {
                        context.Token = token;
                    }
                    return Task.CompletedTask;
                }
            };
        });

        services.AddAuthorization();

        return services;
    }

    /// <summary>
    /// Whether a request may carry its bearer token as <c>?token=</c>: it has no <c>Authorization</c> header, and it is
    /// a WebSocket upgrade, asks for <c>text/event-stream</c>, or is on one of
    /// <see cref="JwtAuthenticationOptions.QueryTokenPathPrefixes"/>.
    /// </summary>
    internal static bool AcceptsQueryToken(HttpContext http, JwtAuthenticationOptions options)
    {
        var request = http.Request;
        if (request.Headers.ContainsKey(HeaderNames.Authorization))
            return false;
        if (http.WebSockets.IsWebSocketRequest)
            return true;
        foreach (var accept in request.Headers.Accept)
        {
            if (accept != null && accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        foreach (var prefix in options.QueryTokenPathPrefixes)
        {
            if (!string.IsNullOrWhiteSpace(prefix) && request.Path.StartsWithSegments(new PathString("/" + prefix.Trim().Trim('/')), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}

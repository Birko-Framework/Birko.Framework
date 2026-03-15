using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Security.AspNetCore;

/// <summary>
/// Tenant resolver type for <see cref="BirkoSecurityOptions"/>.
/// </summary>
public enum TenantResolverType
{
    /// <summary>Resolve tenant from X-Tenant-Id header.</summary>
    Header,
    /// <summary>Resolve tenant from request subdomain.</summary>
    Subdomain,
    /// <summary>Custom resolver registered separately.</summary>
    Custom,
}

/// <summary>
/// Top-level options for <see cref="SecurityServiceExtensions.AddBirkoSecurity"/>.
/// </summary>
public class BirkoSecurityOptions
{
    /// <summary>JWT authentication options.</summary>
    public JwtAuthenticationOptions Jwt { get; set; } = new();

    /// <summary>Tenant resolution strategy.</summary>
    public TenantResolverType TenantResolver { get; set; } = TenantResolverType.Header;

    /// <summary>
    /// Base domain for subdomain tenant resolution (e.g. "example.com").
    /// Only used when TenantResolver = Subdomain.
    /// </summary>
    public string? SubdomainBaseDomain { get; set; }

    /// <summary>
    /// Async lookup function for subdomain → TenantInfo.
    /// Only used when TenantResolver = Subdomain.
    /// </summary>
    public Func<string, CancellationToken, Task<TenantInfo?>>? SubdomainLookup { get; set; }

    /// <summary>Enable wildcard "*" permission that grants all access. Default: true.</summary>
    public bool WildcardPermissionEnabled { get; set; } = true;
}

/// <summary>
/// One-line DI registration for Birko security: JWT + CurrentUser + Permissions + Tenant.
/// </summary>
public static class SecurityServiceExtensions
{
    /// <summary>
    /// Registers all Birko security services: JWT Bearer auth, ICurrentUser, IPermissionChecker, ITenantResolver, ITenantContext.
    /// </summary>
    public static IServiceCollection AddBirkoSecurity(
        this IServiceCollection services,
        Action<BirkoSecurityOptions> configure)
    {
        var options = new BirkoSecurityOptions();
        configure(options);

        // JWT Bearer authentication
        services.AddBirkoJwtBearer(jwt =>
        {
            jwt.Secret = options.Jwt.Secret;
            jwt.Issuer = options.Jwt.Issuer;
            jwt.Audience = options.Jwt.Audience;
            jwt.ExpirationMinutes = options.Jwt.ExpirationMinutes;
            jwt.RefreshExpirationDays = options.Jwt.RefreshExpirationDays;
            jwt.ClockSkewSeconds = options.Jwt.ClockSkewSeconds;
            jwt.Claims = options.Jwt.Claims;
        });

        // HttpContextAccessor (needed by ClaimsCurrentUser)
        services.AddHttpContextAccessor();

        // ICurrentUser — reads from JWT claims
        services.AddScoped<ICurrentUser, ClaimsCurrentUser>();

        // IPermissionChecker — reads from ICurrentUser.Permissions (no DB hit)
        services.AddScoped<IPermissionChecker, ClaimsPermissionChecker>();

        // Tenant resolver
        switch (options.TenantResolver)
        {
            case TenantResolverType.Header:
                services.AddScoped<ITenantResolver, HeaderTenantResolver>();
                break;
            case TenantResolverType.Subdomain:
                if (options.SubdomainLookup is null)
                    throw new ArgumentException("SubdomainLookup is required when TenantResolver = Subdomain.", nameof(configure));
                services.AddScoped<ITenantResolver>(_ =>
                    new SubdomainTenantResolver(options.SubdomainLookup, options.SubdomainBaseDomain));
                break;
            case TenantResolverType.Custom:
                // Caller registers ITenantResolver separately
                break;
        }

        // ITenantContext — adapter over Birko.Data.Tenant
        services.AddScoped<Birko.Data.Tenant.Models.ITenantContext>(
            _ => Birko.Data.Tenant.Models.Tenant.Current);
        services.AddScoped<ITenantContext, TenantContextAdapter>();

        return services;
    }
}

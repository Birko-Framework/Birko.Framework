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

    /// <summary>
    /// When true (the default), <c>UseBirkoTenantHeaderGuard()</c> rejects a request that resolves to a
    /// tenant other than the one its JWT was issued for.
    ///
    /// <para>
    /// The name is historical: the guard covers <b>every</b> tenant source, not just <c>X-Tenant-Id</c> —
    /// query-string key, route value, subdomain, custom resolvers and a renamed <c>TenantHeaderName</c>
    /// alike (SH-H048). It reads the resolution each tenant middleware publishes rather than any one
    /// transport.
    /// </para>
    /// <para>
    /// This is secure-by-default deliberately. A resolver accepts any parseable value without comparing it
    /// to the <c>tenant_id</c> claim, and in a typical app the two feed *different* consumers — repository
    /// tenant scoping follows the resolved tenant while permission resolution follows the token. A caller
    /// could therefore authenticate in their own tenant, address <c>{victim}</c>, keep their home-tenant
    /// permissions and point tenant-scoped reads <b>and writes</b> at another tenant.
    /// </para>
    /// <para>
    /// Set false only for an app that genuinely wants uncorrelated tenancy with no token check — and
    /// note that doing so re-opens the above. An opt-<i>in</i> guard was rejected as the design: a check
    /// nobody knows to enable protects nobody, which is exactly how this survived unnoticed.
    /// </para>
    /// </summary>
    public bool RequireTenantHeaderMatchesClaim { get; set; } = true;
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

        // The resolved options, so middleware can read them (e.g. TenantHeaderClaimGuardMiddleware).
        services.AddSingleton(options);

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
